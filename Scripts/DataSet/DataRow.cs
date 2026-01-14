using Godot;
using System;
using System.Collections.Generic;

public class DataRow
{
    public string Name { get; set; }
    public int Qty { get; set; }
    public List<string> Data { get; set; } = new();
}
