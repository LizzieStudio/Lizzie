public abstract class CommandBase
{
    public virtual VisualCommand Command { get; set; }

    public string Caption { get; protected set; } = string.Empty;
    public bool SingleOnly { get; protected set; } = false;
    public bool EnableToggle { get; protected set; } = false;
    public bool IsToggled { get; set; } = false;
    public bool AddQtySubmenu { get; set; }
}
