using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public class DataRow
{
    public string Name { get; set; }
    public int Qty { get; set; }
    public List<string> Data { get; set; } = new();

    /// <summary>
    /// Creates a deep clone of this DataRow
    /// </summary>
    /// <returns>A new DataRow with copied values</returns>
    public DataRow Clone()
    {
        return new DataRow
        {
            Name = this.Name,
            Qty = this.Qty,
            Data = new List<string>(this.Data),
        };
    }
}
