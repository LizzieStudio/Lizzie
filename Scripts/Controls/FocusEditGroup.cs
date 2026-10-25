using System;
using Godot;

namespace Lizzie;

/// <summary>
/// Makes grouping inputs to a focusable control easier.
/// </summary>
/// <remarks>
/// Add this as a child of a focusable control, then use <see cref="Update"/> to write to the atom.
/// While the parent control retains focus, the updates will go to the same group.
/// If the user defocuses the control, all changes will be written as one Undo target.
/// If the user cancels the edit (Esc), all changes will be reversed without an Undo target.
/// A SpinBox needs <c>update_on_text_changed</c> for this to make any sense.
/// </remarks>
public partial class FocusEditGroup : Node
{
    private Control _focus;
    private SnowportId _group = SnowportId.Empty;

    public override void _Ready()
    {
        var parent = GetParent<Control>();
        // A SpinBox keeps its focus on its inner LineEdit.
        _focus = parent is SpinBox spinBox ? spinBox.GetLineEdit() : parent;
        _focus.FocusExited += Close;
        _focus.GuiInput += OnGuiInput;
    }

    /// <inheritdoc cref="AtomService.Update"/>
    public void Update<T, I>(Identity<T, I> byId, I id, Func<T, T> change)
        where T : Atom
    {
        if (AtomService.Instance.Get(byId, id) is not { } atom)
            return;
        if (_group == SnowportId.Empty)
            _group = AtomService.Instance.Open(this, change(atom));
        else
            AtomService.Instance.Append(_group, change(atom));
    }

    private void OnGuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
        {
            AtomService.Instance.CancelIfOpen(_group);
            _group = SnowportId.Empty;
            _focus.ReleaseFocus();
        }
    }

    private void Close()
    {
        AtomService.Instance.Close(_group);
        _group = SnowportId.Empty;
    }
}
