using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Umad.UmadMitigation.P3;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P3Full;

// The P3 sheet's rows from Bowels of Agony to the second Stray Flames/Tsunami, on the full run's
// clock. Its MT column is the Chaos tank's and its OT column the Exdeath tank's. Sacred Soil and
// Kerachole go out early for Bowels of Agony so they are back for the first elements; the first
// buster's Expedient and Holos go out in the short gap that also covers the second elements. The
// buster's own tank rows are the full run's (UmadP3ThunderMitigation).
internal static class UmadP3OpeningMitigation
{
    public const float BowelsAt = 22.63f;
    public const float ShortStrayAt = 41.68f;
    public const float ShortCrystalAt = 42.75f;
    public static readonly float[] ThunderAt = [51.00f, 54.03f];
    public const float LongStrayAt = 68.68f;
    public const float LongCrystalAt = 69.72f;

    // The sheet's "after Kefka says 'Oh! What other toys...'": back again 30 s later, for the elements.
    private const float EarlyThirties = 10.5f;
    private const float ThirtiesBack = 41f;
    // Prepared right after Bowels of Agony.
    private const float AfterBowels = 24.5f;
    // Late enough to last through the second elements, early enough for the buster.
    private const float ThroughTheElements = 50.3f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    public static MitPlanData Plan(UmadP3Tanks tanks)
    {
        var chaos = Column(tanks.Chaos);
        var exdeath = Column(tanks.Exdeath);
        float[] bowels = [BowelsAt];
        float[] shortElements = [ShortStrayAt, ShortCrystalAt];
        float[] thunder = ThunderAt;
        float[] longElements = [LongStrayAt, LongCrystalAt];
        return new(UmadMitigation.Profile,
            [BowelsOfAgony, Stray, Crystal, ThunderIII],
            [
                new(BowelsAt, [BowelsOfAgony.Key]),
                new(ShortStrayAt, [Stray.Key]), new(ShortCrystalAt, [Crystal.Key]),
                new(ThunderAt[0], [ThunderIII.Key]), new(ThunderAt[1], [ThunderIII.Key]),
                new(LongStrayAt, [Stray.Key]), new(LongCrystalAt, [Crystal.Key]),
            ],
            [
                new(chaos, MitActionId.Reprisal, BowelsAt - Long, "Bowels of Agony", bowels),
                new(SCH, SacredSoil, EarlyThirties, "Bowels of Agony (early, so it is back for the first elements)", bowels),
                new(SGE, Kerachole, EarlyThirties, "Bowels of Agony (early, so it is back for the first elements)", bowels),
                new(WHM, PlenaryIndulgence, BowelsAt - Short, "Bowels of Agony", bowels),
                new(AST, CollectiveUnconscious, BowelsAt - Short, "Bowels of Agony", bowels),
                new(D1, Feint, BowelsAt - Long, "Bowels of Agony", bowels),

                new(SGE, Zoe, AfterBowels - 0.3f, "Stray Flames/Tsunami 1 (Zoe shields, prepared after Bowels of Agony)", []),
                new(SCH, DeploymentTactics, AfterBowels, "Stray Flames/Tsunami 1 (Spreadlo, prepared after Bowels of Agony)", shortElements),
                new(SGE, EukrasianPrognosisII, AfterBowels, "Stray Flames/Tsunami 1 (Zoe shields, prepared after Bowels of Agony)", shortElements),
                new(chaos, PartyMit, ShortStrayAt - Long, "Stray Flames/Tsunami 1", shortElements),
                .. Barrier(ShortStrayAt - Short, "Stray Flames/Tsunami 1", shortElements),
                new(SCH, SacredSoil, ThirtiesBack, "Stray Flames/Tsunami 1 (Soil is back)", shortElements),
                new(SGE, Kerachole, ThirtiesBack, "Stray Flames/Tsunami 1 (Kerachole is back)", shortElements),

                new(exdeath, MitActionId.Reprisal, ThunderAt[0] - Long, "Thunder III 1", thunder, On: UmadMitigation.Exdeath),
                new(D4, Addle, ThunderAt[0] - Long, "Thunder III 1", thunder, On: UmadMitigation.Exdeath),
                Dismantled(ThunderAt[0] - Short, "Thunder III 1", thunder, UmadMitigation.Exdeath),
                new(SCH, Expedient, ThroughTheElements, "Thunder III 1 (up for the next Stray Flames/Tsunami too)", [.. thunder, .. longElements]),
                new(SGE, Holos, ThroughTheElements, "Thunder III 1 (up for the next Stray Flames/Tsunami too)", [.. thunder, .. longElements]),

                new(exdeath, PartyMit, LongStrayAt - Long, "Stray Flames/Tsunami 2", longElements),
                new(WHM, Temperance, LongStrayAt - Long, "Stray Flames/Tsunami 2 (up for Ultima Blaster too)", longElements),
                new(AST, NeutralSect, LongStrayAt - Long, "Stray Flames/Tsunami 2", []),
                new(SCH, SummonSeraph, LongStrayAt - Long, "Stray Flames/Tsunami 2", longElements),
            ],
            UmadMitigation.Chaos);
    }
}
