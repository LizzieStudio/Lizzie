using System;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class WritePlaceTests
{
    // Undo finds an event by the place it was written in, so a write from a dialog that isn't a view would be out of its reach.
    [TestCase]
    [RequireGodotRuntime]
    public void AWriteFromOutsideAViewThrowsAndWritesNothing()
    {
        var dialog = new AcceptDialog();
        var inside = new Node();
        dialog.AddChild(inside);
        using var runner = ISceneRunner.Load(dialog, true);

        var row = new DataRow { Id = Ref<DataRow>.Create() };
        AssertThrown(() => Atoms.R.Write(inside, row)).IsInstanceOf<InvalidOperationException>();
        AssertThat(Atoms.R.Get(DataRow.ById, row.Id))
            .OverrideFailureMessage("The write from outside a view went through anyway.")
            .IsNull();
    }
}
