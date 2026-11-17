using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// The habit layer's promises on every drill of the catalog (not the Full phases): a habit-timed bot
// move is on its spot for the whole last 0.8 s before its inBy and out of its danger for the last
// 0.8 s before its outBy, never stands on a notBefore spot early, a settled or idling bot stays within
// room of its spot, and the human's seat never gets a habit move.
public class HabitDrillInvariantTests
{
    private const int Seeds = 20;
    private const float Reach = 0.05f;

    [Test]
    public void HabitMovesKeepTheMarginAndTheRoomAndLeaveTheHumanAlone()
    {
        var violations = new List<string>();
        var total = 0;
        foreach (var scenario in ScenarioCatalog.Create().Scenarios.Where(s => s.Name != "Full phase"))
            for (var strat = 0; strat < scenario.AiStrats.Count; strat++)
            {
                var moves = 0;
                for (var seed = 0; seed < Seeds; seed++)
                {
                    var watch = new HabitWatch();
                    ScenarioRun.Execute(scenario.GetType(), strat, seed, new ScenarioRunOptions { Probe = watch.Frame });
                    moves += watch.Moves;
                    violations.AddRange(watch.Violations.Select(v => $"{scenario.FullName()} [{scenario.AiStrats[strat].Name}] seed {seed}: {v}"));
                }
                TestContext.Out.WriteLine($"{scenario.FullName()} [{scenario.AiStrats[strat].Name}]: {moves} habit moves over {Seeds} seeds");
                total += moves;
            }
        Assert.Multiple(() =>
        {
            Assert.That(violations, Is.Empty);
            Assert.That(total, Is.GreaterThan(0), "no drill exercised a habit move");
        });
    }

    private sealed class HabitWatch
    {
        private const float Margin = AiManager.StaggeredArrivalMargin;
        // One frame at a sprint: the bot may already have set off on the frame its move fired.
        private const float StartSlack = 0.2f;

        private readonly Dictionary<PartyRole, (HabitPlan Plan, Vector3 Start, bool Done)> watched = new();
        public readonly List<string> Violations = [];
        public int Moves;

        public void Frame(ScenarioProbe p)
        {
            var world = p.World;
            var human = world.Party.PlayerRole;
            if (world.Movement.PlanFor(human) is not null) Violations.Add($"t={p.Time:F2}: a habit move for the human's seat {human}");
            foreach (var role in Enum.GetValues<PartyRole>())
            {
                if (role == human || world.Party.Get(role) is not SimPartyNpc bot) continue;
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
                // A knockback or a death takes the bot out of the habit layer's hands.
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
                else if (order.NotBefore is { } notBefore && now < notBefore - ScenarioRun.FrameSeconds && Flat(bot.Position, plan.Target) <= Reach)
                    violation = $"on its spot {notBefore - now:F2} s before its notBefore {notBefore:F2}";
                else if (world.Movement.IsSettled(role) && !bot.Relocating && !inRoom)
                    violation = $"settled {off:F2} y off its spot with room {order.Room:F2}";
                if (violation is null) continue;
                Violations.Add($"t={now:F2}: {role} {violation}");
                watched[role] = entry with { Done = true };
            }
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }
}
