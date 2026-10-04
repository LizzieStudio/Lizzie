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
public partial class RecordService : Node, IRecordReader
{
    public static RecordService Instance { get; private set; }

    // Each record type's store, made on first use.
    private readonly Dictionary<Type, IReplicatedStore> _stores = new();

    private readonly HashSet<Watcher> _watchers = new();

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
    }

    /// <summary>
    /// Merges an event into the stores, making the stores for what it writes if they're new,
    /// and keeps it for the listeners.
    /// </summary>
    private void OnEventApplied(TableEvent e)
    {
        var sync = EventSynchronizer.Instance;
        var changed = UndoLog.Changes(sync.Log, e).ToList();
        foreach (var record in changed.SelectMany(c => c.Records))
            if (!_stores.ContainsKey(record.GetType()))
                AddStore(NewStore(record.GetType()));

        foreach (var store in _stores.Values)
            store.Apply(sync.Log, changed, sync.BulkLoading);

        if (!sync.BulkLoading)
            ScheduleForListeners(e);
    }

    /// <summary>A new, empty store for records of <paramref name="type"/>.</summary>
    private static IReplicatedStore NewStore(Type type) =>
        (IReplicatedStore)
            Activator.CreateInstance(typeof(ReplicatedDictionary<>).MakeGenericType(type));

    private IReplicatedStore AddStore(IReplicatedStore store)
    {
        var type = store.RecordType;
        _stores.Add(type, store);
        store.Changed += changes => OnRecordsChanged(type, changes);
        return store;
    }

    /// <summary>The store for <typeparamref name="T"/>, made empty if nothing has read or written one yet.</summary>
    private S StoreOf<T, S>()
        where S : IReplicatedStore, new() =>
        _stores.GetValueOrDefault(typeof(T)) switch
        {
            null => (S)AddStore(new S()),
            S store => store,
            var other => throw new InvalidOperationException(
                $"{typeof(T).Name} is held in a {other.GetType().Name}, not a {typeof(S).Name}"
            ),
        };

    /// <summary>
    /// Removes every record, e.g. when the project is replaced.
    /// </summary>
    public void Clear()
    {
        foreach (var store in _stores.Values)
            store.Clear();
    }

    /// <summary>
    /// Sends each store's one notification for everything it merged during a bulk load.
    /// </summary>
    public void FlushBulkLoad()
    {
        foreach (var store in _stores.Values)
            store.FlushBulkLoad();
    }

    #region Writing

    /// <summary>
    /// <para>Writes records to all connected clients, including locally.</para>
    /// <para>
    /// Records are identified by their <see cref="Replicated.Id"/>.
    /// If you Write a record with a new Id, it is created on all clients.
    /// If you Write a record using an existing Id, that record is replaced with the new one.
    /// </para>
    /// <para>
    /// The <paramref name="records"/> passed here are bundled in one event which is undone or redone together.
    /// To bundle multiple discrete events into one undo group, use Open, Append, and Close.
    /// </para>
    /// <para>
    /// Commands return their records from <see cref="Command.Effects"/> instead.
    /// </para>
    /// </summary>
    public void Write(params IEnumerable<Replicated> records)
    {
        var all = records.ToArray();
        if (all.Length > 0)
            Submit(TableEvent.Now(all));
    }

    /// <summary>
    /// Writes records as one event made by <paramref name="command"/>, even if there are none,
    /// so the command's listeners hear it.
    /// </summary>
    public void Write(CommandName command, params IEnumerable<Replicated> records) =>
        Submit(TableEvent.Now(records.ToArray(), command));

    /// <summary>
    /// Writes records as one admin event, which belongs to the table rather than a player,
    /// so no one can undo it. Only the host or a solo player can write one. Does nothing without records.
    /// </summary>
    public void WriteAdmin(params IEnumerable<Replicated> records)
    {
        var all = records.ToArray();
        if (all.Length > 0)
            Submit(TableEvent.Admin(all));
    }

    /// <summary>
    /// <para>
    /// Starts a gesture, like a drag, by writing its first event.
    /// Returns the gesture's undo group to be used in Append and Close.
    /// </para>
    /// <para>
    /// Write the gesture's other events with <see cref="Append(SnowportId, IEnumerable{Replicated})"/>,
    /// and its last with <see cref="Close(SnowportId, IEnumerable{Replicated})"/>.
    /// A not-yet-closed group is skiped by undo and redo events.
    /// A closed group is undone or redone all at once.
    /// </para>
    /// </summary>
    public SnowportId Open(params IEnumerable<Replicated> records) => Open(null, records);

    /// <inheritdoc cref="Open(IEnumerable{Replicated})"/>
    public SnowportId Open(CommandName command, params IEnumerable<Replicated> records) =>
        Open((CommandName?)command, records);

    private SnowportId Open(CommandName? command, IEnumerable<Replicated> records)
    {
        var e = TableEvent.Now(records.ToArray(), command);
        e.Group = e.Id;
        Submit(e);
        return e.Id;
    }

    /// <summary>
    /// Writes records as one event in the open <paramref name="group"/>. Does nothing without records.
    /// </summary>
    public void Append(SnowportId group, params IEnumerable<Replicated> records) =>
        Append(group, null, records);

    /// <inheritdoc cref="Append(SnowportId, IEnumerable{Replicated})"/>
    public void Append(
        SnowportId group,
        CommandName command,
        params IEnumerable<Replicated> records
    ) => Append(group, (CommandName?)command, records);

    private void Append(SnowportId group, CommandName? command, IEnumerable<Replicated> records)
    {
        var e = TableEvent.Now(records.ToArray(), command);
        if (e.Records.Length == 0)
            return;
        e.Group = group;
        Submit(e);
    }

    /// <summary>
    /// Writes the last event of <paramref name="group"/>, which closes it.
    /// Without records it only closes the group, for a gesture that ended without its usual last event.
    /// </summary>
    public void Close(SnowportId group, params IEnumerable<Replicated> records) =>
        Close(group, null, records);

    /// <inheritdoc cref="Close(SnowportId, IEnumerable{Replicated})"/>
    public void Close(
        SnowportId group,
        CommandName command,
        params IEnumerable<Replicated> records
    ) => Close(group, (CommandName?)command, records);

    private void Close(SnowportId group, CommandName? command, IEnumerable<Replicated> records)
    {
        var e = TableEvent.Now(records.ToArray(), command);
        if (group == SnowportId.Empty)
        {
            if (e.Records.Length > 0)
                Submit(e);
            return;
        }
        e.Group = group;
        e.Close = true;
        Submit(e);
    }

    private static void Submit(TableEvent e) => EventSynchronizer.Instance?.Submit(e);

    /// <summary>
    /// The current value of everything saved with the project.
    /// </summary>
    public IEnumerable<Replicated> SavedRecords() =>
        _stores.Values.SelectMany(s => s.SavedRecords());

    #endregion

    /// <summary>
    /// Runs <paramref name="sync"/> at the end of the frame, then again whenever a record it
    /// read changes, until <paramref name="owner"/> leaves the tree. Call from _EnterTree.
    /// The first run is deferred so callers can configure the node after adding it.
    /// </summary>
    public void Watch(Node owner, Action<IRecordReader> sync)
    {
        var watcher = new Watcher(owner, sync, this);
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

    /// <summary>
    /// Runs the Node's Watch functions immediately.
    /// Useful if you need to measure the node.
    /// </summary>
    public void SyncNow(Node owner)
    {
        // Copied, since a Sync can add or remove watchers, e.g. a tray adding its display node.
        foreach (var watcher in _watchers.Where(w => w.Owner == owner).ToList())
        {
            _dirty.Remove(watcher);
            watcher.Run();
        }
    }

    private void Unwatch(Watcher watcher)
    {
        _watchers.Remove(watcher);
        _dirty.Remove(watcher);
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

    private void OnRecordsChanged(Type type, IReadOnlyList<RecordChange> changes)
    {
        foreach (var watcher in _watchers)
        {
            if (!_dirty.Contains(watcher) && watcher.Affected(type, changes))
                MarkDirty(watcher);
        }
    }

    public void MarkDirty(Watcher watcher)
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

    private IReadOnlyDictionary<SnowTag, T> RecordsOf<T>()
        where T : Replicated => StoreOf<T, ReplicatedDictionary<T>>().Records;

    public T Get<T>(SnowTag id)
        where T : Replicated => RecordsOf<T>().TryGetValue(id, out var r) && !r.Deleted ? r : null;

    public T GetIncludingDeleted<T>(SnowTag id)
        where T : Replicated => RecordsOf<T>().GetValueOrDefault(id);

    public Replicated Get(SnowTag id) =>
        _stores.Values.Select(s => s.Find(id)).FirstOrDefault(r => r != null)
            is { Deleted: false } r
            ? r
            : null;

    public IReadOnlyList<T> Get<T>(IEnumerable<SnowTag> ids)
        where T : Replicated
    {
        var records = RecordsOf<T>();
        return ids.Select(id => records.GetValueOrDefault(id))
            .Where(r => r != null && !r.Deleted)
            .ToArray();
    }

    public IReadOnlyList<T> Get<T>(Func<T, bool> filter)
        where T : Replicated => RecordsOf<T>().Values.Where(r => !r.Deleted && filter(r)).ToArray();

    public IReadOnlyList<T> GetIncludingDeleted<T>(Func<T, bool> filter)
        where T : Replicated => RecordsOf<T>().Values.Where(filter).ToArray();

    public IReadOnlyList<T> Get<T>()
        where T : Replicated => RecordsOf<T>().Values.Where(r => !r.Deleted).ToArray();

    public T Single<T>()
        where T : Replicated
    {
        var records = Get<T>();
        if (records.Count == 1)
            return records[0];
        throw new InvalidOperationException(
            records.Count == 0
                ? $"There's no {typeof(T).Name}. Table setup (ProjectService.EnsureSingletons) should have created it."
                : $"There are {records.Count} {typeof(T).Name} records, but it's a singleton."
        );
    }

    /// <summary>Always throws, since only a watch has a previous run to compare against.</summary>
    public SwapLists<K> GetChanged<T, K>(Func<IRecordReader, T, K?> keyFn)
        where T : Replicated
        where K : struct
    {
        throw new InvalidOperationException(
            "GetChanged can only be called on the IRecordReader passed to a Watch's Sync."
        );
    }

    /// <summary>Always throws, since only a watch has a previous run to compare against.</summary>
    public SwapLists GetChanged<T>()
        where T : Replicated
    {
        throw new InvalidOperationException(
            "GetChanged can only be called on the IRecordReader passed to a Watch's Sync."
        );
    }

    #endregion
}
