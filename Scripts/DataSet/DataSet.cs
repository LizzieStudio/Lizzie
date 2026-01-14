using Godot;
using System;
using System.Collections.Generic;

public class DataSet
{
    //Columns except Name and Qty
    public List<string> Columns { get; set; } = new();
    
    public Dictionary<string, DataRow> Rows { get; set; } = new();

    /// <summary>
    /// Helper function that packages a single row as a series of Key-Value pairs
    /// </summary>
    /// <param name="name"></param>
    /// <returns>Dictionary in Key-Value format. Qty is a string</returns>
    public Dictionary<string, string> GetRowDictionary(string name)
    {
        var d = new Dictionary<string, string>();

        if (!Rows.ContainsKey(name)) return d;
        
        var r = Rows[name];
        
        d.Add("Name", name);
        d.Add("Qty", r.Qty.ToString());

        int i = 0;
        foreach (var c in Columns)
        {
            d.Add(c, r.Data[i]);
        }
        
        return d;
    }
}
