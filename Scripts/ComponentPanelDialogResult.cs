using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

public abstract partial class ComponentPanelDialogResult : Control
{
    public abstract Dictionary<string, object> GetParams();

    public virtual void Activate() { }

    public virtual void Deactivate() { }

    public int PrototypeIndex { get; set; } = 0;
    public virtual VisualComponentBase.VisualComponentType ComponentType { get; set; }

    protected float ParamToFloat(string input)
    {
        var s = input.Trim();
        if (float.TryParse(s, out var f))
        {
            return f;
        }

        return 0;
    }

    private FileDialog _fd;
    private Action<string> _callback;

    protected string ShowFileDialog(string title, Action<string> callback)
    {
        _fd = new FileDialog()
        {
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Title = title,
            CurrentDir = UserPrefs.Instance.LastContentPath,
        };

        _callback = callback;

        _fd.FileSelected += FileSelected;
        _fd.Canceled += FileCanceled;

        GetParent().AddChild(_fd);
        _fd.Visible = true;

        return string.Empty;
    }

    private void FileSelected(string file)
    {
        _callback(file);
        _fd.FileSelected -= FileSelected;
        _fd.Canceled -= FileCanceled;
        UserPrefs.Instance.LastContentPath = _fd.CurrentDir;
        _fd = null;
    }

    private void FileCanceled()
    {
        FileSelected(string.Empty);
    }

    public TextureFactory TextureFactory { get; set; }

    public virtual Project CurrentProject { get; set; }

    public DataSet DataSet { get; set; }
    public bool MultipleCreateMode { get; set; } = false;

    //These parameters are used to create a grid of objects in multiple mode.
    public float WidthHint { get; set; }
    public float HeightHint { get; set; }

    public virtual void DisplayPrototype(Guid prototypeId) { }

    public virtual void DisplayPrototype(Prototype prototype) { }

    public abstract List<string> ValidateParameters(Dictionary<string, object> parameters);
}
