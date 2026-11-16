using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Fru.FruMitigation.P3;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P3Full;

// The P3 hits that belong to neither drill, on the full run's clock: Hell's Judgment, the first
// Shockwave Pulsar and Black Halo. The Reprisal, Soil, Kerachole and Feint the sheet lists for the
// Pulsar are the ones pressed for Shell Crusher, which stay up for it.
internal static class FruP3FullMitigation
{
    public const float HellsJudgmentAt = 8.3f;
    public const float PulsarAt = 78.5f;
    public const float BlackHaloAt = 86.8f;

    public static MitPlanData Plan() => new(FruMitigation.Profile,
        [HellsJudgment, ShockwavePulsar, BlackHalo],
        [new(HellsJudgmentAt, [HellsJudgment.Key]), new(PulsarAt, [ShockwavePulsar.Key]), new(BlackHaloAt, [BlackHalo.Key])],
        Rows, FruMitigation.Oracle);

    private static readonly MitPlanEntry[] Rows =
    [
        new(SCH, Concitation, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        new(SGE, EukrasianPrognosisII, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        Dismantled(PulsarAt, "Shockwave Pulsar"),

        .. KitchenSinks(MT, BlackHaloAt, "Black Halo"),
        new(WHM, Aquaveil, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(WHM, DivineBenison, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(SCH, Protraction, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(SCH, Excogitation, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(AST, Exaltation, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(AST, CelestialIntersection, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(SGE, Taurochole, BlackHaloAt - Long, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        new(SGE, Haima, BlackHaloAt - Short, "Black Halo (on the main tank)", [BlackHaloAt], Buddy: true),
        .. BuddyMits(OT, BlackHaloAt, "Black Halo"),
    ];
}
