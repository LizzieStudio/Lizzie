using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// The window for blueprints.
/// </summary>
public partial class BlueprintManager : Window, ICommandView
{
    private Tree _tree;
    private IReadOnlyList<Command> _commands;

    private readonly Dictionary<SnowTag, TreeItem> _rows = new();

    public static readonly SelectionPlace Place = new(SelectionPlaces.Blueprints, SnowTag.Empty);

    private static readonly Texture2D GameIcon = GD.Load<Texture2D>(CommandIcons.NewGame);

    /// <summary>
    /// The font used to mark the stage that is currently on the table.
    /// </summary>
    [Export]
    private Font _shownFont;

    public override void _EnterTree()
    {
        AtomService.Instance.Watch(this, Sync);
    }

    public override void _Ready()
    {
        _tree = GetNode<Tree>("%Tree");
        _tree.CreateItem();
        _tree.SetColumnExpand(1, false);
        foreach (var style in new[] { "selected", "selected_focus" })
            _tree.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        foreach (var style in new[] { "hovered_selected", "hovered_selected_focus" })
            _tree.AddThemeStyleboxOverride(style, _tree.GetThemeStylebox("hovered"));
        _tree.AddThemeColorOverride("font_selected_color", _tree.GetThemeColor("font_color"));
        _tree.AddThemeColorOverride(
            "font_hovered_selected_color",
            _tree.GetThemeColor("font_hovered_color")
        );
        _tree.ItemSelected += () => Select([IdOf(_tree.GetSelected())]);
        _tree.EmptyClicked += (_, _) => Select([]);
        _tree.ItemActivated += () => Rename(IdOf(_tree.GetSelected()));
        _tree.ItemEdited += OnItemEdited;
        _tree.SetDragForwarding(
            Callable.From<Vector2, Variant>(StartDrag),
            Callable.From<Vector2, Variant, bool>((at, data) => Placement(at, data) != null),
            Callable.From<Vector2, Variant>(Drop)
        );

        _commands =
        [
            StageCommands.AddBlueprint,
            new AtomCommand<Stage>
            {
                Name = new("blueprint.rename"),
                Icon = CommandIcons.Rename,
                Caption = "Rename",
                Keys = [Shortcuts.Key(Key.F2)],
                Count = TargetCount.One,
                SideEffects = (stages, _) => Rename(stages[0].Id),
            },
        ];
        CommandList.Register(_commands);

        CloseRequested += QueueFree;
    }

    #region ICommandView

    public SelectionPlace? SelectionScope(IAtomReader R) => Place;

    public bool Shows(IAtomReader R, AtomRef target) => Stage.Reachable(R, (SnowTag)target.Id);

    public IReadOnlyList<Command> Commands => _commands;

    public bool UndoScope(Atom atom)
    {
        if (atom is Stage)
            return true;
        if (atom is Selection s && s.Place == Place)
            return true;

        return false;
    }

    #endregion

    /// <summary>
    /// Updates the rows in place, so collapsed rows, the scroll and an open rename survive changes.
    /// Each blueprint keeps its row while it's reachable, wherever it moves in the tree.
    /// </summary>
    private void Sync(IWatchReader R)
    {
        var selected = R.GetSelection(Stage.ById, this).ToHashSet();
        var colors = R.SelectionColors(Place);
        var onTable = ProjectService.Instance.GameObjects.ShownStage(R);
        var listed = new HashSet<SnowTag>();

        AddChildren(_tree.GetRoot(), SnowTag.Empty);

        // Freeing a row frees the rows under it, so every dead row comes off the tree before any is freed.
        var dead = _rows.Where(r => !listed.Contains(r.Key)).ToList();
        foreach (var (id, item) in dead)
        {
            _rows.Remove(id);
            item.GetParent().RemoveChild(item);
        }
        foreach (var (_, item) in dead)
            item.Free();

        void AddChildren(TreeItem parent, SnowTag parentRef)
        {
            foreach (var (stage, index) in Stage.GetChildren(R, parentRef).Select((s, i) => (s, i)))
            {
                listed.Add(stage.Id);
                // The rows before it are in place already, so the one at its index is the first one after it.
                if (!_rows.TryGetValue(stage.Id, out var item))
                {
                    item = _tree.CreateItem(parent, index);
                    item.SetMetadata(0, stage.Id);
                    item.SetSelectable(1, false);
                    item.SetTextAlignment(1, HorizontalAlignment.Right);
                    // Only untrimmed text widens its column.
                    item.SetTextOverrunBehavior(1, TextServer.OverrunBehavior.NoTrimming);
                    _rows[stage.Id] = item;
                }
                else if (index < parent.GetChildCount())
                {
                    if (parent.GetChild(index) != item)
                        item.MoveBefore(parent.GetChild(index));
                }
                else
                {
                    item.GetParent().RemoveChild(item);
                    parent.AddChild(item);
                }
                item.SetText(0, stage.Name);
                item.SetIcon(0, stage.IsGame ? GameIcon : null);
                item.SetCustomFont(0, stage.Id == onTable ? _shownFont : null);
                int changes = R.Count(ComponentPosition.WrittenIn, stage.Id);
                item.SetText(1, changes.ToString());
                item.SetTooltipText(
                    1,
                    $"{changes} {(changes == 1 ? "change" : "changes")} to parent"
                );
                if (colors.TryGetValue(Stage.ById.Ref(stage.Id), out var color))
                    item.SetCustomBgColor(0, color, justOutline: true);
                else
                    item.ClearCustomBgColor(0);
                // Selecting a row that's already selected would close an open rename.
                if (selected.Contains(stage.Id) != item.IsSelected(0))
                {
                    if (selected.Contains(stage.Id))
                        item.Select(0);
                    else
                        item.Deselect(0);
                }
                AddChildren(item, stage.Id);
            }
        }
    }

