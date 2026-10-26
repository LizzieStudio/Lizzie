using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Picks one atom from a list, or none. It's disabled while there's nothing to pick.
///
/// <code>
/// _selector.List(R => R.Get(Dataset.All).Select(d => (d.Id, d.Name)), _datasetRef);
/// </code>
/// </summary>
/// <remarks>
/// It reads what it shows through its own Watch, so it follows the atoms and <see cref="WatchableValue{T}"/>s its functions read.
/// It keeps no selection of its own. The owner stores the pick, through the <see cref="Binding{T}"/> it passes.
/// </remarks>
public partial class AtomSelector : OptionButton
{
    private Func<IWatchReader, IEnumerable<(IAtomRef Id, string Name)>> _items = _ => [];
    private Func<IWatchReader, IAtomRef> _selected = _ => null;
    private Action<IAtomRef> _pick = _ => { };
    private List<IAtomRef> _ids = [];

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    public override void _Ready()
    {
        ItemSelected += index => _pick(_ids[(int)index]);
    }

    /// <summary>
    /// Lists the atoms <paramref name="items"/> reads, by name,
    /// and shows the one <paramref name="selection"/> reads.
    /// An id that isn't listed shows "(none)".
    /// </summary>
    /// <param name="selection">
    /// The selected id. A pick sets an atom's id, or the default id for "(none)".
    /// </param>
    public void List<I>(
        Func<IWatchReader, IEnumerable<(I Id, string Name)>> items,
        Binding<I> selection
    )
        where I : IAtomRef
    {
        _items = R => items(R).Select(item => ((IAtomRef)item.Id, item.Name));
        _selected = R => selection.Get(R);
        _pick = id => selection.Set(id is I typed ? typed : default);
        Atoms.R.QueueSync(this);
    }

    private void Sync(IWatchReader R)
    {
        Clear();
        _ids = [null];
        AddItem("(none)");
        foreach (var (id, name) in _items(R))
        {
            _ids.Add(id);
            AddItem(name);
        }

        Disabled = ItemCount == 1;
        Select(Math.Max(_ids.IndexOf(_selected(R)), 0));
    }
}
