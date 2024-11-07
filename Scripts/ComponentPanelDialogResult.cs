using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public abstract partial class ComponentPanelDialogResult : Control
{
    public abstract List<string> Validity();
    public abstract Dictionary<string, object> GetParams();

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
            Title = title
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
        _fd = null;
    }

    private void FileCanceled()
    {
        FileSelected(string.Empty);
    }


  
}
