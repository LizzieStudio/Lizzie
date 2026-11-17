using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using SaveUpgrades;
using static GdUnit4.Assertions;

namespace Lizzie;

/// <summary>
/// The project manager lists project folders, and opens them by asking the UI.
/// It lists folders in a temporary folder, and puts the player's own list back after.
/// </summary>
[TestSuite]
public class ProjectManagerTests
{
    private const string Scene = "res://Scenes/Project/ProjectManager.tscn";

    // Lists folders "Ready" (opens), "Newer" (saved only by a newer Lizzie) and "Both", in that order,
    // runs test with the window, and puts the player's list back.
    private static async Task WithProjects(Func<ProjectManager, ISceneRunner, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "LizzieTests", Guid.NewGuid().ToString("N"));
        var newer = new SaveFormat(SaveUpgrade.Current.Release + 1).FileName(
            ProjectFolder.SaveName
        );
        var current = SaveUpgrade.Current.FileName(ProjectFolder.SaveName);
        string Make(string name, params string[] files)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            foreach (var file in files)
                File.WriteAllText(Path.Combine(folder, file), "");
            return ProjectFolder.Normalize(folder);
        }

        var listed = ProjectLibrary.Folders.ToArray();
        var location = Config.Registry.Get<string>("ProjectLocation") ?? "";
        try
        {
            var both = Make("Both", current, newer);
            var newerOnly = Make("Newer", newer);
            var ready = Make("Ready");
            foreach (var folder in new[] { both, newerOnly, ready })
                ProjectLibrary.PutFirst(folder);

            var window = GD.Load<PackedScene>(Scene).Instantiate<ProjectManager>();
            using var runner = ISceneRunner.Load(window, true);
            // A headless run's screen is tiny, which would put the window off it, out of the mouse's reach.
            window.GetTree().Root.Size = new Vector2I(1152, 648);
            window.Position = new Vector2I(40, 60);
            await runner.AwaitIdleFrame();
            await test(window, runner, ready);
        }
        finally
        {
            Config.Registry.Set("Projects", listed);
            Config.Registry.Set("ProjectLocation", location);
            Directory.Delete(root, true);
        }
    }

    // The open name dialog, with its name and location boxes.
    private static (ProjectNameDialog Dialog, LineEdit Name, LineEdit Location) NameDialog(
        ProjectManager window
    )
    {
        var dialog = window
            .GetChildren()
            .OfType<ProjectNameDialog>()
            .Single(d => !d.IsQueuedForDeletion());
        var boxes = dialog.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().ToList();
        return (dialog, boxes[0], boxes.ElementAtOrDefault(1));
    }

    private static void Type(LineEdit box, string text)
    {
        box.Text = text;
        box.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }

    private static ItemList List(ProjectManager window) => window.GetNode<ItemList>("%Projects");

    private static int Index(ProjectManager window, string name)
    {
        var list = List(window);
        return Enumerable.Range(0, list.ItemCount).Single(i => list.GetItemText(i) == name);
    }

    private static string SelectedName(ProjectManager window) =>
        List(window).GetSelectedItems() is [var index]
            ? List(window).GetItemText(index)
            : "nothing";

    // Selects the project the way clicking it does, since selecting it in code sends no signal.
    private static void Choose(ProjectManager window, string name)
    {
        int index = Index(window, name);
        List(window).Select(index);
        List(window).EmitSignal(ItemList.SignalName.ItemSelected, index);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task EachProjectIsListedWithWhatCanBeDoneToIt()
    {
        await WithProjects(
            async (window, runner, _) =>
            {
                // The player's own projects come after these.
                var list = List(window);
                AssertThat(string.Join(", ", Enumerable.Range(0, 3).Select(list.GetItemText)))
                    .IsEqual("Ready, Newer, Both");
                AssertThat(window.GetNode<Button>("%Open").Disabled).IsTrue();

                Choose(window, "Newer");
                await runner.AwaitIdleFrame();
                AssertThat(window.GetNode<Button>("%Open").Disabled)
                    .OverrideFailureMessage("A project only a newer Lizzie can read opened.")
                    .IsTrue();
                AssertThat(list.GetItemIcon(Index(window, "Newer"))).IsNotNull();

                Choose(window, "Both");
                await runner.AwaitIdleFrame();
                AssertThat(window.GetNode<Button>("%Open").Disabled)
                    .OverrideFailureMessage("A project with an older save didn't open.")
                    .IsFalse();
                AssertThat(list.GetItemIcon(Index(window, "Both"))).IsNotNull();
                AssertThat(list.GetItemIcon(Index(window, "Ready"))).IsNull();
            }
        );
    }

    // The list reads the folders again when the player comes back to Lizzie from another app,
    // like after deleting a project's folder there, and keeps what's selected.
    [TestCase]
    [RequireGodotRuntime]
    public async Task ComingBackReadsTheFoldersAgain()
    {
        await WithProjects(
            async (window, runner, ready) =>
            {
                Choose(window, "Ready");
                Directory.Delete(Path.Combine(Path.GetDirectoryName(ready), "Newer"), true);
                // What the scene tree gets when Lizzie gets the focus back.
                window.GetTree().Notification((int)MainLoop.NotificationApplicationFocusIn);
                await runner.AwaitIdleFrame();

                AssertThat(List(window).GetItemTooltip(Index(window, "Newer")))
                    .OverrideFailureMessage("The list didn't notice a folder was deleted.")
                    .Contains("can't be found");
                AssertThat(SelectedName(window)).IsEqual("Ready");
                AssertThat(window.GetNode<Button>("%Open").Disabled).IsFalse();
            }
        );
    }

    // New makes an empty project's folder where the player says, and selects it without opening it.
    // Rename renames its folder.
    [TestCase]
    [RequireGodotRuntime]
    public async Task NewMakesAProjectAndRenameRenamesIt()
    {
        await WithProjects(
            async (window, runner, ready) =>
            {
                var location = Path.GetDirectoryName(ready);
                var opened = ProjectService.Instance?.CurrentProject?.Folder;

                window.GetNode<Button>("%New").EmitSignal(BaseButton.SignalName.Pressed);
                await runner.AwaitIdleFrame();
                var (dialog, name, where) = NameDialog(window);
                Type(where, location);
                Type(name, "CON");
                AssertThat(dialog.GetOkButton().Disabled)
                    .OverrideFailureMessage("A name Windows reserves could be confirmed.")
                    .IsTrue();
                Type(name, "Ready");
                AssertThat(dialog.GetOkButton().Disabled)
                    .OverrideFailureMessage("A name that's taken could be confirmed.")
                    .IsTrue();
                Type(where, ready);
                Type(name, "Inner");
                AssertThat(dialog.GetOkButton().Disabled)
                    .OverrideFailureMessage("A project could be made inside another one.")
                    .IsTrue();
                Type(where, location);
                Type(name, "Fresh");
                AssertThat(dialog.GetOkButton().Disabled).IsFalse();
                dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
                await runner.AwaitIdleFrame();

                var fresh = Path.Combine(location, "Fresh");
                AssertThat(Directory.Exists(fresh)).IsTrue();
                AssertThat(SelectedName(window)).IsEqual("Fresh");
                AssertThat(ProjectService.Instance?.CurrentProject?.Folder ?? "")
                    .OverrideFailureMessage("Making a project opened it.")
                    .IsEqual(opened ?? "");

                window.GetNode<Button>("%Rename").EmitSignal(BaseButton.SignalName.Pressed);
                await runner.AwaitIdleFrame();
                (dialog, name, _) = NameDialog(window);
                AssertThat(name.Text).IsEqual("Fresh");
                Type(name, "Renamed");
                dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
                await runner.AwaitIdleFrame();

                AssertThat(Directory.Exists(fresh)).IsFalse();
                AssertThat(Directory.Exists(Path.Combine(location, "Renamed"))).IsTrue();
                AssertThat(SelectedName(window)).IsEqual("Renamed");
            }
        );
    }

    // Clicking a project and then Open asks to open it, the way a player does it.
    // The first click comes from another window, which brings the project manager back too.
    [TestCase]
    [RequireGodotRuntime]
    public async Task ClickingOpenAsksToOpenTheProject()
    {
        await WithProjects(
            async (window, runner, ready) =>
            {
                string requested = null;
                window.OpenRequested += folder => requested = folder;
                var list = List(window);

                // Window positions are in the root viewport, and its controls' in the window.
                async Task Click(Vector2 at, MouseButton button = MouseButton.Left)
                {
                    runner.SimulateMouseMove(window.Position + at);
                    await runner.AwaitIdleFrame();
                    runner.SimulateMouseButtonPressed(button);
                    await runner.AwaitIdleFrame();
                }
                var other = new Window
                {
                    Position = new Vector2I(800, 460),
                    Size = new Vector2I(200, 120),
                };
                window.AddChild(other);
                other.GrabFocus();
                // Items are as wide as the list, which gets its width once the window lays it out.
                await runner.AwaitIdleFrame();
                AssertThat(window.HasFocus()).IsFalse();

                var itemRect = list.GetItemRect(Index(window, "Ready"));
                await Click(list.GetGlobalRect().Position + itemRect.GetCenter());
                AssertThat(SelectedName(window))
                    .OverrideFailureMessage(
                        $"Clicking the item at {itemRect} in the list at {list.GetGlobalRect()}, "
                            + $"in the window at {window.Position}, didn't select it."
                    )
                    .IsEqual("Ready");

                // The wheel only scrolls, unlike a click on the list's empty space.
                var bottom = list.GetGlobalRect().Position + new Vector2(20, list.Size.Y - 6);
                await Click(bottom, MouseButton.WheelDown);
                AssertThat(SelectedName(window))
                    .OverrideFailureMessage("Scrolling the list took the selection away.")
                    .IsEqual("Ready");

                var open = window.GetNode<Button>("%Open");
                AssertThat(open.Disabled).IsFalse();
                bool pressed = false;
                open.Pressed += () => pressed = true;
                await Click(open.GetGlobalRect().GetCenter());
                AssertThat(pressed)
                    .OverrideFailureMessage(
                        $"Open at {open.GetGlobalRect()} wasn't pressed. "
                            + $"Hovered: {window.GuiGetHoveredControl()?.Name ?? "nothing"}."
                    )
                    .IsTrue();

                AssertThat(requested ?? "nothing")
                    .OverrideFailureMessage(
                        $"Clicking Open at {open.GetGlobalRect()} didn't ask to open the project."
                    )
                    .IsEqual(ready);
            }
        );
    }
}
