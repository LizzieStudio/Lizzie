using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

/// <summary>
/// <para>The project's records, worked out from the event log.</para>
///
/// <para>
/// Read them through its <see cref="IRecordReader"/>, and <see cref="Watch"/> to rerun code when what it read changes.
/// Each record type gets its store the first time it's read or written, whether the
/// write comes from this player, another player, or a loaded save.
/// </para>
/// </summary>
public partial class RecordService : Node, IRecordReader, IRecordServiceMachinery
{
    public static RecordService Instance { get; private set; }

    // Each record type's store, made on first use.
    private readonly Dictionary<Type, IReplicatedStore> _stores = new();

    private readonly HashSet<Watcher> _watchers = new();

    // which watchers read which query values
    private readonly DependencyIndex<Watcher> _dependencies = new();

    // watchers to rerun at the end of the frame
    private readonly HashSet<Watcher> _dirty = new();

    private readonly List<Listener> _listeners = new();

    // events to tell the listeners about at the end of the frame
    private List<TableEvent> _pendingListenerEvents = new();

    private bool _flushQueued;

    private sealed record Listener(Node Owner, CommandName Name, Action<TableEvent> Handler)
    {
        /// <summary>Whether the local player made <paramref name="e"/> with this listener's command.</summary>
        public bool ShouldHearEvent(TableEvent e) =>
            e.Command == Name && e.Id.source == Snowport.Clock.source;
    }

    public override void _EnterTree()
    {
        Instance = this;
    }

    // After the EventSynchronizer, which is an earlier autoload.
    public override void _Ready()
    {
        EventSynchronizer.Instance.Applied += OnEventApplied;
        JsonNameAttribute.ThrowIfAnyUnnamed();
    }

    /// <summary>
    /// Merges an event into the stores, making the stores for what it writes if they're new,
    /// and keeps it for the listeners.
    /// </summary>
    private void OnEventApplied(TableEvent e)
    {
        var sync = EventSynchronizer.Instance;
        var changed = UndoLog.Changes(sync.Log, e).ToList();
        var types = changed.SelectMany(c => c.Records.Concat(c.Unsets).Select(r => r.GetType()));
        foreach (var type in types)
            if (!_stores.ContainsKey(type))
                AddStore(NewStore(type));

        foreach (var store in _stores.Values)
            store.Apply(sync.Log, changed, sync.BulkLoading);

        if (!sync.BulkLoading)
            ScheduleForListeners(e);
    }

    /// <summary>A new, empty store for records of <paramref name="type"/>, held by the type's <see cref="AtomId"/>.</summary>
    private static IReplicatedStore NewStore(Type type)
    {
        var byId = AtomId.For(type);
        return (IReplicatedStore)
            Activator.CreateInstance(typeof(AtomStore<,>).MakeGenericType(type, byId.IdType), byId);
    }

    private IReplicatedStore AddStore(IReplicatedStore store)
    {
        _stores.Add(store.RecordType, store);
        store.Changed += OnRecordsChanged;
        return store;
    }

    /// <summary>The store for <typeparamref name="T"/>, made empty if nothing has read or written one yet.</summary>
    private IAtomStore<T> StoreOf<T>()
        where T : Replicated => (IAtomStore<T>)StoreOf(typeof(T));

    private IReplicatedStore StoreOf(Type type) =>
        _stores.GetValueOrDefault(type) ?? AddStore(NewStore(type));

    /// <inheritdoc/>
    void IRecordServiceMachinery.Clear()
    {
        foreach (var store in _stores.Values)
            store.Clear();
    }

    /// <inheritdoc/>
    void IRecordServiceMachinery.FlushBulkLoad()
    {
        foreach (var store in _stores.Values)
            store.FlushBulkLoad();
    }

    #region Writing

    /// <summary>
    /// <para>Writes records to all connected clients, including locally.</para>
    /// <para>
    /// Records are identified by their type's <see cref="Id{T, I}"/>.
    /// If you Write a record with a new id, it is created on all clients.
    /// If you Write a record using an existing id, that record is replaced with the new one.
    /// Write a <see cref="RecordReaderExtensions.Unset"/> to unset one.
    /// </para>
    /// <para>
    /// The <paramref name="changes"/> passed here are bundled in one event which is undone or redone together.
    /// To bundle multiple discrete events into one undo group, use Open, Append, and Close.
    /// </para>
    /// <para>
    /// Commands return their changes from <see cref="Command.Effects"/> instead.
    /// </para>
    /// </summary>
    public void Write(params IEnumerable<IChange> changes)
    {
        var all = changes.ToArray();
        if (all.Length > 0)
            Submit(TableEvent.Now(all));
    }

