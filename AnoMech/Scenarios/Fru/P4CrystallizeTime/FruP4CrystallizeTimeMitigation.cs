using System;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Fru.FruMitigation.P4;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P4CrystallizeTime;

// The mitigation sheet's P4 rows from Crystallize Time to the second Akh Morn on this scenario's
// clock. The main tank takes the Usurper's Akh Morn alone. Plenary Indulgence and Collective
// Unconscious, 60s buttons the sheet lists for Crystallize Time and again for the Akh Morn 57s
// later, go out early for the first; Soil and Kerachole, back only after the Akh Morn, and the
// Phys Ranged party mitigation, which the sheet moves to Hallowed Wings in this case, skip the Akh
// Morn.
internal static class FruP4CrystallizeTimeMitigation
{
    public const float CrystallizeTimeAt = 11.02f;
    public const float EdgeOfOblivion3 = 17.03f;
    public const float DarkWaterAt = 23.92f;
    public const float DarkAeroAt = 25.92f;
    public const float UnholyDarknessAt = 28.82f;
    public const float QuietusAt = 42.92f;
    public const float SpiritTakerAt = 48.52f;
    public const float HallowedWings1 = 54.23f;
    public const float HallowedWings2 = 58.83f;
    public const float AkhMorn1 = 69.00f;
    public const float AkhMorn2 = 70.05f;
    public const float AkhMorn3 = 71.10f;
    public const float AkhMorn4 = 72.15f;

    private const float CastBarEnds = 11.12f;
    private const float AfterRewindsAreDropped = 45f;
    private const float ForMornAfah = 5.5f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    // The Morn Afah damage lands this long after its snapshot, which is when mitigation has to be up.
    private const float MornAfahDamageDelay = 1.8f;
    // How far ahead of Morn Afah's snapshot a re-shield goes out (a GCD heal's own cast included) and a short mit.
    private const float ShieldLead = 3f;
    private const float SoilLead = 2.5f;
    private const float ShortLead = 2f;
    // A 15 s mit pressed this long before the first Akh Morn hit lasts past Morn Afah's snapshot.
    private const float CoverBothLead = 2.5f;

    // `mornAfahAt` is when the Akh Morn Afah's Morn Afah damage lands on this plan's clock, for a run that has one (the full
    // phase): the sheet's Akh Morn Afah rows then cover it as well as the Akh Morns.
    public static MitPlanData Plan(float? mornAfahAt = null) => new(FruMitigation.Profile,
        [CrystallizeTime, EdgeOfOblivion, DarkWater, DarkAero, FruMitigation.P3.DarkEruption, FruMitigation.P3.UnholyDarkness, Quietus, SpiritTaker,
         HallowedWings, AkhMornUsurper, AkhMornOracle],
        [
            new(CrystallizeTimeAt, [CrystallizeTime.Key]), new(EdgeOfOblivion3, [EdgeOfOblivion.Key]), new(DarkWaterAt, [DarkWater.Key]),
            new(DarkAeroAt, [DarkAero.Key, FruMitigation.P3.DarkEruption.Key]), new(UnholyDarknessAt, [FruMitigation.P3.UnholyDarkness.Key]),
            new(QuietusAt, [Quietus.Key]), new(SpiritTakerAt, [SpiritTaker.Key]),
            new(HallowedWings1, [HallowedWings.Key]), new(HallowedWings2, [HallowedWings.Key]),
            new(AkhMorn1, [AkhMornUsurper.Key, AkhMornOracle.Key]), new(AkhMorn2, [AkhMornUsurper.Key, AkhMornOracle.Key]),
            new(AkhMorn3, [AkhMornUsurper.Key, AkhMornOracle.Key]), new(AkhMorn4, [AkhMornUsurper.Key, AkhMornOracle.Key]),
        ],
        BuildRows(mornAfahAt));

    private static readonly float[] Mech = [DarkWaterAt, DarkAeroAt, UnholyDarknessAt];
    private static readonly float[] Wings = [HallowedWings1, HallowedWings2];
    private static readonly float[] AkhMorns = [AkhMorn1, AkhMorn2, AkhMorn3, AkhMorn4];

