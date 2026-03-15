using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public class DataSet
{
    public string Name { get; set; }

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

        if (!Rows.ContainsKey(name))
            return d;

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

    /// <summary>
    /// Creates a deep clone of this DataSet
    /// </summary>
    /// <returns>A new DataSet with all columns and rows cloned</returns>
    public DataSet Clone()
    {
        var clonedDataSet = new DataSet();

        // Clone the columns list
        clonedDataSet.Columns = new List<string>(this.Columns);

        // Clone each row in the dictionary
        clonedDataSet.Rows = new Dictionary<string, DataRow>();
        foreach (var kvp in this.Rows)
        {
            clonedDataSet.Rows[kvp.Key] = kvp.Value.Clone();
        }

        return clonedDataSet;
    }

    public static DataSet TestDataSet()
    {
        var ds = new DataSet();
        var c = new List<string> { "Title", "Cost", "Image", "Effect" };

        var r1 = new DataRow
        {
            Name = "Test1",
            Qty = 1,
            Data = new List<string> { "Title1", "1", "Heart", "+1 Health" },
        };
        var r2 = new DataRow
        {
            Name = "Test2",
            Qty = 2,
            Data = new List<string> { "Title2", "2", "Star", "+2 Magic" },
        };
        var r3 = new DataRow
        {
            Name = "Test3",
            Qty = 3,
            Data = new List<string> { "Title3", "3", "Axe", "+3 Attack" },
        };
        var r4 = new DataRow
        {
            Name = "Test4",
            Qty = 4,
            Data = new List<string> { "Title4", "4", "Sword", "+4 Attack" },
        };
        var r5 = new DataRow
        {
            Name = "Test5",
            Qty = 5,
            Data = new List<string> { "Title5", "5", "Bow", "+5 Range" },
        };

        ds.Columns = c;
        ds.Rows.Add("Test1", r1);
        ds.Rows.Add("Test2", r2);
        ds.Rows.Add("Test3", r3);
        ds.Rows.Add("Test4", r4);
        ds.Rows.Add("Test5", r5);

        return ds;
    }
}
