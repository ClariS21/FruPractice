using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Fru.FruMitigation.P3;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P3Apocalypse;

// The mitigation sheet's P3 rows from the first Dark Water to Memory's End on this scenario's
// clock. Memory's End is the enrage, so the 15s mitigations the sheet times for Shockwave Pulsar
// are pressed late enough to still be up for it, and the Expedient and Holos the sheet lists a
// second time, and the second Barrier, are still on cooldown from the slice before.
internal static class FruP3ApocalypseMitigation
{
    public const float DarkWater1 = 22.82f;
    public const float SpiritTakerAt = 24.82f;
    public const float EruptionAt = 36.02f;
    public const float DarkWater2 = 41.82f;
    public const float DarkestDanceAt = 44.02f;
    public const float KnockbackAt = 46.82f;
    public const float DarkWater3 = 50.82f;
    public const float PulsarAt = 56.23f;

    private const float BeforeMemorysEnd = 1f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    public static MitPlanData Plan() => new(FruMitigation.Profile,
        [DarkWater, SpiritTaker, DarkEruption, DarkestDance, DarkestDanceKnockback, ShockwavePulsar],
        [
            new(DarkWater1, [DarkWater.Key]), new(SpiritTakerAt, [SpiritTaker.Key]), new(EruptionAt, [DarkEruption.Key]),
            new(DarkWater2, [DarkWater.Key]), new(DarkestDanceAt, [DarkestDance.Key]), new(KnockbackAt, [DarkestDanceKnockback.Key]),
            new(DarkWater3, [DarkWater.Key]), new(PulsarAt, [ShockwavePulsar.Key]),
        ],
        Rows, FruMitigation.Oracle);

    private static readonly MitPlanEntry[] Rows =
    [
        new(SCH, Expedient, DarkWater1 - Long, "Dark Water 1", [DarkWater1, SpiritTakerAt]),
        new(SCH, SacredSoil, DarkWater1 - Long, "Dark Water 1", [DarkWater1, SpiritTakerAt]),
        new(SGE, Kerachole, DarkWater1 - Long, "Dark Water 1", [DarkWater1, SpiritTakerAt]),

        new(OT, MitActionId.Reprisal, EruptionAt - Long, "Dark Eruption", [EruptionAt, DarkWater2]),
        new(SCH, DeploymentTactics, EruptionAt - Short, "Dark Eruption", [EruptionAt, DarkWater2, DarkWater3]),
        new(SGE, EukrasianPrognosisII, EruptionAt - Short, "Dark Eruption", [EruptionAt, DarkWater2]),
        new(WHM, PlenaryIndulgence, EruptionAt - Short, "Dark Eruption", [EruptionAt, DarkWater2]),

        .. KitchenSinks(OT, DarkestDanceAt, "Darkest Dance"),
        .. BuddyMits(MT, DarkestDanceAt, "Darkest Dance"),

        new(MT, MitActionId.Reprisal, PulsarAt - BeforeMemorysEnd, "Shockwave Pulsar (up for Memory's End)", [PulsarAt]),
        new(MT, PartyMit, PulsarAt - BeforeMemorysEnd, "Shockwave Pulsar (up for Memory's End)", [PulsarAt]),
        new(SCH, Concitation, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        new(SCH, SacredSoil, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        new(SGE, EukrasianPrognosisII, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        new(SGE, Kerachole, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        new(AST, CollectiveUnconscious, PulsarAt - Short, "Shockwave Pulsar", [PulsarAt]),
        new(D1, Feint, PulsarAt - BeforeMemorysEnd, "Shockwave Pulsar (up for Memory's End)", [PulsarAt]),
        new(D3, PartyMit, PulsarAt - BeforeMemorysEnd, "Shockwave Pulsar (up for Memory's End)", [PulsarAt]),
        new(D4, Addle, PulsarAt - BeforeMemorysEnd, "Shockwave Pulsar (up for Memory's End)", [PulsarAt]),
    ];
}