    private static MitPlanEntry[] BuildRows(float? mornAfahAt) =>
    [
        new(MT, PartyMit, CrystallizeTimeAt - Short, "Crystallize Time", [CrystallizeTimeAt]),
        new(OT, MitActionId.Reprisal, CrystallizeTimeAt - Long, "Crystallize Time", [CrystallizeTimeAt]),
        new(SCH, SacredSoil, CrystallizeTimeAt - Long, "Crystallize Time", [CrystallizeTimeAt]),
        new(SCH, Concitation, CrystallizeTimeAt - Short, "Crystallize Time", [CrystallizeTimeAt]),
        new(SGE, Kerachole, CrystallizeTimeAt - Long, "Crystallize Time", [CrystallizeTimeAt]),
        new(SGE, EukrasianPrognosisII, CrystallizeTimeAt - Short, "Crystallize Time", [CrystallizeTimeAt]),
        new(WHM, PlenaryIndulgence, CrystallizeTimeAt - 4.1f, "Crystallize Time (early: back for the Akh Morn)", [CrystallizeTimeAt]),
        new(AST, CollectiveUnconscious, CrystallizeTimeAt - 4.1f, "Crystallize Time (early: back for the Akh Morn)", [CrystallizeTimeAt]),
        new(D2, Feint, CrystallizeTimeAt - Long, "Crystallize Time (Gaia)", [CrystallizeTimeAt], On: FruMitigation.Oracle),
        new(D4, Addle, CrystallizeTimeAt - Long, "Crystallize Time (Gaia)", [CrystallizeTimeAt], On: FruMitigation.Oracle),

        new(SCH, DeploymentTactics, DarkWaterAt - Short, "Crystallize Time's mechanic", Mech),
        new(SCH, Expedient, DarkWaterAt - Long, "Crystallize Time's mechanic", Mech),
        new(SCH, Seraphism, DarkWaterAt - Long, "Crystallize Time's mechanic", []),
        new(SGE, EukrasianPrognosisII, DarkWaterAt - Short, "Crystallize Time's mechanic", Mech),
        new(SGE, Holos, DarkWaterAt - Long, "Crystallize Time's mechanic", Mech),
        new(SGE, Philosophia, DarkWaterAt - Long, "Crystallize Time's mechanic", []),
        new(WHM, LiturgyOfTheBell, CastBarEnds, "Crystallize Time's mechanic (as the cast bar finishes)", []),
        new(AST, Macrocosmos, CastBarEnds, "Crystallize Time's mechanic (as the cast bar finishes)", []),

        new(OT, PartyMit, HallowedWings1 - Short, "Hallowed Wings", Wings),
        new(SCH, Concitation, HallowedWings1 - Short, "Hallowed Wings", Wings),
        new(SCH, SacredSoil, AfterRewindsAreDropped, "Hallowed Wings (after the rewinds are dropped)", Wings),
        new(SGE, EukrasianPrognosisII, HallowedWings1 - Short, "Hallowed Wings", Wings),
        new(SGE, Kerachole, AfterRewindsAreDropped, "Hallowed Wings (after the rewinds are dropped)", Wings),
        .. (mornAfahAt is null ? [new MitPlanEntry(D3, PartyMit, HallowedWings1 - Long, "Hallowed Wings", Wings)] : Array.Empty<MitPlanEntry>()),
        .. HallowedWing(MT, HallowedWings1, "Hallowed Wings"),
        .. HallowedWing(OT, HallowedWings1, "Hallowed Wings"),

        .. (mornAfahAt is { } m ? AkhMornAndMornAfah(m) : AkhMornAfahForTheAkhMorns()),

        new(MT, MitActionId.Rampart, AkhMorn1 - ForMornAfah, "Akh Morn (main tank alone)", AkhMorns),
        new(MT, Camouflage, AkhMorn1 - ForMornAfah, "Akh Morn (main tank alone)", AkhMorns, [Gunbreaker]),
        .. SoloAkhMorn(AkhMorn1, "Akh Morn (main tank alone)"),
    ];

