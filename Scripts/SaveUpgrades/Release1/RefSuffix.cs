using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// Every property that refers to an atom by its tag ends in Ref.
/// </summary>
public sealed class RefSuffix : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var obj in Edits.GetObjectsRecursive(writes))
        {
            switch ((string)obj["$type"])
            {
                case "ComponentPosition":
                    Edits.Rename(obj, "stage", "stageRef");
                    break;
                case "DieItem" or "RowFace":
                    Edits.Rename(obj, "row", "rowRef");
                    break;
                case "QuickFace":
                    Edits.Rename(obj, "suit", "suitRef");
                    break;
                case "DataCell":
                    Edits.Rename((JsonObject)obj["id"], "row", "rowRef");
                    Edits.Rename((JsonObject)obj["id"], "column", "columnRef");
                    break;
                case "DataColumn" or "DataRow":
                    Edits.Rename(obj, "datasetId", "datasetRef");
                    break;
                case "ImageSide":
                    Edits.Rename(obj, "image", "imageRef");
                    break;
                case "TemplateSide":
                    Edits.Rename(obj, "template", "templateRef");
                    Edits.Rename(obj, "dataset", "datasetRef");
                    Edits.Rename(obj, "quantity", "quantityRef");
                    break;
                case "TrayParameters":
                    Edits.Rename(obj, "prototype", "dispenseRef");
                    break;
                case "Template":
                    Edits.Rename(obj, "dataset", "datasetRef");
                    break;
            }
        }
    }
}
