using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Holds the <see cref="ComponentPosition"/>s as each <see cref="Stage"/> shows them.
///
/// <para>
/// A stage shows its own write of a position if it has one, and otherwise what its parent shows.
/// When you read a position from a stage, its <see cref="ComponentPosition.StageRef"/> is set to the stage of the read.
/// </para>
///
/// <para>
/// A removed position hides the component and its child components, but the user can toggle this off.
/// While removed components are shown, a removed position shows as the nearest write above the removal instead,
/// marked <see cref="AtomEntry.Removed"/> along with what's inside it.
/// </para>
///
/// <para>
/// <see cref="Apply"/> only merges the writes.
/// <see cref="Settle"/> works out what each stage shows once the <see cref="Stage"/> atoms have merged the same event.
/// </para>
/// </summary>
public sealed class StagedStore : IAtomStore<ComponentPosition, PositionKey>
{
    private readonly AtomStore<Stage, Ref<Stage>> _stages;
    private readonly AtomStore<Component, Ref<Component>> _components;

    /// <summary>
    /// The positions as written, by the stage they were written in.
    /// </summary>
    private readonly AtomStore<ComponentPosition, PositionKey> _raw = new(ComponentPosition.ById);

    /// <summary>
    /// The positions as each stage shows them.
    /// </summary>
    private readonly IndexedAtoms<ComponentPosition, PositionKey> _resolved = new(
        ComponentPosition.ById
    );

    // each stage's written parent, unless it's deleted
    private readonly Dictionary<Ref<Stage>, Ref<Stage>> _written = new();

    // the parent of each stage that's Stage.Reachable
    private Dictionary<Ref<Stage>, Ref<Stage>> _parents = new();

    // the written positions by the component they're inside, so a change to a component reaches its contents
    private readonly Lookup<ComponentPosition, Ref<Component>> _byContainer = new(c =>
        c.ContainerRef
    );

    // the written positions by their component
    private readonly Lookup<ComponentPosition, Ref<Component>> _byComponent = new(c => c.Id);

    // what changed since the last Settle
    private readonly HashSet<PositionKey> _dirty = new();
    private readonly HashSet<Ref<Stage>> _changedStages = new();

    // touches to report in the next Settle
    private List<Touch> _touches = new();

    /// <param name="stages">The store of <see cref="Stage"/> atoms, which says each stage's parent.</param>
    /// <param name="components">The store of <see cref="Component"/> atoms, which says whether a position has anything to place.</param>
    public StagedStore(
        AtomStore<Stage, Ref<Stage>> stages,
        AtomStore<Component, Ref<Component>> components
    )
    {
        _stages = stages;
        _components = components;
        _raw.Changed += OnRawChanged;
        stages.Changed += OnStagesChanged;
        components.Changed += OnComponentsChanged;
        _changedStages.UnionWith(stages.Ids);
    }

    public Type AtomType => typeof(ComponentPosition);

    public event Action<IReadOnlyList<Touch>> Changed;

    /// <summary>
    /// What the stage in <paramref name="id"/> shows for its position, or an entry with a null value if nothing.
    /// </summary>
    public AtomEntry Find(PositionKey id) => _resolved.Find(id);

    AtomEntry IAtomStore.Find(object id) => id is PositionKey typed ? Find(typed) : default;

    /// <summary>
    /// The positions with <paramref name="key"/> in <paramref name="lookup"/> as written for a <see cref="Written"/>,
    /// and as each stage shows them otherwise.
    /// </summary>
    public List<ComponentPosition> Get<K>(Lookup<ComponentPosition, K> lookup, K key) =>
        lookup is Written ? _raw.Get(lookup, key) : _resolved.Get(lookup, key);

    public int Count<K>(Lookup<ComponentPosition, K> lookup, K key) =>
        lookup is Written ? _raw.Count(lookup, key) : _resolved.Count(lookup, key);

