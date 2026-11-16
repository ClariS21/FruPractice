using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using AnoMech.Core.UserActions;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Fru.FruMitigation.P5;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P5ParadiseRegained;

// The mitigation sheet's P5 rows for a Paradise Regained: the wings' tank cooldowns and the
// Polarizing Strikes, on this scenario's clock. The sheet's two Wings rows are the two casts, one per round. The second round's
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
        [.. WingRows(round), .. PolarizingStrikesRows(round)], FruMitigation.Pandora);

    // The sheet's tank tabs give Wings Dark and Light once per cast: one tank invulns it, the other
    // kitchen-sinks it, and the invulnerable one gives its buddy mitigation on the sink's second hit.
    // Each tank keeps its lane: the main tank takes the cleave then the buster, the off tank the
    // buster then the cleave. Which job invulns which cast is FruP5WingsTankPair's.
    private static IEnumerable<MitPlanEntry> WingRows(int round)
    {
        string label = $"Wings Dark and Light {round + 1}";
        var mtHits = new[] { Cleave1, Buster2 };
        var otHits = new[] { Buster1, Cleave2 };
        foreach (var tank in new[] { MT, OT })
            foreach (var job in FruMitigationRows.TankJobs)
                foreach (var partner in FruMitigationRows.TankJobs)
                {
                    var hits = tank == MT ? mtHits : otHits;
                    var buddyHit = tank == MT ? otHits[1] : mtHits[1];
                    var rows = FruP5WingsTankPair.Invulns(tank == MT, job, partner, round)
                        ? Invuln(tank, job, hits, buddyHit, label)
                        : Sink(tank, job, hits, label);
                    foreach (var row in rows) yield return row with { Partners = [partner] };
                }
    }

    private static MitPlanEntry[] Invuln(SheetColumn tank, JobId job, float[] hits, float buddyHit, string label)
        => [Row(tank, job, job switch { Warrior => Holmgang, Paladin => HallowedGround, Gunbreaker => Superbolide, _ => LivingDead }, hits[0], label) with { Covers = hits },
            .. BuddyMit(tank, job, buddyHit, label)];

    // Everything but the invulnerability, up for both hits; the buttons a job can use again for the
    // second hit (Heart of Corundum, The Blackest Night) go out on it.
    private static MitPlanEntry[] Sink(SheetColumn tank, JobId job, float[] hits, string label)
    {
        var both = KitchenSink(tank, job, hits[0], label).Where(r => !(job == Gunbreaker && r.ActionId == HeartOfCorundum || job == DarkKnight && r.ActionId is TheBlackestNight or Oblation))
            .Select(r => r with { Covers = hits });
        var second = job switch
        {
            Gunbreaker => [Row(tank, job, HeartOfCorundum, hits[1], label)],
            DarkKnight => [Row(tank, job, TheBlackestNight, hits[1], label)],
            _ => System.Array.Empty<MitPlanEntry>(),
        };
        return [.. both, .. second];
    }

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
