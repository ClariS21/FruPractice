using System.Numerics;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;

namespace AnoMech.Tests;

// P3's declared uptime windows: the drills still run clean with them on, melee stay within reach of the boss
// through them, and no uptime destination lies in a hazard that goes off before the bot's next strat move.
public class P3UptimeWindowTests
{
    private const int Seeds = 50;
    // The measuring tests watch every frame, so they take fewer.
    private const int WatchedSeeds = 15;

    private static IEnumerable<TestCaseData> Drills()
    {
        yield return new TestCaseData(typeof(FruP3UltimateRelativityScenario)).SetName("Ultimate Relativity");
        yield return new TestCaseData(typeof(FruP3ApocalypseScenario)).SetName("Apocalypse");
        yield return new TestCaseData(typeof(FruP3FullScenario)).SetName("Full phase");
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    [TestCaseSource(nameof(Drills))]
    public void With50SeedsOfWindowsNobodyDiesAndNoMechanicFails(Type scenario)
    {
        var moves = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var hooked = false;
            var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
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
        Assert.That(moves, Is.GreaterThan(0), "the windows let uptime run");
    }

    [TestCaseSource(nameof(Drills))]
    public void MeleeStayWithinReachOfTheBossForMostOfTheirWindowTime(Type scenario)
    {
        int near = 0, total = 0;
        for (var seed = 0; seed < WatchedSeeds; seed++)
            ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                Probe = p =>
                {
                    if (p.World.Party.Combat?.FindTarget() is not { } boss) return;
                    foreach (var role in Enum.GetValues<PartyRole>())
                    {
                        if (SimWorld_HabitRole(role) != HabitRole.Melee || !p.World.Uptime.IsEngaged(role)) continue;
                        total++;
                        if (Flat(p.Member(role)!.Position, boss.Position) <= boss.HitboxRadius + 3.5f) near++;
                    }
                },
            });
        TestContext.Out.WriteLine($"UPTIME {scenario.Name}: melee in reach {near}/{total}");
        Assert.That(total, Is.GreaterThan(0), "melee had uptime");
        Assert.That(near / (float)total, Is.GreaterThanOrEqualTo(0.8f), $"{near}/{total} window frames in reach");
    }

    private static HabitRole SimWorld_HabitRole(PartyRole role) => AnoMech.Core.SimObjects.SimWorld.HabitRoleOf(role);

    [TestCaseSource(nameof(Drills))]
    public void NoUptimeDestinationLiesInAHazardThatGoesOffBeforeTheNextStratMove(Type scenario)
    {
        var checkedMoves = 0;
        for (var seed = 0; seed < WatchedSeeds; seed++)
        {
            var failures = new List<string>();
            var hooked = false;
            ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
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
}
