using System;
using System.Collections.Generic;
using Godot;

namespace Lizzie;

/// <summary>
/// Picks one atom from a list, or none. It's disabled while there's nothing to pick.
/// </summary>
/// <remarks>
/// This only works with Atoms that use a <see cref="SnowTag"/> for their Id, which is most of them.
/// I had to do it this way because generic classes can't be Godot scripts.
/// </remarks>
public partial class AtomSelector : OptionButton
{
    private SnowTag _selectedRef = SnowTag.Empty;
    private Func<IWatchReader, IEnumerable<(string Name, SnowTag Id)>> _items = _ => [];

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    public override void _Ready()
    {
        ItemSelected += OnItemSelected;
    }

    /// <summary>
    /// A function which takes an <see cref="IWatchReader"/>
    /// and returns the atoms listed by name and id.
    ///
    /// <code>
    /// _selector.Items = R => R.Get(Dataset.All).Select(d => (d.Name, d.Id));
    /// </code>
    /// </summary>
    public Func<IWatchReader, IEnumerable<(string Name, SnowTag Id)>> Items
    {
        set
        {
            _items = value;
            Atoms.R.QueueSync(this);
        }
    }

    private void Sync(IWatchReader R)
    {
        Clear();
        AddItem("(none)", SnowTag.Empty);

        foreach (var (name, id) in _items(R))
        {
            AddItem(name, id);
        }

        Disabled = ItemCount == 1;
        UpdateSelection();
    }

    /// <summary>
    /// The Id of the selected atom.
    /// </summary>
    public SnowTag SelectedRef
    {
        get => _selectedRef;
        set
        {
            _selectedRef = value;
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        // Before the first sync there's nothing to select; the sync selects it.
        if (ItemCount == 0)
            return;
        var index = GetItemIndex(_selectedRef);
        if (index < 0)
            index = 0;
        Select(index);
    }

    private void OnItemSelected(long index)
    {
        _selectedRef = GetItemId((int)index);
        AtomSelected?.Invoke(_selectedRef);
    }

    public event Action<SnowTag> AtomSelected;
}