    public void Apply(Timeline timeline, IReadOnlyList<TimelineEvent> changed, bool bulkLoading) =>
        _raw.Apply(timeline, changed, bulkLoading);

    public void FlushBulkLoad() => _raw.FlushBulkLoad();

    private void OnRawChanged(IReadOnlyList<Touch> touches)
    {
        foreach (var touch in touches)
        {
            if (touch.Query == ComponentPosition.ById)
                _dirty.Add((PositionKey)touch.Key);
            else if (touch.Query is Written)
                _touches.Add(touch);
        }
    }

    private void OnStagesChanged(IReadOnlyList<Touch> touches)
    {
        foreach (var touch in touches)
            if (touch.Query == Stage.ById)
                _changedStages.Add((Ref<Stage>)touch.Key);
    }

    private void OnComponentsChanged(IReadOnlyList<Touch> touches)
    {
        foreach (var touch in touches)
        {
            if (touch.Query != Component.ById)
                continue;

            foreach (var written in _raw.Get(_byComponent, (Ref<Component>)touch.Key))
                _dirty.Add(ComponentPosition.ById.Of(written));
        }
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

        var queue = new Queue<PositionKey>(keys.ToHashSet());
        while (queue.TryDequeue(out var key))
        {
            var old = _resolved.Find(key);
            var now = Resolve(key);
            if (now.Value is ComponentPosition { ContainerRef: { IsNone: false } container })
            {
                var shown = _resolved.Find(key with { ComponentRef = container });
                var removed = now.Removed || shown.Removed;
                now = shown.Value == null ? default : now with { Removed = removed };
            }
            if (old == now)
                continue;
            _resolved.Replace(key, now);
            _touches.AddRange(_resolved.Touched(key, old, now));
            if ((old.Value == null) != (now.Value == null) || old.Removed != now.Removed)
            {
                foreach (var inside in _raw.Get(_byContainer, key.ComponentRef))
                    queue.Enqueue(key with { ComponentRef = inside.Id });
            }
        }
        _dirty.Clear();
        _changedStages.Clear();
        Report();
    }

    /// <summary>
    /// Whether reads show removed positions, which is "Show Removed Components".
    /// Setting it settles the store.
    /// </summary>
    public bool ShowsRemoved
    {
        get;
        set
        {
            field = value;
            _dirty.UnionWith(
                _raw.Ids.Where(id => _raw.Find(id).Value is ComponentPosition { Removed: true })
            );
            Settle();
        }
    }

    /// <summary>
    /// Every stage, and every stage something is resolved in, with every component written or resolved.
    /// </summary>
    private IEnumerable<PositionKey> Everything()
    {
        var resolved = _resolved.Entries.Keys;
        var stages = _parents.Keys.Concat(resolved.Select(k => k.StageRef)).ToHashSet();
        var atoms = _raw.Ids.Concat(resolved).Select(k => k.ComponentRef).ToHashSet();
        return stages.SelectMany(s => atoms.Select(c => new PositionKey(s, c)));
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
    /// What the stage in <paramref name="key"/> shows for its position, wherever the position is.
    /// </summary>
    private AtomEntry Resolve(PositionKey key)
    {
        if (_components.Find(key.ComponentRef).Value == null)
            return default;

        bool removed = false;
        for (var stage = key.StageRef; _parents.TryGetValue(stage, out var parent); stage = parent)
        {
            if (
                _raw.Find(key with { StageRef = stage })
                is not { Value: ComponentPosition atom } written
            )
                continue;
            if (!atom.Removed)
            {
                return new AtomEntry(
                    atom with
                    {
                        StageRef = key.StageRef,
                    },
                    written.LastUpdate,
                    removed
                );
            }
            if (!ShowsRemoved)
                return default;
            removed = true;
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
    /// The positions as written, so a save never holds what a stage only inherits.
    /// </summary>
    public IEnumerable<AtomEntry> SavedAtoms(Timeline timeline) => _raw.SavedAtoms(timeline);
}
