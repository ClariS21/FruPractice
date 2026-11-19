using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P5Celestriad;

// The P5 sheet's Celestriad row on this scenario's clock. Celestriad itself deals no damage: its
// Seraph goes out during the cast bar and its party mitigation for the first towers. The Feint and
// Addle the sheet presses after the third towers are for the Ultima Repeater after the scenario.
internal static class UmadP5CelestriadMitigation
{
    public static readonly float[] TowersAt = [15.07f, 21.12f, 27.12f];

    private const float DuringTheCastBar = 3f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    // `firstInvuln` is the seat the sheet's MT column stands for.
    public static MitPlanData Plan(PartyRole firstInvuln) => new(UmadMitigation.Profile,
        [UmadMitigation.P5.CelestriadTower],
        [.. TowersAt.Select(at => new MitScheduledHit(at, [UmadMitigation.P5.CelestriadTower.Key]))],
        [
            new(SCH, SummonSeraph, DuringTheCastBar, "Celestriad (during the cast bar)", [TowersAt[0]]),
            new(Column(firstInvuln), PartyMit, TowersAt[0] - Long, "Celestriad", [TowersAt[0], TowersAt[1]]),
        ],
        UmadMitigation.Kefka);
}
