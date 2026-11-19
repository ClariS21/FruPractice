using System.Numerics;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P4Full;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;
using AnoMech.Scenarios.Umad.P5Full;

namespace AnoMech.Tests.Scenarios.Umad;

// DMU's declared uptime windows: the runs stay clean with them on, no uptime move ends in a hazard that fires before
// the bot's next strat move, melee keep to their boss, and in P3 each seat fights the boss of its Decisive Battle group.
// (Celestriad declares none: its towers hold their soakers through the drill. Forsaken Null is a replay with no strat.)
public class UmadUptimeWindowTests
{
    private const int Seeds = 30;
    private const int WatchedSeeds = 10;

    public static IEnumerable<TestCaseData> Scenarios() =>
    [
        new TestCaseData(typeof(UmadP3LimitCutScenario), false).SetName("P3 Limit Cut"),
        new TestCaseData(typeof(UmadP3BlackHoleScenario), false).SetName("P3 Black Hole"),
        new TestCaseData(typeof(UmadP3FullScenario), true).SetName("P3 Full"),
        new TestCaseData(typeof(UmadP4KefkaSaysScenario), false).SetName("P4 Kefka Says"),
        new TestCaseData(typeof(UmadP4FullScenario), false).SetName("P4 Full"),
        new TestCaseData(typeof(UmadP5ExaflaresScenario), false).SetName("P5 Exaflares"),
        new TestCaseData(typeof(UmadP5FloodScenario), false).SetName("P5 Flood"),
        new TestCaseData(typeof(UmadP5FullScenario), true).SetName("P5 Full"),
    ];

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    [TestCaseSource(nameof(Scenarios))]
    public void WithTheWindowsNobodyDiesAndNoMechanicFails(Type scenario, bool bots)
    {
        var moves = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var hooked = false;
            var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                WriteArtifactsOnFailure = false,
                Probe = p =>
                {
                    if (hooked) return;
                    hooked = true;
                    p.World.Uptime.Issued += _ => moves++;
                },
            });
            Assert.That(run.Passed, $"seed {seed}: {run}");
        }
        TestContext.Out.WriteLine($"UPTIME {scenario.Name}: {moves} uptime moves over {Seeds} seeds");
        // Only a seat on a habit move has a strat spot to keep near; the other drills move their bots plainly, so their windows stay unused.
        if (bots) Assert.That(moves, Is.GreaterThan(0), "the windows let uptime run");
    }

    [TestCaseSource(nameof(Scenarios))]
    public void NoUptimeMoveEndsInAHazardThatGoesOffBeforeTheNextStratMove(Type scenario, bool bots)
    {
        var checkedMoves = 0;
        for (var seed = 0; seed < WatchedSeeds; seed++)
        {
            var failures = new List<string>();
            var hooked = false;
            ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                WriteArtifactsOnFailure = false,
                Probe = p =>
                {
                    if (hooked) return;
                    hooked = true;
                    var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                    p.World.Uptime.Issued += move =>
                    {
                        checkedMoves++;
                        var until = move.NextStrat ?? move.At + 5f;
                        foreach (var hazard in source.Upcoming(move.At, until - move.At))
                            foreach (var (name, spot) in new[] { ("destination", move.Destination), ("settle", move.Settle) })
                                if (hazard.Shape.SignedDistance(new Vector2(spot.X, spot.Z)) <= 1f)
                                    failures.Add($"{move.Role} {name} at {move.At:F1} is within 1 y of {hazard.Label} ({hazard.FiresAt:F1})");
                    };
                },
            });
            Assert.That(failures, Is.Empty, $"seed {seed}: {string.Join("; ", failures)}");
        }
        TestContext.Out.WriteLine($"UPTIME {scenario.Name}: {checkedMoves} moves checked over {WatchedSeeds} seeds");
    }

    [TestCaseSource(nameof(Scenarios))]
    public void MeleeStayWithinReachOfTheirBossThroughTheirUptime(Type scenario, bool bots)
    {
        int near = 0, total = 0;
        for (var seed = 0; seed < WatchedSeeds; seed++)
            ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                WriteArtifactsOnFailure = false,
                Probe = p =>
                {
                    foreach (var role in Enum.GetValues<PartyRole>())
                    {
                        if (SimWorld.HabitRoleOf(role) != HabitRole.Melee || !p.World.Uptime.IsEngaged(role)) continue;
                        if (p.World.Party.Combat?.FindTarget(role) is not { } boss) continue;
                        total++;
                        if (Flat(p.Member(role)!.Position, boss.Position) <= boss.HitboxRadius + 3.5f) near++;
                    }
                },
            });
        TestContext.Out.WriteLine($"UPTIME {scenario.Name}: melee in reach {near}/{total}");
        if (total > 0) Assert.That(near / (float)total, Is.GreaterThanOrEqualTo(0.8f), $"{near}/{total} window frames in reach");
    }

    // Decisive Battle's Chaos group is its tank, H1 (Regen) and both melee; the rest and Exdeath's tank fight Exdeath.
    [TestCase(typeof(UmadP3LimitCutScenario), 12f)]
    [TestCase(typeof(UmadP3FullScenario), 20f)]
    public void EachP3SeatFightsTheBossOfItsGroup(Type scenario, float at)
    {
        var checkedSeats = 0;
        var run = ScenarioRun.Execute(scenario, 0, 1, new ScenarioRunOptions
        {
            WriteArtifactsOnFailure = false,
            StopAt = at + 0.5f,
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                var tanks = scenario == typeof(UmadP3FullScenario) ? UmadP3Tanks.For(p.World.Party) : UmadP3Tanks.MainOnChaos;
                foreach (var role in Enum.GetValues<PartyRole>())
                {
                    var chaosSide = role == tanks.Chaos || role is PartyRole.RegenHealer or PartyRole.MeleeDpsA or PartyRole.MeleeDpsB;
                    var target = p.World.Party.Combat!.FindTarget(role);
                    Assert.That(target?.DisplayName, Is.EqualTo(chaosSide ? "Chaos" : "Exdeath"), role.ToString());
                    checkedSeats++;
                }
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(checkedSeats, Is.EqualTo(8));
    }
}
