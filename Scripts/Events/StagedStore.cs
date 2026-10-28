using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Holds all <see cref="StagedAtom"/> of type <typeparamref name="T"/> as each <see cref="Stage"/> shows them.
///
/// <para>
/// A stage shows its own write of an atom if it has one, and otherwise what its parent shows.
/// A write with <see cref="StagedAtom.Removed"/> hides the parent atom.
/// When you read a staged atom from a stage, its <see cref="StagedAtom.StageRef"/> is set to the stage of the read.
/// </para>
///
/// <para>
/// <see cref="Apply"/> only merges the writes.
/// <see cref="Settle"/> works out what each stage shows once the <see cref="Stage"/> atoms have merged the same event.
/// </para>
/// </summary>
public sealed class StagedStore<T> : IAtomStore<T, StageKey<T>>
    where T : StagedAtom
{
    private readonly StagedId<T> _byId;

    private readonly AtomStore<Stage, Ref<Stage>> _stages;

    /// <summary>
    /// The atoms as written, by the stage they were written in.
    /// </summary>
    private readonly AtomStore<T, StageKey<T>> _raw;

    /// <summary>
    /// The atoms as each stage shows them.
    /// </summary>
    private readonly IndexedAtoms<T, StageKey<T>> _resolved;

    // each stage's written parent, unless it's deleted
    private readonly Dictionary<Ref<Stage>, Ref<Stage>> _written = new();

    // the parent of each stage that's Stage.Reachable
    private Dictionary<Ref<Stage>, Ref<Stage>> _parents = new();

    // what changed since the last Settle
    private readonly HashSet<StageKey<T>> _dirty = new();
    private readonly HashSet<Ref<Stage>> _changedStages = new();

    // touches to report in the next Settle
    private List<Touch> _touches = new();

    /// <param name="byId">The type's Id.</param>
    /// <param name="stages">The store of <see cref="Stage"/> atoms, which says each stage's parent.</param>
    public StagedStore(StagedId<T> byId, AtomStore<Stage, Ref<Stage>> stages)
    {
        _byId = byId;
        _stages = stages;
        _raw = new(byId);
        _resolved = new(byId);
        _raw.Changed += OnRawChanged;
        stages.Changed += OnStagesChanged;
        _changedStages.UnionWith(stages.Ids);
    }

    public Type AtomType => typeof(T);

    public event Action<IReadOnlyList<Touch>> Changed;

    /// <summary>
    /// What the stage in <paramref name="id"/> shows for its atom, or an entry with a null value if nothing.
    /// </summary>
    public AtomEntry Find(StageKey<T> id) => _resolved.Find(id);

    AtomEntry IAtomStore.Find(object id) => id is StageKey<T> typed ? Find(typed) : default;

    /// <summary>
    /// The atoms with <paramref name="key"/> in <paramref name="lookup"/> as written for a <see cref="Written{T}"/>,
    /// and as each stage shows them otherwise.
    /// </summary>
    public List<T> Get<K>(Lookup<T, K> lookup, K key) =>
        lookup is Written<T> ? _raw.Get(lookup, key) : _resolved.Get(lookup, key);

    public int Count<K>(Lookup<T, K> lookup, K key) =>
        lookup is Written<T> ? _raw.Count(lookup, key) : _resolved.Count(lookup, key);

    public void Apply(Timeline timeline, IReadOnlyList<TimelineEvent> changed, bool bulkLoading) =>
        _raw.Apply(timeline, changed, bulkLoading);

    public void FlushBulkLoad() => _raw.FlushBulkLoad();

    // The written atoms' id touches say what to resolve again; the rest are Written lookup touches to pass on.
    private void OnRawChanged(IReadOnlyList<Touch> touches)
    {
        foreach (var touch in touches)
            if (touch.Query == _byId)
                _dirty.Add((StageKey<T>)touch.Key);
            else
                _touches.Add(touch);
    }

    private void OnStagesChanged(IReadOnlyList<Touch> touches)
    {
        foreach (var touch in touches)
            if (touch.Query == Stage.ById)
                _changedStages.Add((Ref<Stage>)touch.Key);
    }

    public void Settle()
    {
        // Check for a stage showing up, going, away, or reparenting.
        bool restructured = false;
        if (_changedStages.Count > 0)
        {
            foreach (var id in _changedStages)
                if (_stages.Find(id).Value is Stage { Deleted: false } stage)
                    _written[id] = stage.ParentRef;
                else
                    _written.Remove(id);

            var parents = _written
                .Where(w =>
                    Stage.Reachable(w.Key, s => _written.TryGetValue(s, out var p) ? p : null)
                )
                .ToDictionary();
            restructured =
                parents.Count != _parents.Count
                || parents.Any(p => !_parents.TryGetValue(p.Key, out var old) || old != p.Value);
            _parents = parents;
        }

        var keys = restructured
            ? Everything()
            : _dirty.SelectMany(k => Below(k.StageRef).Select(s => k with { StageRef = s }));

        foreach (var key in keys.ToHashSet())
        {
            var old = _resolved.Find(key);
            var now = Resolve(key);
            if (old == now)
                continue;
            _resolved.Replace(key, now);
            _touches.AddRange(_resolved.Touched(key, old, now));
        }
        _dirty.Clear();
        _changedStages.Clear();
        Report();
    }

    /// <summary>
    /// Every stage, and every stage something is resolved in, with every atom written or resolved.
    /// </summary>
    private IEnumerable<StageKey<T>> Everything()
    {
        var resolved = _resolved.Entries.Keys;
        var stages = _parents.Keys.Concat(resolved.Select(k => k.StageRef)).ToHashSet();
        var atoms = _raw.Ids.Concat(resolved).Select(k => k.ComponentRef).ToHashSet();
        return stages.SelectMany(s => atoms.Select(c => new StageKey<T>(s, c)));
    }

    /// <summary>
    /// <paramref name="root"/> and every stage whose chain of parents reaches it.
    /// </summary>
    private HashSet<Ref<Stage>> Below(Ref<Stage> root)
    {
        var found = new HashSet<Ref<Stage>> { root };
        var queue = new Queue<Ref<Stage>>(found);
        while (queue.TryDequeue(out var stage))
            foreach (var (child, parent) in _parents)
                if (parent == stage && found.Add(child))
                    queue.Enqueue(child);
        return found;
    }

    /// <summary>
    /// What the stage in <paramref name="key"/> shows for its atom.
    /// </summary>
    private AtomEntry Resolve(StageKey<T> key)
    {
        for (var stage = key.StageRef; _parents.TryGetValue(stage, out var parent); stage = parent)
        {
            var written = _raw.Find(key with { StageRef = stage });
            if (written.Value is StagedAtom atom)
                return atom.Removed
                    ? default
                    : new AtomEntry(atom with { StageRef = key.StageRef }, written.LastUpdate);
        }
        return default;
    }

    public void Clear()
    {
        _raw.Clear();
        foreach (var (key, entry) in _resolved.Entries)
            _touches.AddRange(_resolved.Touched(key, entry, default));
        _resolved.Clear();
        _written.Clear();
        _parents.Clear();
        _dirty.Clear();
        _changedStages.Clear();
        Report();
    }

    private void Report()
    {
        if (_touches.Count == 0)
            return;
        var touches = _touches;
        _touches = new();
        Changed?.Invoke(touches);
    }

    /// <summary>
    /// The atoms as written, so a save never holds what a stage only inherits.
    /// </summary>
    /// <remarks>.
    /// A Removed atom is left out when its parent stage shows nothing beneath it,
    /// since keeping a removal isn't necessary when it's not overriding anything.
    /// </remarks>
    public IEnumerable<Atom> SavedAtoms() =>
        _raw.SavedAtoms()
            .Where(a =>
                a is not T { Removed: true } removed
                || _parents.TryGetValue(removed.StageRef, out var parent)
                    && _resolved.Find(_byId.Of(removed) with { StageRef = parent }).Value != null
            );
}