    // The drill ends with the Akh Morns: each Akh Morn Afah row is pressed for them alone.
    private static MitPlanEntry[] AkhMornAfahForTheAkhMorns() =>
    [
        new(MT, MitActionId.Reprisal, AkhMorn1 - ForMornAfah, "Akh Morn Afah", AkhMorns),
        new(SCH, Concitation, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(SGE, EukrasianPrognosisII, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(WHM, Temperance, AkhMorn1 - Long, "Akh Morn Afah", AkhMorns),
        new(WHM, DivineCaress, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(WHM, PlenaryIndulgence, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(AST, NeutralSect, AkhMorn1 - Long, "Akh Morn Afah", []),
        new(AST, SunSign, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(D1, Feint, AkhMorn1 - ForMornAfah, "Akh Morn Afah (Shiva)", AkhMorns, On: FruMitigation.Usurper),
        Dismantled(AkhMorn1, "Akh Morn Afah"),
        .. Barrier(AkhMorn1, "Akh Morn Afah"),

    ];

    // The full phase's Akh Morn Afah. Mitigation is judged at Morn Afah's snapshot, 1.8 s before its damage lands:
    // the 15 s mits go out before the first Akh Morn so they cover the four hits and Morn Afah (the Feint on the
    // Oracle, whose damage Morn Afah is); the shields, which the Akh Morns eat, are pressed again after the last hit;
    // the 10 s buttons can't span both and cover Morn Afah, the bigger hit. The phys ranged party mit is pressed once, here:
    // the sheet lists it for both Akh Morn Afahs and the Hallowed Wings, 90 s apart at best.
    private static MitPlanEntry[] AkhMornAndMornAfah(float m)
    {
        var snapshot = m - MornAfahDamageDelay;
        float[] both = [AkhMorn1, AkhMorn2, AkhMorn3, AkhMorn4, m];
        float[] afah = [m];
        var cover = AkhMorn1 - CoverBothLead;
        return
        [
            new(MT, MitActionId.Reprisal, cover, "Akh Morn Afah", both),
            new(D1, Feint, cover, "Akh Morn Afah (Morn Afah's Oracle)", both, On: FruMitigation.Oracle),
            new(D3, PartyMit, cover, "Akh Morn Afah", both),
            new(WHM, Temperance, cover, "Akh Morn Afah", both),
            new(AST, NeutralSect, cover - 1f, "Akh Morn Afah", []),
            new(AST, SunSign, cover, "Akh Morn Afah", both),

            new(SCH, Concitation, snapshot - ShieldLead, "Morn Afah (shielded again after the Akh Morns)", afah),
            new(SCH, SacredSoil, snapshot - SoilLead, "Morn Afah (back after the Hallowed Wings)", afah),
            new(SGE, EukrasianPrognosisII, snapshot - ShieldLead, "Morn Afah (shielded again after the Akh Morns)", afah),
            new(SGE, Kerachole, snapshot - SoilLead, "Morn Afah (back after the Hallowed Wings)", afah),
            new(AST, HeliosConjunction, snapshot - ShieldLead, "Morn Afah (Neutral Sect's Helios)", afah),
            new(WHM, PlenaryIndulgence, snapshot - ShortLead, "Morn Afah", afah),
            new(WHM, DivineCaress, snapshot - ShortLead, "Morn Afah", afah),
            new(AST, CollectiveUnconscious, snapshot - ShortLead, "Morn Afah", afah),
            new(Extras, Dismantle, snapshot - ShortLead, "Morn Afah", afah, [Machinist]),
            new(Extras, MagickBarrier, snapshot - ShortLead, "Morn Afah", afah, [RedMage]),
            new(Extras, TemperaCoat, snapshot - ShortLead - 1f, "Morn Afah", afah, [Pictomancer]),
            new(Extras, TemperaGrassa, snapshot - ShortLead, "Morn Afah", afah, [Pictomancer]),
        ];
    }
}