    /// <summary>
    /// Calls <paramref name="change"/> with the current version of the atom with <paramref name="id"/>,
    /// then passes the result to <see cref="Write"/>. Does nothing if the record is missing or unset.
    ///
    /// <inheritdoc cref="Write"/>
    /// </summary>
    public void Update<T, I>(Id<T, I> byId, I id, Func<T, T> change)
        where T : Replicated
        where I : IComparable<I>
    {
        if (Get(byId, id) is { } record)
            Write(change(record));
    }

    /// <summary>
    /// Writes changes as one event made by <paramref name="command"/>, even if there are none,
    /// so the command's listeners hear it.
    /// </summary>
    public void Write(CommandName command, params IEnumerable<IChange> changes) =>
        Submit(TableEvent.Now(changes.ToArray(), command));

    /// <summary>
    /// Writes changes as one admin event, which belongs to the table rather than a player,
    /// so no one can undo it. Only the host or a solo player can write one. Does nothing without changes.
    /// </summary>
    public void WriteAdmin(params IEnumerable<IChange> changes)
    {
        var all = changes.ToArray();
        if (all.Length > 0)
            Submit(TableEvent.Admin(all));
    }

    /// <summary>
    /// <para>
    /// Starts a gesture, like a drag, by writing its first event.
    /// Returns the gesture's undo group to be used in Append and Close.
    /// </para>
    /// <para>
    /// Write the gesture's other events with <see cref="Append(SnowportId, IEnumerable{IChange})"/>,
    /// and its last with <see cref="Close(SnowportId, IEnumerable{IChange})"/>.
    /// A not-yet-closed group is skiped by undo and redo events.
    /// A closed group is undone or redone all at once.
    /// </para>
    /// </summary>
    public SnowportId Open(params IEnumerable<IChange> changes) => Open(null, changes);

    /// <inheritdoc cref="Open(IEnumerable{IChange})"/>
    public SnowportId Open(CommandName command, params IEnumerable<IChange> changes) =>
        Open((CommandName?)command, changes);

    private SnowportId Open(CommandName? command, IEnumerable<IChange> changes)
    {
        var e = TableEvent.Now(changes.ToArray(), command);
        e.Group = e.Id;
        Submit(e);
        return e.Id;
    }

    /// <summary>
    /// Writes changes as one event in the open <paramref name="group"/>. Does nothing without changes.
    /// </summary>
    public void Append(SnowportId group, params IEnumerable<IChange> changes) =>
        Append(group, null, changes);

    /// <inheritdoc cref="Append(SnowportId, IEnumerable{IChange})"/>
    public void Append(
        SnowportId group,
        CommandName command,
        params IEnumerable<IChange> changes
    ) => Append(group, (CommandName?)command, changes);

    private void Append(SnowportId group, CommandName? command, IEnumerable<IChange> changes)
    {
        var all = changes.ToArray();
        if (all.Length == 0)
            return;
        var e = TableEvent.Now(all, command);
        e.Group = group;
        Submit(e);
    }

    /// <summary>
    /// Writes the last event of <paramref name="group"/>, which closes it.
    /// Without changes it only closes the group, for a gesture that ended without its usual last event.
    /// </summary>
    public void Close(SnowportId group, params IEnumerable<IChange> changes) =>
        Close(group, null, changes);

    /// <inheritdoc cref="Close(SnowportId, IEnumerable{IChange})"/>
    public void Close(SnowportId group, CommandName command, params IEnumerable<IChange> changes) =>
        Close(group, (CommandName?)command, changes);

    private void Close(SnowportId group, CommandName? command, IEnumerable<IChange> changes)
    {
        var all = changes.ToArray();
        var e = TableEvent.Now(all, command);
        if (group == SnowportId.Empty)
        {
            if (all.Length > 0)
                Submit(e);
            return;
        }
        e.Group = group;
        e.End = GroupEnd.Closed;
        Submit(e);
    }

    /// <summary>
    /// Ends an open <paramref name="group"/> as if it never happened,
    /// but does nothing to an already closed or canceled group.
    /// Its events stop counting, so what they wrote goes back, and undo and redo pass over it.
    /// </summary>
    public void CancelIfOpen(SnowportId group)
    {
        var sync = EventSynchronizer.Instance;
        if (sync != null && UndoLog.IsOpen(sync.Log, group))
            Submit(TableEvent.Canceling(group));
    }

    private static void Submit(TableEvent e) => EventSynchronizer.Instance?.Submit(e);

    /// <inheritdoc/>
    IEnumerable<Replicated> IRecordServiceMachinery.SavedAtoms() =>
        _stores.Values.SelectMany(s => s.SavedAtoms());

    #endregion

