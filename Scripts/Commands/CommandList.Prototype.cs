/// <summary>Commands for prototypes.</summary>
public static partial class CommandList
{
    public static readonly Command EditPrototype = new RecordCommand<Prototype>
    {
        Id = new("prototype.edit"),
        Caption = "Edit Prototype",
        Count = TargetCount.One,
        Action = ps => EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeId = ps[0].Id }),
    };
}
