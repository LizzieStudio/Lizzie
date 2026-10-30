using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Makes grouping inputs to a focusable control easier.
/// </summary>
/// <remarks>
/// Add this as a child of a focusable control, then use <see cref="Update"/> to write to the atom.
/// While the parent control retains focus, the updates will go to the same transition.
/// If the user defocuses the control, all changes will be written as one Undo target.
/// If the user cancels the edit (Esc), all changes will be reversed without an Undo target.
/// A SpinBox needs <c>update_on_text_changed</c> for this to make any sense.
/// </remarks>
public partial class FocusEditGroup : Node
{
    private Control _focus;
    private Transition _transition;

    // the atoms the open transition wrote, which leaving the control keeps
    private readonly HashSet<IAtomRef> _edited = new();

    public override void _Ready()
    {
        var parent = GetParent<Control>();
        // A SpinBox keeps its focus on its inner LineEdit.
        _focus = parent is SpinBox spinBox ? spinBox.GetLineEdit() : parent;
        _focus.FocusExited += Close;
        _focus.GuiInput += OnGuiInput;
    }

    /// <inheritdoc cref="Atoms.Update"/>
    public void Update<T, I>(Identity<T, I> byId, I id, Func<T, T> change)
        where T : Atom
        where I : IAtomRef<T>, IComparable<I>
    {
        if (Atoms.R.Get(byId, id) is not { } atom)
            return;
        if (_transition == Transition.None)
            _transition = Atoms.R.Open(this, change(atom));
        else
            Atoms.R.Append(_transition, change(atom));
        _edited.Add(id);
    }

    private void OnGuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
        {
            _edited.Clear();
            _focus.ReleaseFocus();
        }
    }

    // Keeps what the transition wrote, as it is now, unless another player took it away.
    private void Close()
    {
        if (_transition == Transition.None)
            return;
        Atoms.R.Close(
            this,
            _transition,
            _edited.Select(Atoms.R.Get).Where(a => a?.Transition == _transition)
        );
        _transition = Transition.None;
        _edited.Clear();
    }
}
