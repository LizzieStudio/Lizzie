using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public class IconLibrary : Dictionary<string, IconEntry>
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
        Addl("Book-Closed", "book.png");
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
        Addl("DNA", "dna.png");
        Addl("Book-Open", "open-book.png");
        Addl("Magic", "sparkler.png");
        Addl("Laser", "laser.png");
        Addl("Gem", "diamond.png");
        Addl("Barrel", "barrel.png");
        Addl("Oil Drum", "oil-barrel.png");
        Addl("Exclamation Mark", "exclamation.png");
        Addl("Question Mark", "question.png");
        Addl("Draw Card", "draw.png");
        Addl("Discard Card", "discard.png");
        Addl("Trash", "trash.png");
        Addl("Eye", "eye.png");
        Addl("Hide", "hidden.png");
        Addl("Cube", "cube.png");
        Addl("Cylinder", "cylinder.png");
        Addl("Meeple", "meeple.png");
        Addl("Hand", "hand.png");
        Addl("Fireball", "fireball.png");
        Addl("Palm Tree", "palm-tree.png");
        Addl("Pine Tree", "christmas-tree.png");
        Addl("Tree", "tree.png");
        Addl("House", "house.png");
        Addl("Hammer", "hammer.png");
        Addl("Temple", "bank.png");
        Addl("Building", "building.png");
        Addl("Bottle", "bottle.png");
        Addl("Grapes", "grapes.png");
        Addl("Apple", "apple.png");
        Addl("Electronics", "cpu.png");
        Addl("Computer", "computer.png");
        Addl("Diamond", "diamond-suit.png");
        Addl("Sound Wave", "sound.png");
        Addl("Water Waves", "water-waves.png");
        Addl("Atom", "atom.png");
        Addl("Lightning", "thunder.png");
        Addl("Club", "club.png");
        Addl("Spade", "spade.png");
        Addl("Castle", "castle.png");
        Addl("Tower", "tower.png");
        Addl("Flag", "flag.png");
        Addl("Pennant", "pennant.png");
        Addl("Checkered Flag", "checkered-flag.png");
        Addl("Bomb 1", "bomb.png");
        Addl("Bomb 2", "bomb-drop.png");
        Addl("Skull and Crossbones", "skull-and-crossbones.png");
        Addl("Crown", "crown.png");
        Addl("Paw", "paw.png");
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
        if (!ContainsKey(key))
        {
            return ResourceLoader.Load(BaseFolder + "notfound.png") as Texture2D;
        }

        return ResourceLoader.Load(BaseFolder + this[key].FileName) as Texture2D;
    }

    public void LoadOptionButton(OptionButton button)
    {
        int id = 0;

        foreach (var icon in GetCoreIconList())
        {
            button.AddItem(icon, id);
            id++;
        }

        //put in user-image block
        if (ProjectService.Instance.CurrentProject.Images.Count > 0)
        {
            button.AddSeparator();
            foreach (var img in ProjectService.Instance.CurrentProject.Images)
            {
                button.AddItem($"u:{img.Value.Name}", id);
                id++;
            }
        }

        button.AddSeparator();

        id++;
        foreach (var icon in GetExtendedIconList())
        {
            button.AddItem(icon, id);
            id++;
        }
    }

    public void LoadPopupMenu(PopupMenu menu)
    {
        int id = 0;

        foreach (var icon in GetCoreIconList())
        {
            menu.AddItem(icon, id);
            id++;
        }

        menu.AddSeparator();

        id++;
        foreach (var icon in GetExtendedIconList())
        {
            menu.AddItem(icon, id);
            id++;
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