    private static SnowTag IdOf(TreeItem item) => (SnowTag)item.GetMetadata(0);

    #region Moving blueprints

    private Variant StartDrag(Vector2 at)
    {
        if (_tree.GetItemAtPosition(at) is not { } item)
            return default;
        _tree.DropModeFlags = (int)(
            Tree.DropModeFlagsEnum.OnItem | Tree.DropModeFlagsEnum.Inbetween
        );
        _tree.SetDragPreview(new Label { Text = item.GetText(0) });
        return new BlueprintDrag { Id = IdOf(item) };
    }

    // We need this for Godot drag and drop.
    private sealed partial class BlueprintDrag : RefCounted
    {
        public SnowTag Id { get; init; }
    }

    /// <summary>
    /// Where a drop at <paramref name="at"/> would move the dragged blueprint.
    /// </summary>
    /// <returns>
    /// A Tuple of:
    /// <list type="number">
    /// <item>The dragged blueprint</item>
    /// <item>The new parent, or <see cref="SnowTag.Empty"/> for the root.</item>
    /// <item>The new siblings</item>
    /// <item>The position among the siblings</item>
    /// </list>
    /// Or null if it can't move there.
    /// </returns>
    private (Stage Moved, SnowTag Parent, List<Stage> Siblings, int Index)? Placement(
        Vector2 at,
        Variant data
    )
    {
        if (data.As<GodotObject>() is not BlueprintDrag drag)
            return null;
        IAtomReader R = AtomService.Instance;
        if (
            R.Get(Stage.ById, drag.Id) is not { } moved
            || !_rows.TryGetValue(moved.Id, out var row)
        )
            return null;
        var item = _tree.GetItemAtPosition(at);
        if (item == row)
            return null;
        int section = item == null ? 0 : _tree.GetDropSectionAtPosition(at);
        if (section is < -1 or > 2)
            return null;
        var under =
            item == null ? _tree.GetRoot()
            : section is 0 or 2 ? item
            : item.GetParent();
        var parent = under == _tree.GetRoot() ? SnowTag.Empty : IdOf(under);
        // Nothing builds on a game, a game needs a blueprint, and a stage can't build on itself.
        if (
            R.Get(Stage.ById, parent) is { IsGame: true }
            || moved.IsGame && parent == SnowTag.Empty
            || Stage.Chain(R, parent).Any(s => s.Id == moved.Id)
        )
            return null;

        var others = under.GetChildren().Where(c => c != row).ToList();
        int index = section switch
        {
            -1 => others.IndexOf(item),
            0 => others.Count,
            1 => others.IndexOf(item) + 1,
            _ => 0,
        };
        if (row.GetParent() == under && row.GetIndex() == index)
            return null;
        return (moved, parent, others.Select(c => R.Get(Stage.ById, IdOf(c))).ToList(), index);
    }

    private void Drop(Vector2 at, Variant data)
    {
        if (Placement(at, data) is not (var moved, var parent, var siblings, var index))
            return;
        IAtomReader R = AtomService.Instance;
        var placed = moved with
        {
            ParentRef = parent,
            Rank = SnowRank.At(siblings, index, SnowportId.Create()),
        };
        IChange[] changes = moved.IsGame
            ? [placed, R.Get(ProjectGameSettings.Only) with { BlueprintRef = parent }]
            : [placed];
        AtomService.Instance.Write(changes);
    }

    #endregion

    private void Select(IEnumerable<SnowTag> blueprints) =>
        AtomService.Instance.SetSelection(Stage.ById, blueprints, this);

    /// <summary>
    /// Starts editing the name of <paramref name="stage"/>.
    /// </summary>
    private void Rename(SnowTag stage) =>
        Callable
            .From(() =>
            {
                // The stage may be gone or unselected since.
                if (_tree.GetSelected() is { } item && IdOf(item) == stage)
                    _tree.EditSelected(true);
            })
            .CallDeferred();

    private void OnItemEdited()
    {
        var item = _tree.GetEdited();
        var name = item.GetText(0);
        if (AtomService.Instance.Get(Stage.ById, IdOf(item)) is { } stage && stage.Name != name)
            AtomService.Instance.Write(stage with { Name = name });
    }
}
