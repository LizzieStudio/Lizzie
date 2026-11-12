using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveUpgrades;
using Xunit;

namespace Lizzie.Tests;

public class SaveUpgradeTests
{
    private static List<JsonObject> Lines(params string[] lines) =>
        lines.Select(l => JsonNode.Parse(l).AsObject()).ToList();

    private static string Folder([CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path), "Saves");

    [Fact]
    public void LeavesTheEventsOfACurrentSave()
    {
        var lines = Lines(SaveUpgrade.Current.Header, """{"id":1,"writes":[]}""");
        Assert.False(SaveUpgrade.Run(lines));
        Assert.True(
            JsonNode.DeepEquals(Assert.Single(lines), JsonNode.Parse("""{"id":1,"writes":[]}"""))
        );
    }

    [Fact]
    public void RefusesANewerRelease() =>
        Assert.Throws<JsonException>(() =>
            SaveUpgrade.Run(
                Lines(new SaveFormat(SaveUpgrade.Current.Release + 1).Header, """{"id":1}""")
            )
        );

    [Fact]
    public void RefusesANewerDraft()
    {
        var current = SaveUpgrade.Current;
        var next = current.Draft is int draft
            ? current with
            {
                Draft = draft + 1,
            }
            : new SaveFormat(current.Release + 1, 1);
        Assert.Throws<JsonException>(() => SaveUpgrade.Run(Lines(next.Header, """{"id":1}""")));
    }

    /// <summary>
    /// A load opens the newest save it can read, so v1.20 wins over v1.9 and a main save.
    /// </summary>
    [Fact]
    public void DraftsSortByNumber() =>
        Assert.Equal(
            new SaveFormat(1, 20),
            new[] { new SaveFormat(1, 9), new SaveFormat(1, 20), new SaveFormat(0) }.Max()
        );

    [Theory]
    [InlineData(0, null, "P.proj")]
    [InlineData(1, 20, "P.v1.20.proj")]
    [InlineData(2, null, "P.v2.proj")]
    public void ReadsTheFormatBackFromAFileName(int release, int? draft, string file)
    {
        var format = new SaveFormat(release, draft);
        Assert.Equal(file, format.FileName("P"));
        Assert.Equal(format, SaveFormat.FromFileName(file, "P"));
    }

    [Theory]
    [InlineData("Q.v1.proj")]
    [InlineData("P.old.proj")]
    [InlineData("P.v1.2.proj.123.tmp")]
    [InlineData("P.v99999999999.proj")]
    [InlineData("P.v1.99999999999.proj")]
    public void IgnoresFilesThatArentTheProjectsSaves(string file) =>
        Assert.Null(SaveFormat.FromFileName(file, "P"));

    /// <summary>
    /// A file naming a whole release that's still being drafted can only come from a newer Lizzie.
    /// </summary>
    [Fact]
    public void CantReadTheWholeOfAnUnreleasedRelease()
    {
        var current = SaveUpgrade.Current;
        var drafting = current.Draft != null ? current.Release : current.Release + 1;
        Assert.False(SaveUpgrade.CanRead(new SaveFormat(drafting)));
    }

    /// <summary>
    /// <para>
    /// Upgrades a save from the main branch, <c>Saves/Format0.proj</c>,
    /// and compares it to <c>Saves/Format0.upgraded.json</c>.
    /// </para>
    ///
    /// <para>
    /// It also checks that a checkpoint is made after each draft.
    /// A change to a step that changes what it writes fails here.
    /// If the change is intended, replace the upgraded file with the received one.
    /// </para>
    /// </summary>
    [Fact]
    public void UpgradesAMainSave()
    {
        var pinned = Path.Combine(Folder(), "Format0.upgraded.json");
        var received = Path.Combine(Folder(), "Format0.upgraded.received.json");

        var lines = Lines(File.ReadAllLines(Path.Combine(Folder(), "Format0.proj")));
        var checkpoints = new List<SaveFormat>();
        SaveUpgrade.Run(lines, checkpoints.Add);
        var current = SaveUpgrade.Current;
        Assert.Equal(
            Enumerable.Range(1, current.Draft ?? 0).Select(d => new SaveFormat(current.Release, d)),
            checkpoints
        );

        var upgraded =
            new JsonArray([.. lines]).ToJsonString(
                new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }
            ) + "\n";
        var expected = File.Exists(pinned)
            ? File.ReadAllText(pinned).ReplaceLineEndings("\n")
            : null;
        if (upgraded == expected)
        {
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, upgraded);
        Assert.Fail(
            expected == null
                ? $"There's no {Path.GetFileName(pinned)} yet. Check {Path.GetFileName(received)} and rename it."
                : $"Upgrading Format0.proj changed. Compare the results with:\n"
                    + $"    code --diff Tests/Saves/{Path.GetFileName(pinned)} Tests/Saves/{Path.GetFileName(received)}\n"
                    + $"If the change is intended, replace {Path.GetFileName(pinned)} with {Path.GetFileName(received)}."
        );
    }
}
