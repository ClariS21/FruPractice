using System.Collections.Generic;
using System.Linq;
using AnoMech.Scenarios;
using PartyRole = AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Core.Game.Party;

// Seconds from the anchor cast's start, both ends inclusive; negative is before it.
internal readonly record struct BurstRange(float From, float To);

// Where real FRU parties burst, as offsets from a boss cast. Raid buffs (recast 110 s or more) fire only
// in a window with a Raid range; the one-minute personal buffs fire in every window. Occurrence picks
// which cast of the anchor action, 1-based in time order.
internal sealed record BurstWindow(string Name, uint AnchorId, int Occurrence, BurstRange? Raid, BurstRange Personal)
{
    public static readonly IReadOnlyList<BurstWindow> All =
    [
        new("P3-A", ActionId.HellsJudgment, 1, null, new(1.1f, 6.5f)),
        new("P3-B", ActionId.ShellCrusherCast, 1, new(1.7f, 5.8f), new(0.8f, 4.1f)),
        new("P3-C", ActionId.DarkestDance, 1, null, new(-2.7f, 1.3f)),
        new("P4-A", ActionId.DarklitDragonsong, 1, new(-3.8f, -0.4f), new(-11.5f, -1.2f)),
        new("P4-B", ActionId.CrystallizeTimeUsurper, 1, null, new(-7.0f, 2.8f)),
        new("P4-C", ActionId.CrystallizeTimeUsurper, 1, null, new(62.0f, 65.1f)),
        new("P5-A", ActionId.FulgentBlade, 1, new(-1.6f, 1.1f), new(-3.9f, 2.5f)),
        new("P5-B", ActionId.FulgentBlade, 1, null, new(56.7f, 64.0f)),
        new("P5-C", ActionId.FulgentBlade, 2, new(5.9f, 7.9f), new(4.1f, 10.8f)),
        new("P5-D", ActionId.FulgentBlade, 2, null, new(64.4f, 71.3f)),
        new("P5-E", ActionId.FulgentBlade, 2, new(126.2f, 128.3f), new(124.5f, 131.8f)),
    ];

    // The bot's moment in a range, as an offset from the anchor. Drawn from a fork named for the bot and
    // the window, so it does not depend on what else the stream was asked, in a drill or the full phase.
    public static float Pick(Rng combatStream, PartyRole role, string window, bool raid, BurstRange range)
        => range.From + combatStream.Fork($"burst-{role}-{window}-{(raid ? "raid" : "personal")}").NextSingle() * (range.To - range.From);

    // The window's anchor on the scenario's clock, if the scenario has that cast.
    public float? AnchorTime(IReadOnlyList<BurstAnchor> anchors)
    {
        var seen = 0;
        foreach (var anchor in anchors.OrderBy(a => a.Time))
            if (anchor.ActionId == AnchorId && ++seen == Occurrence) return anchor.Time;
        return null;
    }
}
