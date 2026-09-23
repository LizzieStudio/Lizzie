using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;
using TTSS.Scripts.Templating;

public class Project
{
    public string Filename { get; set; }
    public Dictionary<SnowTag, Template> Templates { get; set; } = new();
    public Dictionary<SnowTag, DataRow> DataRows { get; set; } = new();
    public Dictionary<SnowTag, Prototype> Prototypes { get; set; } = new();

    /// <summary>
    /// Named scene snapshots the user can save and restore.
    /// Key is the state's immutable id.
    /// </summary>
    public Dictionary<SnowTag, GameState> GameStates { get; set; } = new();

    /// <summary>
    /// The snapshot currently loaded or <see cref="SnowTag.Empty"/>.
    /// </summary>
    public SnowTag ActiveGameState { get; set; }

    /// <summary>
    /// Project-level settings edited via the Project Settings dialog.
    /// </summary>
    public ProjectGameSettings GameSettings { get; set; } = new();

    /// <summary>
    /// The non-deleted rows of a dataset in order.
    /// </summary>
    public List<DataRow> GetRows(SnowTag datasetRef)
    {
        var rows = new List<DataRow>();
        if (datasetRef == SnowTag.Empty)
            return rows;

        foreach (var r in DataRows.Values)
        {
            if (!r.Deleted && r.DataSetId == datasetRef)
                rows.Add(r);
        }

        rows.Sort(
            (a, b) =>
            {
                int c = RowRank.Comparer.Compare(a.Rank, b.Rank);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            }
        );
        return rows;
    }

    public GameState GetGameState(SnowTag stateRef)
    {
        if (stateRef == SnowTag.Empty)
            return null;
        if (GameStates.TryGetValue(stateRef, out var state) && !state.Deleted)
            return state;
        return null;
    }
}
