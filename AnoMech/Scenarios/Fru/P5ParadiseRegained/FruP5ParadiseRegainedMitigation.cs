using System.Collections.Generic;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using AnoMech.Core.UserActions;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Fru.FruMitigation.P5;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P5ParadiseRegained;

// The mitigation sheet's P5 rows for a Paradise Regained: the wings' tank cooldowns and the
// Polarizing Strikes, on this scenario's clock. The sheet gives the wings' cooldowns once, so only
// the first round presses them; the second round's wings hit the tanks bare. The second round's
// Temperance, Divine Caress, Neutral Sect and Sun Sign aren't here: the first round's are still on cooldown.
internal static class FruP5ParadiseRegainedMitigation
{
    public const float TowerExplosion1 = 15.72f;
    public const float Cleave1 = 15.72f;
    public const float Buster1 = 16.52f;
    public const float TowerExplosion2 = 19.22f;
    public const float Cleave2 = 19.42f;
    public const float Buster2 = 20.22f;
    public const float TowerExplosion3 = 22.72f;
    public const float Lines1 = 37.43f;
    public const float Lines2 = 42.03f;
    public const float Lines3 = 46.63f;
    public const float Lines4 = 51.23f;

    private const uint PartyMit = MitPlanEntry.PartyMit;
    private static readonly float[] Lines = [Lines1, Lines2, Lines3, Lines4];

    public static MitPlanData Plan(int round = 0) => new(FruMitigation.Profile,
        [TowerExplosion, WingsCleave, WingsBuster, CruelPathOfLight, CruelPathOfDarkness],
        [
            new(TowerExplosion1, [TowerExplosion.Key, WingsCleave.Key]), new(Buster1, [WingsBuster.Key]),
            new(TowerExplosion2, [TowerExplosion.Key]), new(Cleave2, [WingsCleave.Key]), new(Buster2, [WingsBuster.Key]),
            new(TowerExplosion3, [TowerExplosion.Key]),
            new(Lines1, [CruelPathOfLight.Key, CruelPathOfDarkness.Key]), new(Lines2, [CruelPathOfLight.Key, CruelPathOfDarkness.Key]),
            new(Lines3, [CruelPathOfLight.Key, CruelPathOfDarkness.Key]), new(Lines4, [CruelPathOfLight.Key, CruelPathOfDarkness.Key]),
        ],
        [.. (round == 0 ? WingRows() : []), .. PolarizingStrikesRows(round)], FruMitigation.Pandora);

    // The first wing's cleave goes to the main tank and its buster to the off tank; the second's the
    // other way round. A Warrior takes the first cleave with Holmgang and covers the buster with
    // Nascent Flash; the other jobs sink the first buster, and take the second cleave on their
    // invulnerability with their buddy mitigation on the buster.
    private static IEnumerable<MitPlanEntry> WingRows()
    {
        foreach (var tank in new[] { MT, OT })
        {
            yield return Row(tank, Warrior, Holmgang, Cleave1, "Wings Dark and Light 1");
            yield return Row(tank, Warrior, NascentFlash, Buster1, "Wings Dark and Light 1", buddy: true);
            foreach (var sink in KitchenSink(tank, Warrior, Buster2, "Wings Dark and Light 2")) yield return sink;

            foreach (var job in new[] { Paladin, Gunbreaker, DarkKnight })
            {
                foreach (var sink in FirstWingSink(tank, job)) yield return sink;
                yield return Row(tank, job, job switch { Paladin => HallowedGround, Gunbreaker => Superbolide, _ => LivingDead }, Cleave2, "Wings Dark and Light 2");
                foreach (var buddy in BuddyMit(tank, job, Buster2, "Wings Dark and Light 2")) yield return buddy;
            }
        }
    }

    // Without the buttons the same job's buddy mitigation for the second wing needs back.
    private static MitPlanEntry[] FirstWingSink(SheetColumn tank, JobId job) => job switch
    {
        Gunbreaker => Sink(tank, job, Buster1, "Wings Dark and Light 1", MitActionId.Rampart, Camouflage, GreatNebula),
        DarkKnight => Sink(tank, job, Buster1, "Wings Dark and Light 1", MitActionId.Rampart, ShadowedVigil, DarkMind),
        _ => KitchenSink(tank, job, Buster1, "Wings Dark and Light 1"),
    };

    private static IEnumerable<MitPlanEntry> PolarizingStrikesRows(int round)
    {
        var ps = Lines1;
        string label = $"Polarizing Strikes {round + 1}";
        if (round == 0)
        {
            yield return new(MT, MitActionId.Reprisal, ps - Long, label, Lines);
            yield return new(OT, PartyMit, ps - Short, label, Lines);
            yield return new(SCH, SummonSeraph, ps - Short, label, Lines);
            yield return new(SCH, FeyIllumination, ps - Long, label, Lines);
            yield return new(SCH, Expedient, ps - Long, label, Lines);
            yield return new(SCH, SacredSoil, ps - Long, label, Lines);
            yield return new(SGE, Panhaima, ps - Short, label, Lines);
            yield return new(SGE, Kerachole, ps - Long, label, Lines);
            yield return new(WHM, Temperance, ps - Long, label, Lines);
            yield return new(WHM, DivineCaress, ps - Short, label, Lines);
            yield return new(AST, NeutralSect, ps - Long, label, []);
            yield return new(AST, SunSign, ps - Short, label, Lines);
            yield return new(D3, PartyMit, ps - Long, label, Lines);
            yield break;
        }
        yield return new(OT, PartyMit, ps - Short, label, Lines);
        yield return new(SCH, SummonSeraph, ps - Short, label, Lines);
        yield return new(SCH, Seraphism, ps - Long, label, []);
        yield return new(SCH, Expedient, ps - Long, label, Lines);
        yield return new(SCH, FeyIllumination, ps - Long, label, Lines);
        yield return new(SGE, Panhaima, ps - Short, label, Lines);
        yield return new(SGE, Philosophia, ps - Long, label, []);
        yield return new(WHM, LiturgyOfTheBell, ps - Long, label, []);
        yield return new(AST, Macrocosmos, ps - Long, label, []);
        yield return new(D3, PartyMit, ps - Long, label, Lines);
        foreach (var barrier in Barrier(ps, label)) yield return barrier;
    }
}
