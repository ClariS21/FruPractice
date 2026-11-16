using System;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Fru.FruMitigation.P4;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P4DarklitDragonsong;

// The mitigation sheet's P4 rows from Darklit Dragonsong to the first Akh Morn on this scenario's
// clock. The other seven stack on the Oracle's Akh Morn while the main tank takes the Usurper's
// alone. Whichever tank the strat has take Somber Dance presses its own job's invulnerability for
// it, and no other tank presses anything. The sheet lists Reprisal,
// Temperance, Neutral Sect, Soil and Kerachole for both Darklit Dragonsong and The Path of Light,
// 12s apart: each goes out once, for both.
internal static class FruP4DarklitDragonsongMitigation
{
    public const float EdgeOfOblivion1 = 11.62f;
    public const float DarklitDragonsongAt = 21.13f;
    public const float BrightHungerAt = 32.22f;
    public const float PathOfLightAt = 33.07f;
    public const float SpiritTakerAt = 35.62f;
    public const float DarkWaterAt = 40.17f;
    public const float SomberFarAt = 43.82f;
    public const float SomberNearAt = 47.02f;
    public const float EdgeOfOblivion2 = 50.43f;
    public const float AkhMorn1 = 57.77f;
    public const float AkhMorn2 = 58.82f;
    public const float AkhMorn3 = 59.87f;
    public const float AkhMorn4 = 60.92f;

    private const float BeforeBothHits = 3f;
    private const float AfterSecondSomber = 48.2f;
    private const float AfterFirstSomber = 44.3f;
    private const float ForMornAfah = 5.5f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    // The Morn Afah damage lands this long after its snapshot, which is when mitigation has to be up.
    private const float MornAfahDamageDelay = 1.8f;
    private const float ShieldLead = 3f;
    private const float CoverBothLead = 2.5f;

    // `mornAfahAt` is when the first Akh Morn Afah's Morn Afah damage lands on this plan's clock, for a run that has one (the
    // full phase): the sheet's row then covers it as well as the Akh Morns.
    public static MitPlanData Plan(Func<PartyRole?>? somberTank = null, float? mornAfahAt = null) => new(FruMitigation.Profile,
        [EdgeOfOblivion, DarklitDragonsong, BrightHunger, PathOfLight, SpiritTaker, DarkWater, SomberDanceFar, SomberDanceNear, AkhMornUsurper, AkhMornOracle],
        [
            new(EdgeOfOblivion1, [EdgeOfOblivion.Key]), new(DarklitDragonsongAt, [DarklitDragonsong.Key]), new(BrightHungerAt, [BrightHunger.Key]),
            new(PathOfLightAt, [PathOfLight.Key]), new(SpiritTakerAt, [SpiritTaker.Key]), new(DarkWaterAt, [DarkWater.Key]),
            new(SomberFarAt, [SomberDanceFar.Key]), new(SomberNearAt, [SomberDanceNear.Key]), new(EdgeOfOblivion2, [EdgeOfOblivion.Key]),
            new(AkhMorn1, [AkhMornUsurper.Key, AkhMornOracle.Key]), new(AkhMorn2, [AkhMornUsurper.Key, AkhMornOracle.Key]),
            new(AkhMorn3, [AkhMornUsurper.Key, AkhMornOracle.Key]), new(AkhMorn4, [AkhMornUsurper.Key, AkhMornOracle.Key]),
        ],
        BuildRows(mornAfahAt), Baiter: somberTank);

    private static readonly float[] DarklitAndPath = [DarklitDragonsongAt, BrightHungerAt, PathOfLightAt];
    private static readonly float[] AkhMorns = [AkhMorn1, AkhMorn2, AkhMorn3, AkhMorn4];

