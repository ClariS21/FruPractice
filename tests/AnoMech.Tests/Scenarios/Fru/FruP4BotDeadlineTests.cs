using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Tests;

// The P4 strats, drills and the Full phase, on their first 20 seeds: each drill clears; every habit-timed bot move is on
// its spot for the last 0.8 s before its inBy and out of its danger for the last 0.8 s before its outBy; and, checked
// against the hits themselves rather than the deadlines the strats declare, no bot stands in the area of a hit it has
// to avoid in the last 0.8 s before that hit goes off.
public class FruP4BotDeadlineTests
{
    private const int Seeds = 20;
    private const float Margin = AiManager.StaggeredArrivalMargin;
    private const float Reach = 0.05f;
    // One frame at a sprint: the bot may already have set off on the frame its move fired.
    private const float StartSlack = 0.2f;
    private const float TargetReach = 0.6f;

    private static readonly uint[] AvoidedByAll =
    [
        ActionId.Maelstrom, ActionId.TidalLightFirst, ActionId.TidalLightRest, ActionId.AkhRhaiCast, ActionId.AkhRhai,
        ActionId.HallowedWingsLeft, ActionId.HallowedWingsRight,
    ];
    private static readonly uint[] AvoidedByAllButTheTarget =
        [ActionId.DarkBlizzard, ActionId.DarkEruption, ActionId.SpiritTaker, ActionId.SomberDanceFar, ActionId.SomberDanceNear];

    private static IEnumerable<TestCaseData> Strats(bool drillsOnly)
        => from scenario in ScenarioCatalog.Create().Scenarios
           where scenario.Phase == FruZone.P4 && (!drillsOnly || scenario.Name != "Full phase")
           from strat in scenario.AiStrats.Select((ai, index) => (ai, index))
           select new TestCaseData(scenario.GetType(), strat.index).SetName($"{{m}}: {scenario.FullName()} [{strat.ai.Name}]");

    private static IEnumerable<TestCaseData> Drills() => Strats(drillsOnly: true);
    private static IEnumerable<TestCaseData> EveryP4Strat() => Strats(drillsOnly: false);

    [TestCaseSource(nameof(Drills))]
    public void TwentySeedsClear(Type scenario, int strat)
    {
        var failures = Enumerable.Range(0, Seeds)
            .Select(seed => ScenarioRun.Execute(scenario, strat, seed, new ScenarioRunOptions { WriteArtifactsOnFailure = false }))
            .Where(run => !run.Passed)
            .ToList();
        Assert.That(failures, Is.Empty, () => string.Join(Environment.NewLine, failures));
    }

    [TestCaseSource(nameof(EveryP4Strat))]
    public void BotsKeepEveryDeadlineWithTheMarginToSpare(Type scenario, int strat)
    {
        var violations = new List<string>();
        var moves = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var deadlines = new DeadlineWatch();
            var hits = new HitWatch();
            AoeQuery.Evaluated += hits.Evaluated;
            ScenarioRun run;
            try
            {
                run = ScenarioRun.Execute(scenario, strat, seed, new ScenarioRunOptions
                {
                    WriteArtifactsOnFailure = false,
                    Probe = p =>
                    {
                        deadlines.Frame(p);
                        hits.Frame(p);
                    },
                });
            }
            finally
            {
                AoeQuery.Evaluated -= hits.Evaluated;
            }
            moves += deadlines.Moves;
            violations.AddRange(deadlines.Violations.Concat(hits.Violations(run.PlayerRole)).Select(v => $"seed {seed}: {v}"));
        }
        TestContext.Out.WriteLine($"{moves} habit moves over {Seeds} seeds");
        Assert.Multiple(() =>
        {
            Assert.That(violations, Is.Empty);
            Assert.That(moves, Is.GreaterThan(0));
        });
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    // The declared side: inBy and outBy as the habit layer was handed them.
    private sealed class DeadlineWatch
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
                var now = p.Time;
                var onSpot = Flat(bot.Position, order.Spot) <= order.Room + Reach;
                string? violation = null;
                if (order.InBy is { } inBy && now >= inBy - Margin && now <= inBy && !onSpot)
                    violation = $"{Flat(bot.Position, order.Spot):F2} y off its spot {inBy - now:F2} s before its inBy {inBy:F2}";
                else if (order.OutBy is { } outBy && now >= outBy - Margin && now <= outBy
                         && !(order.ExitDistance is { } exit ? Flat(bot.Position, entry.Start) >= exit - StartSlack : onSpot))
                    violation = $"still in its danger {outBy - now:F2} s before its outBy {outBy:F2}";
                if (violation is null) continue;
                Violations.Add($"t={now:F2}: {role} {violation}");
                watched[role] = entry with { Done = true };
            }
        }
    }

    // The real side: where every bot stood in the 0.8 s before each hit it had to avoid.
    private sealed class HitWatch
    {
        private readonly List<(float Time, Vector3[] Positions, bool[] Bots)> frames = [];
        private readonly List<(float Time, AoeQuery Query, bool[] Clawed)> resolved = [];
        private SimWorld? world;

        public void Frame(ScenarioProbe p)
        {
            world = p.World;
            var roles = Enum.GetValues<PartyRole>();
            frames.Add((p.Time, roles.Select(r => p.Member(r)?.Position ?? Vector3.Zero).ToArray(),
                roles.Select(r => p.Member(r) is SimPartyNpc bot && bot.IsAlive()).ToArray()));
        }

        public void Evaluated(AoeQuery query)
        {
            if (world is null || !AvoidedByAll.Contains(query.ActionId) && !AvoidedByAllButTheTarget.Contains(query.ActionId)
                && query.ActionId != ActionId.LongingOfTheLost) return;
            resolved.Add((world.Events.Elapsed, query,
                Enum.GetValues<PartyRole>().Select(r => world.Party.Get(r)?.HasStatus(StatusId.Wyrmclaw) == true).ToArray()));
        }

        public IEnumerable<string> Violations(PartyRole human)
        {
            foreach (var (at, query, clawed) in resolved)
            {
                var atHit = frames.LastOrDefault(f => f.Time <= at + 0.001f);
                if (atHit.Positions is null) continue;
                for (var slot = 0; slot < 8; slot++)
                {
                    var role = (PartyRole)slot;
                    if (role == human || !atHit.Bots[slot]) continue;
                    if (query.ActionId == ActionId.LongingOfTheLost && clawed[slot]) continue;
                    if (AvoidedByAllButTheTarget.Contains(query.ActionId) && Flat(atHit.Positions[slot], query.Source.Position) < TargetReach) continue;
                    var lastInside = frames.Where(f => f.Time > at - Margin - 0.001f && f.Time <= at + 0.001f)
                        .Where(f => query.SignedDistance(f.Positions[slot]) is <= 0f)
                        .Select(f => (float?)f.Time).LastOrDefault();
                    if (lastInside is { } inside)
                        yield return $"{role} in the area of {Natives.Data.ActionName(query.ActionId)} {at - inside:F2} s before it went off at {at:F2}";
                }
            }
        }
    }
}
