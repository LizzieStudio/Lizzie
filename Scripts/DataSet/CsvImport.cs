using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>Replaces a dataset's rows with a CSV file's.</summary>
public static class CsvImport
{
    /// <summary>
    /// Replaces the rows of <paramref name="dataset"/> with those in the CSV file at <paramref name="path"/>,
    /// in one event. The first line names the columns: if they differ from the dataset's, they replace them.
    /// </summary>
    public static void Replace(IRecordReader R, DataSet dataset, string path)
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

        var records = new List<Replicated>();

        var header = lines[0];
        if (!dataset.Columns.Select(c => c.Name).SequenceEqual(header))
        {
            // TODO: we should do column matching based on string
            dataset = dataset with
            {
                Columns = header
                    .Select(h => new Column { Id = Snowport.Clock.CreateTag(), Name = h })
                    .ToImmutableArray(),
            };
            records.Add(dataset);
        }

        foreach (var existing in R.GetRows(dataset.Id))
            records.Add(existing with { Deleted = true });

        string rank = R.LastRank(dataset.Id);
        foreach (var line in lines.Skip(1))
        {
            var data = ImmutableDictionary<SnowTag, string>.Empty;
            for (int c = 0; c < dataset.Columns.Length && c < line.Length; c++)
            {
                if (!string.IsNullOrEmpty(line[c]))
                    data = data.SetItem(dataset.Columns[c].Id, line[c]);
            }

            rank = RowRank.New(rank, null, Snowport.Clock.source);
            records.Add(
                new DataRow
                {
                    Id = Snowport.Clock.CreateTag(),
                    DataSetId = dataset.Id,
                    Rank = rank,
                    Data = data,
                }
            );
        }

        RecordService.Instance.Write(records);
    }
}
