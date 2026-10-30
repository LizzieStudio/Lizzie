using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// The window for blueprints.
/// </summary>
public partial class BlueprintManager : Window, ICommandView
{
    public bool CanSelect(IAtomReader R, IAtomRef target) => Stage.Reachable(R, (Ref<Stage>)target);

    public IReadOnlyList<Command> Commands =>
        [StageCommands.AddBlueprint, StageCommands.AddTable, RenameStage];

    private Tree _tree;

    private readonly Dictionary<Ref<Stage>, TreeItem> _rows = new();

    // Marks the current table, and the stage in your view when it's something else.
    private static readonly Texture2D CurrentTableIcon = GD.Load<Texture2D>(
        CommandIcons.MakeCurrent
    );
    private static readonly Texture2D ShownIcon = GD.Load<Texture2D>(CommandIcons.Show);

    [Command("blueprint.rename")]
    private static readonly Command RenameStage = new AtomCommand<Stage>
    {
        Icon = CommandIcons.Rename,
        Caption = "Rename",
        Keys = [Shortcuts.Key(Key.F2)],
        MaxTargets = 1,
        Effects = (_, stages) =>
        {
            EventBus.Instance.Publish(new RenameStageEvent { StageRef = stages[0].Id });
            return null;
        },
    };

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
        EventBus.Instance.Subscribe<RenameStageEvent>(OnRenameStage);
    }

    public override void _ExitTree()
    {
        EventBus.Instance.Unsubscribe<RenameStageEvent>(OnRenameStage);
    }

    private void OnRenameStage(RenameStageEvent e) => Rename(e.StageRef);

    public override void _Ready()
    {
        _tree = GetNode<Tree>("%Tree");
        _tree.CreateItem();
        _tree.SetColumnExpand(IconColumn, false);
        _tree.SetColumnExpand(ChangesColumn, false);
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

        CloseRequested += QueueFree;
    }

    /// <summary>
    /// Updates the rows in place, so collapsed rows, the scroll and an open rename survive changes.
    /// Each blueprint keeps its row while it's reachable, wherever it moves in the tree.
    /// </summary>
    private void Sync(IWatchReader R)
    {
        var selected = R.GetSelection(Stage.ById, this).ToHashSet();
        var colors = R.SelectionColors(ViewPlace.Of(this));
        var shown = ProjectService.Instance.GameObjects.ShownStage(R);
        var current = R.Get(CurrentTable.Only).StageRef;
        var listed = new HashSet<Ref<Stage>>();
        int indent = _tree.GetThemeConstant("item_margin");

        AddChildren(_tree.GetRoot(), Ref<Stage>.Empty, 0);

        // Freeing a row frees the rows under it, so every dead row comes off the tree before any is freed.
        var dead = _rows.Where(r => !listed.Contains(r.Key)).ToList();
        foreach (var (id, item) in dead)
        {
            _rows.Remove(id);
            item.GetParent().RemoveChild(item);
        }
        foreach (var (_, item) in dead)
            item.Free();

        void AddChildren(TreeItem parent, Ref<Stage> parentRef, int depth)
        {
            foreach (
                var (stage, index) in R.Get(Stage.Children, parentRef).Select((s, i) => (s, i))
            )
            {
                listed.Add(stage.Id);
                // The rows before it are in place already, so the one at its index is the first one after it.
                if (!_rows.TryGetValue(stage.Id, out var item))
                {
                    item = _tree.CreateItem(parent, index);
                    item.SetMetadata(NameColumn, stage.Id);
                    item.SetSelectable(IconColumn, false);
                    item.SetSelectable(ChangesColumn, false);
                    item.SetTextAlignment(ChangesColumn, HorizontalAlignment.Right);
                    // Only untrimmed text widens its column.
                    item.SetTextOverrunBehavior(
                        ChangesColumn,
                        TextServer.OverrunBehavior.NoTrimming
                    );
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
                item.SetText(NameColumn, stage.Name);
                // Translucent, since the tree draws its hover highlight underneath.
                var fill = UI.StageColor(stage.IsTable, 0.42f) with
                {
                    A = 0.5f,
                };
                Color? outline = colors.TryGetValue(stage.Id, out var color) ? color : null;
                for (int column = NameColumn; column <= ChangesColumn; column++)
                    item.SetCustomStylebox(
                        column,
                        RowPart(
                            fill,
                            outline,
                            column,
                            column == NameColumn ? depth * indent + Gap : Gap
                        )
                    );
                var (icon, tooltip) =
                    stage.Id == current
                        ? (CurrentTableIcon, "The current table, which everyone plays")
                    : stage.Id == shown ? (ShownIcon, "In your view")
                    : (null, "");
                item.SetIcon(IconColumn, icon);
                item.SetTooltipText(IconColumn, tooltip);
                int changes = R.Count(ComponentPosition.WrittenIn, stage.Id);
                item.SetText(ChangesColumn, changes.ToString());
                item.SetTooltipText(
                    ChangesColumn,
                    $"{changes} {(changes == 1 ? "change" : "changes")} to parent"
                );
                // Selecting a row that's already selected would close an open rename.
                if (selected.Contains(stage.Id) != item.IsSelected(NameColumn))
                {
                    if (selected.Contains(stage.Id))
                        item.Select(NameColumn);
                    else
                        item.Deselect(NameColumn);
                }
                AddChildren(item, stage.Id, depth + 1);
            }
        }
    }

    // The tree's columns
    private const int NameColumn = 0;
    private const int IconColumn = 1;
    private const int ChangesColumn = 2;

    // The space between a row's box and its neighbors, in pixels.
    private const int Gap = 2;

    /// <summary>
    /// One cell's part of a row's rounded box, outlined in the color of whoever selected the row.
    /// The cells meet at the column edges, and only the box's two ends are rounded.
    /// The tree draws a cell's stylebox across the whole row height, and the name cell's from the tree's left edge,
    /// so the box pulls in by <paramref name="inset"/> at its ends and by half the gap above and below.
    /// </summary>
    private static StyleBoxFlat RowPart(Color fill, Color? outline, int column, int inset)
    {
        const int radius = 4;
        bool left = column == NameColumn;
        bool right = column == ChangesColumn;
        int border = outline == null ? 0 : 1;
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = outline ?? default,
            BorderWidthTop = border,
            BorderWidthBottom = border,
            BorderWidthLeft = left ? border : 0,
            BorderWidthRight = right ? border : 0,
            CornerRadiusTopLeft = left ? radius : 0,
            CornerRadiusBottomLeft = left ? radius : 0,
            CornerRadiusTopRight = right ? radius : 0,
            CornerRadiusBottomRight = right ? radius : 0,
            ExpandMarginLeft = left ? -inset : 0,
            ExpandMarginRight = right ? -inset : 0,
            ExpandMarginTop = -Gap / 2f,
            ExpandMarginBottom = -Gap / 2f,
        };
    }

    private static Ref<Stage> IdOf(TreeItem item) => (Ref<Stage>)item.GetMetadata(NameColumn);

    #region Moving blueprints

    private Variant StartDrag(Vector2 at)
    {
        if (_tree.GetItemAtPosition(at) is not { } item)
            return default;
        _tree.DropModeFlags = (int)(
            Tree.DropModeFlagsEnum.OnItem | Tree.DropModeFlagsEnum.Inbetween
        );
        _tree.SetDragPreview(new Label { Text = item.GetText(NameColumn) });
        return new BlueprintDrag { StageRef = IdOf(item) };
    }

    // We need this for Godot drag and drop.
    private sealed partial class BlueprintDrag : RefCounted
    {
        public Ref<Stage> StageRef { get; init; }
    }

    /// <summary>
    /// Where a drop at <paramref name="at"/> would move the dragged blueprint.
    /// </summary>
    /// <returns>
    /// A Tuple of:
    /// <list type="number">
    /// <item>The dragged blueprint</item>
    /// <item>The new parent, or <see cref="Ref{T}.Empty"/> for the root.</item>
    /// <item>The new siblings</item>
    /// <item>The position among the siblings</item>
    /// </list>
    /// Or null if it can't move there.
    /// </returns>
    private (Stage Moved, Ref<Stage> Parent, List<Stage> Siblings, int Index)? Placement(
        Vector2 at,
        Variant data
    )
    {
        if (data.As<GodotObject>() is not BlueprintDrag drag)
            return null;
        if (
            Atoms.R.Get(Stage.ById, drag.StageRef) is not { } moved
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
        var parent = under == _tree.GetRoot() ? Ref<Stage>.Empty : IdOf(under);
        // A stage can't build on itself.
        if (Stage.Chain(Atoms.R, parent).Any(s => s.Id == moved.Id))
            return null;
        if (Atoms.R.Get(Stage.ById, parent) is { IsTable: true })
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
        return (
            moved,
            parent,
            others.Select(c => Atoms.R.Get(Stage.ById, IdOf(c))).ToList(),
            index
        );
    }

    private void Drop(Vector2 at, Variant data)
    {
        if (Placement(at, data) is not (var moved, var parent, var siblings, var index))
            return;
        Atoms.R.Write(this, moved with { ParentRef = parent, Rank = SnowRank.At(siblings, index) });
    }

    #endregion

    private void Select(IEnumerable<Ref<Stage>> blueprints) =>
        Atoms.R.SetSelection(Stage.ById, blueprints, this);

    /// <summary>
    /// Starts editing the name of <paramref name="stage"/>.
    /// </summary>
    private void Rename(Ref<Stage> stage) =>
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
        var name = item.GetText(NameColumn);
        if (Atoms.R.Get(Stage.ById, IdOf(item)) is { } stage && stage.Name != name)
            Atoms.R.Write(this, stage with { Name = name });
    }
}
