/// <summary>Commands for prototypes.</summary>
public static class PrototypeCommands
{
    public static readonly Command Edit = new RecordCommand<Prototype>
    {
        Id = new("prototype.edit"),
        // The prototype manifest's edit icon.
        Icon = UI.TextureUI_Pencil,
        Caption = "Edit Prototype",
        Count = TargetCount.One,
        ActsOn = Context.Selected | Context.Referenced,
        SideEffects = (ps, _) =>
            EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeId = ps[0].Id }),
    };
}