    private static MitPlanEntry[] BuildRows(float? mornAfahAt)
    {
        var withMornAfah = mornAfahAt is not null;
        float[] akhMornsAndAfah = mornAfahAt is { } m ? [.. AkhMorns, m] : AkhMorns;
        var cover = withMornAfah ? AkhMorn1 - CoverBothLead : AkhMorn1 - ForMornAfah;
        return
        [
        new(OT, MitActionId.Reprisal, DarklitDragonsongAt - BeforeBothHits, "Darklit Dragonsong and The Path of Light", DarklitAndPath),
        new(OT, PartyMit, DarklitDragonsongAt - Short, "Darklit Dragonsong", [DarklitDragonsongAt]),
        new(SCH, Recitation, DarklitDragonsongAt - Short - 1.5f, "Darklit Dragonsong", []),
        new(SCH, Concitation, DarklitDragonsongAt - Short, "Darklit Dragonsong", DarklitAndPath),
        new(SCH, SacredSoil, DarklitDragonsongAt - BeforeBothHits, "Darklit Dragonsong and The Path of Light", DarklitAndPath),
        new(SGE, Zoe, DarklitDragonsongAt - Short - 1.5f, "Darklit Dragonsong", []),
        new(SGE, EukrasianPrognosisII, DarklitDragonsongAt - Short, "Darklit Dragonsong", DarklitAndPath),
        new(SGE, Kerachole, DarklitDragonsongAt - BeforeBothHits, "Darklit Dragonsong and The Path of Light", DarklitAndPath),
        new(WHM, Temperance, DarklitDragonsongAt - Long, "Darklit Dragonsong and The Path of Light", DarklitAndPath),
        new(AST, NeutralSect, DarklitDragonsongAt - Long, "Darklit Dragonsong and The Path of Light", []),
        Dismantled(DarklitDragonsongAt, "Darklit Dragonsong"),
        .. Barrier(DarklitDragonsongAt, "Darklit Dragonsong"),

        new(WHM, DivineCaress, PathOfLightAt - Short, "The Path of Light", [PathOfLightAt]),
        new(WHM, PlenaryIndulgence, PathOfLightAt - Short, "The Path of Light", [PathOfLightAt]),
        new(AST, SunSign, PathOfLightAt - Short, "The Path of Light", [PathOfLightAt]),

        .. Invulns(MT, SomberFarAt, "Somber Dance"),
        .. Invulns(OT, SomberFarAt, "Somber Dance"),

        new(MT, MitActionId.Reprisal, cover, "Akh Morn Afah", akhMornsAndAfah),
        new(SCH, SacredSoil, AfterSecondSomber, "Akh Morn Afah (after the second Somber Dance)", AkhMorns),
        new(SCH, Concitation, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(SCH, SummonSeraph, AkhMorn1 - Short, "Akh Morn Afah", akhMornsAndAfah),
        new(SGE, Kerachole, AfterSecondSomber, "Akh Morn Afah (after the second Somber Dance)", AkhMorns),
        new(SGE, EukrasianPrognosisII, AkhMorn1 - Short, "Akh Morn Afah", AkhMorns),
        new(SGE, Panhaima, AkhMorn1 - Short, "Akh Morn Afah", akhMornsAndAfah),
        // The full phase's Feint goes to the second Akh Morn Afah: 90 s apart is out of reach for the two, and the chain would drop the later.
        .. (withMornAfah ? Array.Empty<MitPlanEntry>() : new MitPlanEntry[] { new(D1, Feint, cover, "Akh Morn Afah (Shiva)", akhMornsAndAfah, On: FruMitigation.Usurper) }),
        .. (mornAfahAt is { } afah
            ? new MitPlanEntry[] { new(SCH, Concitation, afah - MornAfahDamageDelay - ShieldLead, "Morn Afah (shielded again after the Akh Morns)", [afah]),
               new(SGE, EukrasianPrognosisII, afah - MornAfahDamageDelay - ShieldLead, "Morn Afah (shielded again after the Akh Morns)", [afah]) }
            : new MitPlanEntry[] { new(D3, PartyMit, AkhMorn1 - ForMornAfah, "Akh Morn Afah", AkhMorns) }),

        new(MT, MitActionId.Rampart, AfterFirstSomber, "Akh Morn (main tank alone, after the first Somber Dance)", AkhMorns),
        new(MT, Camouflage, AfterFirstSomber, "Akh Morn (main tank alone, after the first Somber Dance)", AkhMorns, [Gunbreaker]),
        .. SoloAkhMorn(AkhMorn1, "Akh Morn (main tank alone)"),
        ];
    }
}
