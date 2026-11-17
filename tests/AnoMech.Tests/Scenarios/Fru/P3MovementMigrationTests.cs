using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;

namespace AnoMech.Tests;

// P3's drills on the habit-timed movement layer: nobody dies and no mechanic fails, and every bot's strat
// move keeps the 0.8 s margin to the hit it declares.
public class P3MovementMigrationTests
{
    private const float Margin = AiManager.StaggeredArrivalMargin;

    private static IEnumerable<TestCaseData> Drills()
    {
        yield return new TestCaseData(typeof(FruP3UltimateRelativityScenario)).SetName("Ultimate Relativity");
        yield return new TestCaseData(typeof(FruP3ApocalypseScenario)).SetName("Apocalypse");
        yield return new TestCaseData(typeof(FruP3FullScenario)).SetName("Full phase");
    }

    [TestCaseSource(nameof(Drills))]
    public void NoBotDiesAndNoMechanicFailsOver20Seeds(Type scenario)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var run = ScenarioRun.Execute(scenario, 0, seed);
            Assert.That(run.Passed, $"seed {seed}: {run}");
        }
    }

    private sealed class Watched(HabitOrder order, Vector3 start, float seenAt)
    {
        public HabitOrder Order { get; } = order;
        public Vector3 Start { get; } = start;
        public float SeenAt { get; } = seenAt;
        public bool InChecked, OutChecked;
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    [TestCaseSource(nameof(Drills))]
    public void EveryBotStratMoveBeatsItsHitByTheMarginOrClearsItsAreaEarly(Type scenario)
    {
        var forced = 0;
        var checkedMoves = 0;
        for (var seed = 0; seed < 20; seed++)
        {
            var watched = new Dictionary<(PartyRole, int), Watched>();
            var player = (PartyRole)(seed % 8);
            var failures = new List<string>();
            var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                PlayerRole = player,
                Probe = p =>
                {
                    foreach (var role in Enum.GetValues<PartyRole>())
                    {
                        if (role == player || p.Member(role) is not { } member) continue;
                        if (p.World.Movement.PlanFor(role) is { } plan && !watched.ContainsKey((role, plan.Generation)))
                            watched[(role, plan.Generation)] = new Watched(plan.Order, member.Position, p.Time);
                    }
                    foreach (var ((role, generation), move) in watched)
                    {
                        var bot = p.Member(role);
                        if (bot is null) continue;
                        // A move a newer order replaced is judged by that order.
                        var current = p.World.Movement.PlanFor(role)?.Generation == generation;
                        var order = move.Order;
                        if (!move.InChecked && order.InBy is { } inBy && p.Time >= inBy - Margin)
                        {
                            move.InChecked = true;
                            var walk = Flat(move.Start, order.Spot);
                            // Even sprinting from the moment it was seen, the bot can't make it: the move is forced.
                            if (move.SeenAt + (walk - order.Room) / AiManager.SprintSpeed > inBy - Margin) forced++;
                            else if (current)
                            {
                                checkedMoves++;
                                if (Flat(bot.Position, order.Spot) > order.Room + 0.15f)
                                    failures.Add($"{role} is {Flat(bot.Position, order.Spot):F2} y from its spot {Margin:F1} s before its inBy {inBy:F2} (seen {move.SeenAt:F2}, {walk:F1} y)");
                            }
                        }
                        if (!move.OutChecked && order.OutBy is { } outBy && p.Time >= outBy - Margin)
                        {
                            move.OutChecked = true;
                            var exit = order.ExitDistance ?? Flat(move.Start, order.Spot);
                            if (move.SeenAt + exit / AiManager.SprintSpeed > outBy - Margin) forced++;
                            else if (current)
                            {
                                checkedMoves++;
                                var left = Flat(bot.Position, move.Start);
                                var arrived = Flat(bot.Position, order.Spot) <= order.Room + 0.15f;
                                if (left < exit - 0.25f && !arrived)
                                    failures.Add($"{role} has left {left:F2} of the {exit:F1} y it needs {Margin:F1} s before its outBy {outBy:F2} (seen {move.SeenAt:F2})");
                            }
                        }
                    }
                },
            });
            Assert.That(run.Passed, $"seed {seed}: {run}");
            Assert.That(failures, Is.Empty, $"seed {seed}: {string.Join("; ", failures)}");
        }
        TestContext.Out.WriteLine($"MARGIN {scenario.Name}: {checkedMoves} deadlines checked, {forced} forced (even a sprint from the start misses the margin)");
        Assert.That(checkedMoves, Is.GreaterThan(100));
    }
}
