public static class PrototypeCommands
{
    public static readonly Command Edit = new AtomCommand<Prototype>
    {
        Name = new("prototype.edit"),
        Icon = CommandIcons.Edit,
        Caption = "Edit Prototype",
        Count = TargetCount.One,
        ActsOn = Context.Selected | Context.Referenced,
        SideEffects = (ps, _) =>
            EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeId = ps[0].Id }),
    };
}
