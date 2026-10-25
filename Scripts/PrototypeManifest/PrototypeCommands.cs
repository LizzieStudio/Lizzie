using System.Linq;

namespace Lizzie;

public static class PrototypeCommands
{
    [Command("prototype.update_decks")]
    public static readonly Command UpdateDecks = new AtomCommand<Prototype>
    {
        Icon = CommandIcons.UpdateDecks,
        Caption = "Update Decks",
        Tooltip = "Adds new components and removes deleted components from blueprint decks.",
        ActsOn = Context.Selected | Context.Referenced,
        AppliesTo = (_, p) => p.Parameters is PrintedParameters,
        Requires =
        [
            (
                "The front and back have different numbers of items.",
                (R, p) => ((PrintedParameters)p.Parameters).SidesFit(R)
            ),
        ],
        Effects = (R, ps) =>
            ps.SelectMany(p => DeckReconciliation.Changes(R, p.Id, (PrintedParameters)p.Parameters))
                .ToList(),
    };

    [Command("prototype.edit")]
    public static readonly Command Edit = new AtomCommand<Prototype>
    {
        Icon = CommandIcons.Edit,
        Caption = "Edit Component",
        MaxTargets = 1,
        ActsOn = Context.Selected | Context.Referenced,
        Effects = (_, ps) =>
        {
            EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeRef = ps[0].Id });
            return null;
        },
    };
}
