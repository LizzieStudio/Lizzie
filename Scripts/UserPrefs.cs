using System;
using Godot;

public class UserPrefs
{
    // Access settings
    private string _lastContentPath = "";
    public string LastContentPath
    {
        get {
            return _lastContentPath;
        }
        set
        {
            if (value != _lastContentPath)
            {
                _lastContentPath = value;
                config.SetValue( Section_Files, Key_LastContentPath, _lastContentPath);
                config.Save(SettingsPath);
            }
        }
    }

    // Save the settings to a config file
    private const string SettingsPath = "user://settings.ini";
    private const string Key_LastContentPath = "LastContentPath";
    private const string Section_Files = "Files";
    private ConfigFile config = new ConfigFile();


    // Singleton, created on demand
    private static readonly Lazy<UserPrefs> instance = new Lazy<UserPrefs>(() => new UserPrefs());
    public static UserPrefs Instance => instance.Value;

     // Private constructor to prevent instantiation from outside the class.
    private UserPrefs()
    {
        config = new ConfigFile();
        Error err = config.Load(SettingsPath);
        if (err != Error.Ok)
        {
            // Set defaults
            _lastContentPath = OS.GetSystemDir(OS.SystemDir.Documents);
            config.SetValue( Section_Files, Key_LastContentPath, _lastContentPath);
            config.Save(SettingsPath);
        } else {
            // Initialize with values from config
            _lastContentPath = (string)config.GetValue( Section_Files, Key_LastContentPath );
        }
    }
}