    /// <summary>
    /// Runs <paramref name="sync"/> at the end of the frame, then again whenever a record it
    /// read changes, until <paramref name="owner"/> leaves the tree. Call from _EnterTree.
    /// The first run is deferred so callers can configure the node after adding it.
    /// </summary>
    public void Watch(Node owner, Action<IWatchReader> sync)
    {
        var watcher = new Watcher(owner, sync, this, _dependencies);
        _watchers.Add(watcher);
        owner.Connect(
            Node.SignalName.TreeExiting,
            Callable.From(() => Unwatch(watcher)),
            (uint)ConnectFlags.OneShot
        );
        MarkDirty(watcher);
    }

    /// <summary>
    /// Schedules a rerun of the Node's Watch functions at the end of the frame.
    /// Does nothing if the Node is not in the tree.
    /// </summary>
    public void QueueSync(Node owner)
    {
        foreach (var watcher in _watchers.Where(w => w.Owner == owner))
        {
            MarkDirty(watcher);
        }
    }

    private void Unwatch(Watcher watcher)
    {
        _watchers.Remove(watcher);
        _dirty.Remove(watcher);
        _dependencies.Clear(watcher);
    }

    /// <summary>
    /// Calls <paramref name="handler"/> with each event the local player makes with <paramref name="command"/>.
    /// </summary>
    public void Listen(Node owner, Command command, Action<TableEvent> handler) =>
        AddListener(new Listener(owner, command.Name, handler));

    private void AddListener(Listener listener)
    {
        _listeners.Add(listener);
        listener.Owner.Connect(
            Node.SignalName.TreeExiting,
            Callable.From(() => _listeners.Remove(listener)),
            (uint)ConnectFlags.OneShot
        );
    }

    /// <summary>
    /// Keeps an event made by a command, undos and redos included, for the listeners.
    /// </summary>
    private void ScheduleForListeners(TableEvent e)
    {
        if (e.Command == null || _listeners.Count == 0)
            return;
        _pendingListenerEvents.Add(e);
        QueueFlush();
    }

    private void OnRecordsChanged(IReadOnlyList<Touch> touched)
    {
        foreach (var watcher in _dependencies.Affected(touched))
            MarkDirty(watcher);
    }

    void IRecordServiceMachinery.MarkDirty(Watcher watcher) => MarkDirty(watcher);

    private void MarkDirty(Watcher watcher)
    {
        if (_watchers.Contains(watcher))
        {
            _dirty.Add(watcher);
            QueueFlush();
        }
    }

    private void QueueFlush()
    {
        if (_flushQueued)
            return;
        _flushQueued = true;
        Callable.From(Flush).CallDeferred();
    }

    /// <summary>
    /// Runs the dirty watchers, parents before their descendants, so a parent removes or
    /// replaces a child before the child can sync a record that no longer fits it.
    /// Watchers at the same depth run in the order they were registered.
    /// Then tells the listeners about the events, in the order they arrived.
    /// </summary>
    private void Flush()
    {
        _flushQueued = false;

        var batch = _dirty.OrderBy(w => Depth(w.Owner)).ThenBy(w => w.Sequence).ToArray();
        _dirty.Clear();
        foreach (var watcher in batch)
        {
            if (_watchers.Contains(watcher) && IsInstanceValid(watcher.Owner))
                watcher.Run();
        }

        // A handler that makes an event is heard in the next flush.
        var events = _pendingListenerEvents;
        _pendingListenerEvents = new();
        foreach (var e in events)
        {
            foreach (var listener in _listeners.Where(l => l.ShouldHearEvent(e)).ToList())
            {
                if (_listeners.Contains(listener) && IsInstanceValid(listener.Owner))
                    listener.Handler(e);
            }
        }
    }

    private static int Depth(Node node)
    {
        if (!IsInstanceValid(node))
            return 0;

        int depth = 0;
        for (var n = node.GetParent(); n != null; n = n.GetParent())
            depth++;
        return depth;
    }

    #region IRecordReader

    public T Get<T>(SnowTag id)
        where T : Replicated => (T)StoreOf<T>().Find(id).Value;

    public Replicated Get(SnowTag id) =>
        _stores.Values.Select(s => s.Find(id).Value).FirstOrDefault(v => v != null);

    public SnowportId Version(Replicated atom) =>
        StoreOf(atom.GetType()).Find(AtomId.For(atom.GetType()).Of(atom)).LastUpdate;

    public T Get<T, I>(Id<T, I> byId, I id)
        where T : Replicated
        where I : IComparable<I> => (T)StoreOf(byId).Find(id).Value;

    private AtomStore<T, I> StoreOf<T, I>(Id<T, I> byId)
        where T : Replicated
        where I : IComparable<I> => (AtomStore<T, I>)StoreOf<T>();

    public IReadOnlyList<T> Get<T, G>(Group<T, G> group, G key)
        where T : Replicated => StoreOf<T>().InGroup(group, key);

    public int Count<T, G>(Group<T, G> group, G key)
        where T : Replicated => StoreOf<T>().Count(group, key);

    #endregion
}
