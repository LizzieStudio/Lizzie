using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// DataSet is spelled Dataset.
/// </summary>
public sealed class DatasetSpelling : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var write in Edits.GetObjects(writes))
        {
            switch ((string)write["$type"])
            {
                case "DataSet":
                    write["$type"] = "Dataset";
                    break;
                case "DataColumn" or "DataRow":
                    Edits.Rename(write, "dataSetId", "datasetId");
                    break;
                case "Template":
                    Edits.Rename(write, "dataSet", "dataset");
                    break;
            }
        }
    }
}
