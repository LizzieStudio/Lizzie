using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public class IconLibrary : Dictionary<string, IconEntry >
{
    private const string BaseFolder = "res://Textures/Shapes/";

    public IconLibrary()
    {
        LoadDictionary();
    }
    
    private void LoadDictionary()
    {
        Clear();
        Addl("Circle", "circle.png", true);
        Addl("Rectangle", "square.png", true);
        Addl("Hex Point Up", "hex.png", true);
        Addl("Hex Flat Up", "hexflat.png", true);
        Addl("Rounded Rectangle", "RoundedRectangle.png", true);
        Addl("Triangle", "triangle.png", true);
        Addl("Star", "star.png");
        Addl("Pentagon", "pentagon.png");
        Addl("Airplane", "airplane.png");
        Addl("Rifle", "rifle.png");
        Addl("Bow", "archery.png");
        Addl("Book", "book.png");
        Addl("Boots", "boots.png");
        Addl("Bullets", "bullet.png");
        Addl("Checkmark", "check.png");
        Addl("Delete", "close.png");
        Addl("Battle", "battle.png");
        Addl("Die", "dice.png");
        Addl("Down Arrow", "down-arrow.png");
        Addl("Droplet", "drop.png");
        Addl("Explosion", "explosion.png");
        Addl("Double Arrow", "fast-forward.png");
        Addl("Fire", "fire-flame.png");
        Addl("Footsteps", "footstep.png");
        Addl("Gun", "gun.png");
        Addl("Heart", "heart.png");
        Addl("Skull", "skull.png");
        Addl("Moon", "moon.png");
        Addl("Parachute", "parachute.png");
        Addl("Potion", "potion.png");
        Addl("Radiation", "radiation.png");
        Addl("Refresh", "refresh-arrow.png");
        Addl("Rocket", "rocket.png");
        Addl("Revolver", "revolver.png");
        Addl("Right Arrow", "right-arrow.png");
        Addl("Snowflake", "snowflake.png");
        Addl("Shield", "shield.png");
        Addl("Soldier", "soldier.png");
        Addl("Sun", "sun.png");
        Addl("Sword", "sword.png");
        Addl("Tank", "tank.png");
        Addl("Fighter Jet", "vehicle.png");
        Addl("Wheat", "wheat.png");
        Addl("Wood", "wood.png");
        Addl("Axe", "axe.png");
        Addl("Pickaxe", "pickaxe.png");
        Addl("Ore", "ore.png");
        Addl("Gold Bars", "gold.png");
    }

    private void Addl(string key, string value, bool isCore = false)
    {
        Add(key, new IconEntry { FileName = value, IsCore = isCore });
    }

    public List<string> GetCoreIconList()
    {
        return this.Where(x => x.Value.IsCore).Select(x => x.Key).OrderBy(key => key).ToList();
    }
    
    public List<string> GetExtendedIconList()
    {
        return this.Where(x => !x.Value.IsCore).Select(x => x.Key).OrderBy(key => key).ToList();
    }

    public Texture2D TextureFromKey(string key)
    {
        if (!ContainsKey(key)) return new Texture2D();
        
        return ResourceLoader.Load(BaseFolder + this[key].FileName) as Texture2D;
    }

    public void LoadOptionButtonCore(OptionButton button)
    {
        foreach (var icon in GetCoreIconList())
        {
            button.AddItem(icon);
        }
    }

    public void LoadOptionButtonExtended(OptionButton button)
    {
        foreach (var icon in GetExtendedIconList())
        {
            button.AddItem(icon);
        }
    }
    
}

public struct IconEntry
{
    public string FileName;
    public bool IsCore;
}
