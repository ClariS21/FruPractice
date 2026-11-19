using System.Linq;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Umad.UmadMitigation.P5;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P5Flood;

// The P5 sheet's Chaotic Flood row on this scenario's clock; Holos goes out for the Ultima Repeater
// before the scenario starts and carries into it. Its 20 s buttons go out as late as the first stack
// allows: they carry on into the Maddening Orchestra after it.
internal static class UmadP5FloodMitigation
{
    public static readonly float[] FloodAt = [7.03f, 8.05f, 9.08f, 10.10f];

    public static MitPlanData Plan()
    {
        var first = FloodAt[0];
        float[] floods = [first, FloodAt[^1]];
        return new(UmadMitigation.Profile,
            [ChaoticFlood],
            [.. FloodAt.Select(at => new MitScheduledHit(at, [ChaoticFlood.Key]))],
            [
                new(SGE, Holos, -11f, "Ultima Repeater (before the scenario)", [first]),
                new(WHM, Temperance, first - Short, "Chaotic Flood (up for Maddening Orchestra too)", floods),
                new(AST, NeutralSect, first - Short, "Chaotic Flood", []),
                new(SCH, Expedient, first - Short, "Chaotic Flood (up for Maddening Orchestra too)", floods),
                new(SGE, Panhaima, first - Short, "Chaotic Flood", floods),
            ],
            UmadMitigation.Kefka);
    }
}
