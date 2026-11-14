using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

/// <summary>
/// The permission tier a zone grants a given player over a component inside it.
/// Ordered from least to most restrictive so that "most restrictive wins" is simply the max.
/// </summary>
public enum ZoneTier
{
    SeeAndMove = 0,
    SeeOnly = 1,
    Hidden = 2,
}

/// <summary>
/// Resolves, for the local player, the see/move permission of each on-board component
/// based on the zones that geometrically contain it. Hiding is a render
/// mask, not network withholding.
/// </summary>
public static class ZoneService
{
    /// <summary>
    /// Resolve the effective tier for a component given the zones on the table and the local
    /// player's seat. Overlapping/nested zones combine most-restrictively.
    /// </summary>
    private static ZoneTier ResolveTier(
        VisualComponentBase component,
        IReadOnlyList<VcZone> zones,
        Ref<Seat> localSeat,
        bool seesAll
    )
    {
        if (seesAll || zones.Count == 0)
            return ZoneTier.SeeAndMove;

        var tier = ZoneTier.SeeAndMove;
        var pos = component.GlobalPosition;

        foreach (var zone in zones)
        {
            if (!zone.Contains(pos))
                continue;

            ZoneTier zoneTier = zone.SeatIncluded(localSeat)
                ? ZoneTier.SeeAndMove
                : (zone.HiddenWhenExcluded ? ZoneTier.Hidden : ZoneTier.SeeOnly);

            if (zoneTier > tier)
                tier = zoneTier;
        }

        return tier;
    }

    public static void Recompute(
        IEnumerable<VisualComponentBase> components,
        Ref<Seat> localSeat,
        bool isBlueprint
    )
    {
        var seesAll = isBlueprint || Atoms.R.Get(Seat.ById, localSeat)?.IsAdmin == true;
        var all = components as IList<VisualComponentBase> ?? components.ToList();
        var zones = all.OfType<VcZone>().ToList();

        foreach (var c in all)
        {
            if (c is VcZone)
            {
                // A zone is not affected by other zones
                c.ZoneHidden = false;
                c.LocallyMovable = true;
                continue;
            }

            var tier = ResolveTier(c, zones, localSeat, seesAll);
            c.ZoneHidden = tier == ZoneTier.Hidden;
            c.LocallyMovable = tier == ZoneTier.SeeAndMove;
        }
    }
}
