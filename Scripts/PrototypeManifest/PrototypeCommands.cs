/// <summary>Commands for prototypes.</summary>
public static class PrototypeCommands
{
    public static readonly Command Edit = new RecordCommand<Prototype>
    {
        Id = new("prototype.edit"),
        Caption = "Edit Prototype",
        Count = TargetCount.One,
        SideEffects = (ps, _) =>
            EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeId = ps[0].Id }),
    };
}
