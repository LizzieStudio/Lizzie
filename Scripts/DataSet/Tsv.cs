using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Tab-separated text, as spreadsheets put cells on the clipboard:
/// tabs between cells, newlines between rows, and quotes around cells that contain either,
/// with "" for a literal quote.
/// </summary>
public static class Tsv
{
    /// <summary>The rows of cells in <paramref name="text"/>. A trailing newline doesn't make an empty row.</summary>
    public static List<string[]> Parse(string text)
    {
        text = text.TrimEnd('\r', '\n').Replace("\r\n", "\n").Replace('\r', '\n');

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch != '"')
                    cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    cell.Append(text[++i]);
                else
                    quoted = false;
            }
            else if (ch == '"' && cell.Length == 0)
                quoted = true;
            else if (ch == '\t')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(cell.ToString());
                rows.Add(row.ToArray());
                row.Clear();
                cell.Clear();
            }
            else
                cell.Append(ch);
        }

        row.Add(cell.ToString());
        rows.Add(row.ToArray());
        return rows;
    }

    /// <summary>The rows of cells as text that <see cref="Parse"/> and spreadsheets read back.</summary>
    public static string Format(IEnumerable<IEnumerable<string>> rows) =>
        string.Join("\n", rows.Select(row => string.Join("\t", row.Select(Quoted))));

    private static string Quoted(string cell) =>
        cell.IndexOfAny(['\t', '\n', '\r', '"']) < 0 ? cell : $"\"{cell.Replace("\"", "\"\"")}\"";
}
