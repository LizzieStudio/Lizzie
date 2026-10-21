using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>Replaces a dataset's rows with a CSV file's.</summary>
public static class CsvImport
{
    /// <summary>
    /// Replaces the rows of <paramref name="dataset"/> with those in the CSV file at <paramref name="path"/>,
    /// in one event. The first line names the columns: if they differ from the dataset's, they replace them.
    /// </summary>
    public static void Replace(IAtomReader R, DataSet dataset, string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"Could not open CSV file '{path}': {FileAccess.GetOpenError()}");
            return;
        }

        var lines = new List<string[]>();
        while (!file.EofReached())
        {
            var cells = file.GetCsvLine();
            if (cells.Length == 1 && string.IsNullOrEmpty(cells[0]))
                continue;
            lines.Add(cells);
        }

        if (lines.Count == 0)
        {
            GD.PrintErr($"CSV file '{path}' contained no data.");
            return;
        }

        var changes = new List<IChange>();

        var header = lines[0];
        var columns = R.GetColumns(dataset.Id);
        if (!columns.Select(c => c.Name).SequenceEqual(header))
        {
            // TODO: we should do column matching based on string
            changes.AddRange(columns.Select(R.Unset));
            SnowRank columnRank = default;
            columns = header
                .Select(h => new DataColumn
                {
                    Id = SnowTag.Create(),
                    DataSetId = dataset.Id,
                    Rank = columnRank = columnRank.After(SnowportId.Create()),
                    Name = h,
                })
                .ToList();
            changes.AddRange(columns);
        }

        foreach (var existing in R.GetRows(dataset.Id))
            changes.Add(R.Unset(existing));

        SnowRank rank = default;
        foreach (var line in lines.Skip(1))
        {
            rank = rank.After(SnowportId.Create());
            var row = new DataRow
            {
                Id = SnowTag.Create(),
                DataSetId = dataset.Id,
                Rank = rank,
            };
            changes.Add(row);
            for (int c = 0; c < columns.Count && c < line.Length; c++)
                if (!string.IsNullOrEmpty(line[c]))
                    changes.Add(
                        new DataCell { Id = new CellId(row.Id, columns[c].Id), Value = line[c] }
                    );
        }

        AtomService.Instance.Write(changes);
    }
}
