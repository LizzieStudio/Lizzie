using Godot;
using System;
using System.Collections.Generic;

public partial class Config : Node
{
    const string DEFAULT_SECTION = "AppState";
    public static Config Registry = new();
    public const string CONFIG_FILE_PATH = "user://lizzie-config.cfg";
    ConfigFile config = new();

    Config()
    {
        config.Load(CONFIG_FILE_PATH);
    }

    // Default section is AppState for ease of use
    public T Get<[MustBeVariant] T>(string name)
    {
        return Get<T>(DEFAULT_SECTION, name);
    }

    public T Get<[MustBeVariant] T>(string section, string name)
    {
        if (!config.HasSection(section) || !config.HasSectionKey(section, name))
            return default;

        Variant value = config.GetValue(section, name);

        try {
            return value.As<T>();
        } catch {
            throw new Exception("Invalid value in '" + section + "->" + name + "'.");
        }
    }

    public void Set<[MustBeVariant] T>(string name, T value)
    {
        Set(DEFAULT_SECTION, name, value);
    }

    public void Set<[MustBeVariant] T>(string section, string name, T value)
    {
        config.SetValue(section, name, Variant.From(value));
        config.Save(CONFIG_FILE_PATH);
    }
}
