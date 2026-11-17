using System;
using System.IO;
using System.Linq;
using SaveUpgrades;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Each project is a folder of saves, one per format, which the project manager reads and moves around.
/// </summary>
public sealed class ProjectFolderTests : IDisposable
{
    // A fresh folder for each test, removed after.
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LizzieTests",
        Guid.NewGuid().ToString("N")
    );

    public ProjectFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Folder(string name, params string[] files)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(folder, file), "");
        return folder;
    }

    private static readonly SaveFormat Newer = new(SaveUpgrade.Current.Release + 1);

    private static string SaveOf(SaveFormat format) => format.FileName(ProjectFolder.SaveName);

    [Fact]
    public void SavesAreReadFromTheirFileNames()
    {
        var folder = Folder(
            "Game",
            "project.proj",
            "project.v1.proj",
            "project.v1.20.proj",
            "project.v1.proj.4242.tmp",
            "notes.txt"
        );

        // A draft comes before its release.
        Assert.Equal(
            [new SaveFormat(0), new SaveFormat(1, 20), new SaveFormat(1)],
            ProjectFolder.GetSaveFormats(folder).Order()
        );
    }

    [Fact]
    public void AProjectIsNamedByItsFolder()
    {
        Assert.Equal("My Game", ProjectFolder.NameOf(Path.Combine(_root, "My Game")));
    }

    [Fact]
    public void AnEmptyFolderOpensAsANewProject()
    {
        var status = ProjectStatus.Of(Folder("Game"));

        Assert.True(status.CanOpen);
        Assert.Null(status.Readable);
    }

    [Fact]
    public void AMissingFolderIsntFoundAndDoesntOpen()
    {
        var status = ProjectStatus.Of(Path.Combine(_root, "Gone"));

        Assert.False(status.Found);
        Assert.False(status.CanOpen);
    }

    [Fact]
    public void ASaveFromANewerLizzieAloneDoesntOpen()
    {
        var status = ProjectStatus.Of(Folder("Game", SaveOf(Newer)));

        Assert.False(status.CanOpen);
        Assert.False(status.OpensOlderSave);
    }

    [Fact]
    public void ASaveFromANewerLizzieBesideAnOlderOneOpensTheOlder()
    {
        var status = ProjectStatus.Of(Folder("Game", SaveOf(SaveUpgrade.Current), SaveOf(Newer)));

        Assert.True(status.CanOpen);
        Assert.True(status.OpensOlderSave);
        Assert.Equal(SaveUpgrade.Current, status.Readable);
    }

    [Fact]
    public void ANameThatsTakenIsAProblem()
    {
        Folder("Game");

        Assert.NotNull(ProjectFolder.ProblemWithLocation(_root, "Game"));
        Assert.Throws<IOException>(() => ProjectFolder.Create(_root, "Game"));
        Assert.Null(ProjectFolder.ProblemWithLocation(_root, "Other"));
    }

    [Fact]
    public void ALocationMustBeAFullPath()
    {
        Assert.NotNull(ProjectFolder.ProblemWithLocation("", "Game"));
        Assert.NotNull(ProjectFolder.ProblemWithLocation("relative", "Game"));
    }

    [Fact]
    public void CreatingMakesTheFolderAndItsLocation()
    {
        var location = Path.Combine(_root, "Lizzie Projects");

        var folder = ProjectFolder.Create(location, "Game");

        Assert.True(Directory.Exists(folder));
        Assert.Equal(Path.Combine(location, "Game"), folder);
    }

    [Fact]
    public void RenamingMovesTheFolderWithItsSaves()
    {
        var folder = Folder("Game", SaveOf(SaveUpgrade.Current));

        var renamed = ProjectFolder.Rename(folder, "Better Game");

        Assert.False(Directory.Exists(folder));
        Assert.Equal([SaveUpgrade.Current], ProjectFolder.GetSaveFormats(renamed));
    }

    [Fact]
    public void RenamingCanChangeOnlyTheCase()
    {
        var folder = Folder("game", SaveOf(SaveUpgrade.Current));

        var renamed = ProjectFolder.Rename(folder, "Game");

        Assert.Equal(["Game"], Directory.GetDirectories(_root).Select(Path.GetFileName));
        Assert.Equal("Game", ProjectFolder.NameOf(renamed));
    }

    [Fact]
    public void RenamingOntoATakenNameFails()
    {
        var folder = Folder("Game");
        Folder("Other");

        Assert.Throws<IOException>(() => ProjectFolder.Rename(folder, "Other"));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void AdoptingALooseSaveMovesItIntoAProjectFolder()
    {
        var user = Folder("user", "Test Project.proj");
        var location = Path.Combine(_root, "Lizzie Projects");

        var folder = ProjectFolder.Adopt(Path.Combine(user, "Test Project.proj"), location);

        Assert.Equal("Test Project", ProjectFolder.NameOf(folder));
        Assert.Equal([new SaveFormat(0)], ProjectFolder.GetSaveFormats(folder));
        Assert.Empty(Directory.GetFiles(user));
    }

    [Fact]
    public void AdoptingBesideATakenNameNumbersTheFolder()
    {
        var user = Folder("user", "Game.proj");
        var location = Path.Combine(_root, "Lizzie Projects");
        Directory.CreateDirectory(Path.Combine(location, "Game"));

        var folder = ProjectFolder.Adopt(Path.Combine(user, "Game.proj"), location);

        Assert.Equal("Game (2)", ProjectFolder.NameOf(folder));
    }

    // Projects can't be inside each other, since deleting the outer one would delete both.
    [Fact]
    public void AFolderInsideAnotherIsNested()
    {
        var game = Path.Combine(_root, "Game");

        Assert.True(ProjectFolder.AreNested(game, Path.Combine(game, "Inner")));
        Assert.True(ProjectFolder.AreNested(Path.Combine(game, "Inner", "Deeper"), game));
        Assert.True(ProjectFolder.AreNested(Path.Combine(_root, "GAME", "Inner"), game));
        Assert.False(ProjectFolder.AreNested(game, game));
        Assert.False(ProjectFolder.AreNested(game, Path.Combine(_root, "Game 2")));
    }

    // Only a folder with saves, or an empty one like a new project's, can be added,
    // since deleting a project trashes its whole folder.
    [Fact]
    public void AFolderIsAProjectWhenItHasSavesOrIsEmpty()
    {
        Assert.True(ProjectFolder.IsProject(Folder("Saved", SaveOf(SaveUpgrade.Current))));
        Assert.True(ProjectFolder.IsProject(Folder("New")));
        Assert.False(ProjectFolder.IsProject(Folder("Documents", "notes.txt")));
        Assert.False(ProjectFolder.IsProject(Path.Combine(_root, "Gone")));
    }
}
