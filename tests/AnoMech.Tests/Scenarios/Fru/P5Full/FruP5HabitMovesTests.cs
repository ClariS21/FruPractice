using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5Full;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Tests;

// Every P5 strat move is habit-timed: a bot is on its spot 0.8 s before its inBy, or out of the danger
// 0.8 s before its outBy, whichever the move declares; and each drill runs clean over 20 seeds.
public class FruP5HabitMovesTests
{
    private const int Seeds = 20;
    private const float Margin = AiManager.StaggeredArrivalMargin;
    private const float Reach = 0.05f;
    private const float StartSlack = 0.2f;

    private static IEnumerable<TestCaseData> Drills() =>
    [
        new TestCaseData(typeof(FruP5FulgentBladeScenario), Seeds).SetName("Fulgent Blade"),
        new TestCaseData(typeof(FruP5ParadiseRegainedScenario), Seeds).SetName("Paradise Regained"),
        new TestCaseData(typeof(FruP5FullScenario), Seeds).SetName("Full phase"),
    ];

    [TestCaseSource(nameof(Drills))]
    public void EveryHabitMoveKeepsItsMarginAndNoOneDies(Type scenario, int seeds)
    {
        var violations = new List<string>();
        var failures = new List<string>();
        var moves = 0;
        for (var seed = 0; seed < seeds; seed++)
        {
            var watch = new Watch();
            var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions { Probe = watch.Frame, WriteArtifactsOnFailure = false });
            moves += watch.Moves;
            violations.AddRange(watch.Violations.Select(v => $"seed {seed}: {v}"));
            if (!run.Passed) failures.Add(run.ToString());
        }
        TestContext.Out.WriteLine($"{scenario.Name}: {moves} habit moves over {seeds} seeds");
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);
            Assert.That(violations, Is.Empty);
            Assert.That(moves, Is.GreaterThan(0));
        });
    }

    private sealed class Watch
    {
        private readonly Dictionary<PartyRole, (HabitPlan Plan, Vector3 Start, bool Done)> watched = new();
        public readonly List<string> Violations = [];
        public int Moves;

        public void Frame(ScenarioProbe p)
        {
            var world = p.World;
            foreach (var role in Enum.GetValues<PartyRole>())
            {
                if (role == world.Party.PlayerRole || world.Party.Get(role) is not SimPartyNpc bot) continue;
                if (world.Movement.PlanFor(role) is not { } plan)
                {
                    watched.Remove(role);
                    continue;
                }
                if (!watched.TryGetValue(role, out var entry) || !ReferenceEquals(entry.Plan, plan))
                {
                    watched[role] = entry = (plan, bot.Position, false);
                    Moves++;
                }
                if (entry.Done || !bot.IsAlive() || bot.ForcedMoving)
                {
                    watched[role] = entry with { Done = true };
                    continue;
                }
                var order = plan.Order;
                var off = Flat(bot.Position, order.Spot);
                var inRoom = off <= order.Room + Reach;
                var now = p.Time;
                string? violation = null;
                if (order.InBy is { } inBy && now >= inBy - Margin && now <= inBy && !inRoom)
                    violation = $"{off:F2} y off its spot {inBy - now:F2} s before its inBy {inBy:F2}";
                else if (order.OutBy is { } outBy && now >= outBy - Margin && now <= outBy
                         && !(order.ExitDistance is { } exit ? Flat(bot.Position, entry.Start) >= exit - StartSlack : inRoom))
                    violation = $"still in the danger {outBy - now:F2} s before its outBy {outBy:F2}";
                if (violation is null) continue;
                Violations.Add($"t={now:F2}: {role} {violation}");
                watched[role] = entry with { Done = true };
            }
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }
}
