using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P5Full;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

// Every ordered tank pair, a same-job one included: each buster's tank rows are on the tank that takes
// that hit, each invuln is pressed by the tank the pair gives it, and the bots live through the phase.
public class UmadFullTankPairSweepTests
{
    private static readonly JobId[] TankJobs = [Warrior, Paladin, Gunbreaker, DarkKnight];

    private static IEnumerable<TestCaseData> Pairs()
    {
        foreach (var main in TankJobs)
            foreach (var off in TankJobs)
                yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    // The bots' comp with the pair in the tank seats.
    private static Dictionary<PartyRole, JobId> Comp(JobId main, JobId off)
        => new(MitigationPlanAssert.Comps().First().Jobs) { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };

    // A tank's own cooldowns, every job's, and its invulnerability.
    private static readonly HashSet<uint> Personal =
    [
        MitActionId.Rampart,
        .. TankJobs.SelectMany(job => new[]
        {
            UmadMitigationRows.NinetySeconds(job), UmadMitigationRows.FortyPercent(job), UmadMitigationRows.ShortMit(job),
            UmadMitigationRows.Invulnerability(job),
        }),
    ];

    private static readonly HashSet<uint> Invulns = [.. TankJobs.Select(UmadMitigationRows.Invulnerability)];

    // The statuses each invulnerability puts up.
    private static readonly ushort[] InvulnStatuses = [409, 82, 1836, 810];

    private static bool Covers(ResolvedPress press, float hit) => press.Covers.Concat(press.Soft).Any(c => MathF.Abs(c - hit) < 0.05f);

    private static readonly float[][] P3Sets =
    [
        UmadP3OpeningMitigation.ThunderAt,
        [.. UmadP3LimitCutMitigation.ThunderAt.Select(t => UmadP3FullScenario.LimitCutAt + t)],
        UmadP3FullMitigation.BridgeThunderAt,
        [.. UmadP3BlackHoleMitigation.FirstThunderAt.Select(t => UmadP3FullScenario.BlackHoleAt + t)],
        [.. UmadP3BlackHoleMitigation.SecondThunderAt.Select(t => UmadP3FullScenario.BlackHoleAt + t)],
    ];

    [TestCaseSource(nameof(Pairs))]
    public void P3FullBusterRowsFollowTheBossEachTankHolds(JobId main, JobId off)
    {
        var jobs = Comp(main, off);
        var tanks = UmadP3Tanks.For(main, off);
        if (main == off) Assert.That(tanks.Chaos, Is.EqualTo(PartyRole.MainTank), "a same-job pair keeps Chaos on the main tank");
        var busters = UmadMitigationPlanTests.DefaultBusters(tanks);
        var plan = MitPlan.Resolve(UmadP3FullScenario.MitigationPlan(tanks, busters).Entries, jobs);
        // The group's order: TB1 the Chaos tank first, TB2 its invuln, then the Exdeath tank first, its invuln, first again.
        PartyRole[] firstTaker = [tanks.Chaos, tanks.Chaos, tanks.Exdeath, tanks.Exdeath, tanks.Exdeath];
        for (var set = 0; set < P3Sets.Length; set++)
        {
            var (first, second) = ThunderIIIPlanning.Roles(busters[set]);
            Assert.That(first, Is.EqualTo(firstTaker[set]), $"Thunder III {set + 1}'s first hit");
            var hits = P3Sets[set];
            PartyRole[] takers = [first, second ?? first];
            for (var h = 0; h < 2; h++)
            {
                var rows = plan.Where(p => Personal.Contains(p.Action.ActionId) && Covers(p, hits[h])).ToList();
                Assert.That(rows, Is.Not.Empty, $"Thunder III {set + 1} hit {h + 1}: no tank cooldowns");
                Assert.That(rows.Select(p => p.Slot).Distinct(), Is.EqualTo(new[] { takers[h] }), $"Thunder III {set + 1} hit {h + 1}");
            }
            if (second is null)
                Assert.That(plan.Count(p => Invulns.Contains(p.Action.ActionId) && p.Slot == first && Covers(p, hits[0]) && Covers(p, hits[1])), Is.EqualTo(1),
                    $"Thunder III {set + 1}: the invuln");
        }
    }

    [TestCaseSource(nameof(Pairs))]
    public void P5FullInvulnsFollowTheOmnitankOrder(JobId main, JobId off)
    {
        var jobs = Comp(main, off);
        var first = UmadP5TankPair.HolyTank(main, off, 0);
        if (main == off) Assert.That(first, Is.EqualTo(PartyRole.MainTank), "a same-job pair has the main tank invuln first");
        var plan = MitPlan.Resolve(UmadP5FullScenario.MitigationPlan(first).Entries, jobs);
        PartyRole[] invulners = [first, UmadP5TankPair.HolyTank(main, off, 1)];
        for (var round = 0; round < 2; round++)
        {
            var holy = UmadP5FullMitigation.OrchestraAt[round][2];
            var invulns = plan.Where(p => Invulns.Contains(p.Action.ActionId) && Covers(p, holy)).ToList();
            Assert.That(invulns.Select(p => p.Slot), Is.EqualTo(new[] { invulners[round] }), $"Surprise Holy {round + 1}");
            var alone = UmadP5FullMitigation.FellForcesAt.Where(t => t > holy).Take(2);
            Assert.That(alone.All(t => Covers(invulns[0], t)), $"Surprise Holy {round + 1}: the Fell Forces taken alone");
        }
    }

    // Two bot tanks never share a job (the off tank's look moves it off the main tank's), so a same-job
    // pair seats the player, bot-driven, as the off tank.
    private static ScenarioRun Run(Type type, JobId main, JobId off, int seed, Action<ScenarioProbe> probe)
        => ScenarioRun.Execute(type, 0, seed, new ScenarioRunOptions
        {
            PlayerRole = main == off ? PartyRole.OffTank : PartyRole.CasterDps,
            JobOverrides = Comp(main, off),
            UserActionsEnabled = true,
            Configure = c => c.MitigationPractice = true,
            Probe = probe,
        });

    private static bool Invulnerable(ScenarioProbe p, PartyRole seat) => p.Member(seat) is { } tank && InvulnStatuses.Any(tank.HasStatus);

    [TestCaseSource(nameof(Pairs))]
    public void P3FullSurvivesEveryTankPair(JobId main, JobId off)
    {
        var tanks = UmadP3Tanks.For(main, off);
        for (var seed = 0; seed < 2; seed++)
        {
            bool? chaosInvuln = null, exdeathInvuln = null;
            var run = Run(typeof(UmadP3FullScenario), main, off, seed, p =>
            {
                if (p.Crossed(P3Sets[1][0])) chaosInvuln = Invulnerable(p, tanks.Chaos);
                if (p.Crossed(P3Sets[3][0])) exdeathInvuln = Invulnerable(p, tanks.Exdeath);
            });
            Assert.That(chaosInvuln, Is.True, $"seed {seed}: the Chaos tank's invuln for Thunder III 2");
            Assert.That(exdeathInvuln, Is.True, $"seed {seed}: the Exdeath tank's invuln for Thunder III 4");
            Assert.That(run.WouldHaveKilled, Is.Empty, $"seed {seed}");
            Assert.That(run.Passed, $"seed {seed}: {run}");
        }
    }

    [TestCaseSource(nameof(Pairs))]
    public void P5FullSurvivesEveryTankPair(JobId main, JobId off)
    {
        PartyRole[] invulners = [UmadP5TankPair.HolyTank(main, off, 0), UmadP5TankPair.HolyTank(main, off, 1)];
        for (var seed = 0; seed < 2; seed++)
        {
            var invulned = new bool?[2];
            var run = Run(typeof(UmadP5FullScenario), main, off, seed, p =>
            {
                for (var round = 0; round < 2; round++)
                    if (p.Crossed(UmadP5FullMitigation.OrchestraAt[round][2])) invulned[round] = Invulnerable(p, invulners[round]);
            });
            Assert.That(invulned, Is.All.True, $"seed {seed}: the Surprise Holy tanks' invulns");
            Assert.That(run.WouldHaveKilled, Is.Empty, $"seed {seed}");
            Assert.That(run.Passed, $"seed {seed}: {run}");
        }
    }
}
