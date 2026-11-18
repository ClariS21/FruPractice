using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3Full;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Core.UserActions.JobId;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

// Of a tank pair, Chaos goes to the first of Warrior, Dark Knight, Gunbreaker, Paladin and Exdeath
// to the other, whichever seat each sits in; every boss-held step of the phase follows that.
public class UmadP3FullTankPairSweepTests
{
    private static IEnumerable<TestCaseData> Pairs()
    {
        JobId[] tanks = [Warrior, Paladin, Gunbreaker, DarkKnight];
        foreach (var main in tanks)
            foreach (var off in tanks)
                if (main != off) yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    private static readonly JobId[] ChaosFirst = [Warrior, DarkKnight, Gunbreaker, Paladin];

    private static PartyRole ChaosHolder(JobId main, JobId off)
        => Array.IndexOf(ChaosFirst, off) < Array.IndexOf(ChaosFirst, main) ? OffTank : MainTank;

    [TestCase(Gunbreaker, DarkKnight, OffTank)]
    [TestCase(DarkKnight, Gunbreaker, MainTank)]
    [TestCase(Warrior, Paladin, MainTank)]
    [TestCase(Paladin, Warrior, OffTank)]
    [TestCase(Gunbreaker, Paladin, MainTank)]
    [TestCase(Paladin, Gunbreaker, OffTank)]
    [TestCase(Warrior, Warrior, MainTank)]
    public void ChaosGoesToTheFirstOfWarriorDarkKnightGunbreakerPaladin(JobId main, JobId off, PartyRole chaos)
    {
        var tanks = UmadP3Tanks.For(main, off);
        Assert.That(tanks.Chaos, Is.EqualTo(chaos));
        Assert.That(tanks.Exdeath, Is.EqualTo(chaos == MainTank ? OffTank : MainTank));
    }

    // Each buster's two hits, by the boss each tank holds: a swap, the Chaos tank's invuln, a swap, the
    // Exdeath tank's invuln, a swap.
    private static readonly (float First, float Second, bool ChaosFirst, bool Shared)[] Busters =
    [
        (50.97f, 54.00f, true, true),
        (109.67f, 112.70f, true, false),
        (126.80f, 129.83f, false, true),
        (168.35f, 171.35f, false, false),
        (209.66f, 212.66f, false, true),
    ];

    [TestCaseSource(nameof(Pairs))]
    public void EveryBossHeldStepFollowsThePair(JobId main, JobId off)
    {
        var chaos = ChaosHolder(main, off);
        var exdeath = chaos == MainTank ? OffTank : MainTank;
        var hits = new List<(float At, PartyRole[] Roles)>();
        PartyRole[] epic = [];
        var run = ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = CasterDps,
            JobOverrides = new Dictionary<PartyRole, JobId> { [MainTank] = main, [OffTank] = off },
            Probe = p =>
            {
                if (p.Crossed(3.6f)) epic = [.. PerRole.All.Where(r => p.Member(r)!.HasStatus(StatusId.EpicHero))];
                foreach (var (first, second, _, _) in Busters)
                    foreach (var at in new[] { first, second })
                        if (p.Crossed(at + 0.4f))
                            hits.Add((at, [.. PerRole.All.Where(r => p.Member(r)!.HasStatus(StatusId.LightningResistanceDownII))]));
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(epic, Does.Contain(chaos).And.Not.Contain(exdeath), "Epic Hero");
        Assert.Multiple(() =>
        {
            for (var k = 0; k < Busters.Length; k++)
            {
                var (_, _, chaosFirst, shared) = Busters[k];
                var first = chaosFirst ? chaos : exdeath;
                var second = shared ? (chaosFirst ? exdeath : chaos) : first;
                Assert.That(hits[2 * k].Roles, Is.EquivalentTo(new[] { first }), $"Thunder III {k + 1}, first hit");
                Assert.That(hits[2 * k + 1].Roles, Is.EquivalentTo(new[] { first, second }.Distinct()), $"Thunder III {k + 1}, second hit");
            }
        });
    }
}
