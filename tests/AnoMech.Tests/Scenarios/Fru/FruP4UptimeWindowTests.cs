using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Scenarios.Fru.P4Full;

namespace AnoMech.Tests;

// P4's uptime windows, on 50 seeds of each drill strat and 25 of each full-phase one (whose drills are the same): the runs
// clear with uptime on, the melee keep within reach of their target for at least 80% of the window time, and no uptime
// move goes anywhere a reported hazard goes off before the bot is due to move on.
public class FruP4UptimeWindowTests
{
    private const int DrillSeeds = 50;
    private const int FullPhaseSeeds = 25;
    private const float MeleeReach = 3.5f;
    private const float InReachShare = 0.8f;
    // How far ahead an uptime move's hazards are taken.
    private const float Horizon = 60f;
    private static readonly PartyRole[] Melee = [PartyRole.MeleeDpsA, PartyRole.MeleeDpsB];

    private static IEnumerable<TestCaseData> Strats()
        => from scenario in ScenarioCatalog.Create().Scenarios
           where scenario.Phase == FruZone.P4
           from strat in scenario.AiStrats.Select((ai, index) => (ai, index))
           select new TestCaseData(scenario.GetType(), strat.index).SetName($"{{m}}: {scenario.FullName()} [{strat.ai.Name}]");

    // Each window on the run's clock: a drill starts at 0, the full phase starts each drill at its own offset.
    private static List<(float From, float To, float MaxDistance)> Windows(Type scenario)
    {
        static IEnumerable<(float, float, float)> Shifted(IEnumerable<(float From, float To, float MaxDistance)> windows, float by)
            => windows.Select(w => (w.From + by, w.To + by, w.MaxDistance));
        if (scenario == typeof(FruP4DarklitDragonsongScenario)) return [.. FruP4DarklitDragonsongScenario.UptimeWindows];
        if (scenario == typeof(FruP4CrystallizeTimeScenario)) return [.. FruP4CrystallizeTimeScenario.UptimeWindows];
        return
        [
            .. FruP4FullScenario.UptimeWindows,
            .. Shifted(FruP4DarklitDragonsongScenario.UptimeWindows, FruP4FullScenario.DarklitDragonsongAt),
            .. Shifted(FruP4CrystallizeTimeScenario.UptimeWindows, FruP4FullScenario.CrystallizeTimeAt),
        ];
    }

    [TestCaseSource(nameof(Strats))]
    public void UptimeWindowsClearKeepMeleeInReachAndStayOutOfHazards(Type scenario, int strat)
    {
        var windows = Windows(scenario);
        var failures = new List<string>();
        var unsafeMoves = new List<string>();
        int moves = 0, meleeFrames = 0, inReach = 0;
        var seeds = scenario == typeof(FruP4FullScenario) ? FullPhaseSeeds : DrillSeeds;
        for (var seed = 0; seed < seeds; seed++)
        {
            Game? game = null;
            // Each uptime move with the hazards reported when it was made, until the strat next gives its bot a spot.
            var pending = new List<(UptimeMove Move, List<GuidanceHazard> Hazards)>();
            var done = new List<(UptimeMove Move, List<GuidanceHazard> Hazards, float Until)>();
            var trailSeen = 0;
            var run = ScenarioRun.Execute(scenario, strat, seed, new ScenarioRunOptions
            {
                WriteArtifactsOnFailure = false,
                Probe = p =>
                {
                    if (!ReferenceEquals(game, p.Game))
                    {
                        game = p.Game;
                        var source = p.Game.ActiveScenario as IGuidanceHazardSource;
                        p.World.Uptime.Issued += move =>
                        {
                            moves++;
                            pending.Add((move, source?.Upcoming(move.At, Horizon).ToList() ?? []));
                        };
                    }
                    var trail = p.World.Strat.Entries;
                    for (; trailSeen < trail.Count; trailSeen++)
                        foreach (var entry in pending.Where(m => m.Move.Role == trail[trailSeen].Role && trail[trailSeen].IssuedAt > m.Move.At).ToList())
                        {
                            pending.Remove(entry);
                            done.Add((entry.Move, entry.Hazards, trail[trailSeen].IssuedAt));
                        }
                    if (!windows.Any(w => p.Time >= w.From && p.Time <= w.To)) return;
                    var target = p.World.Party.Combat?.FindTarget();
                    foreach (var role in Melee)
                    {
                        if (role == p.World.Party.PlayerRole || p.Member(role) is not { } melee || !melee.IsAlive()) continue;
                        meleeFrames++;
                        if (target is not null && Flat(melee.Position, target.Position) <= target.HitboxRadius + MeleeReach) inReach++;
                    }
                },
            });
            if (!run.Passed) failures.Add(run.ToString().ReplaceLineEndings(" | "));
            foreach (var (move, hazards, until) in done.Concat(pending.Select(m => (m.Move, m.Hazards, Until: float.PositiveInfinity))))
                if (Unsafe(move, hazards, until) is { } why) unsafeMoves.Add($"seed {seed}: {why}");
        }
        var share = meleeFrames == 0 ? 0f : (float)inReach / meleeFrames;
        TestContext.Out.WriteLine($"{moves} uptime moves; melee within reach for {share:P1} of {meleeFrames} window frames");
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);
            Assert.That(unsafeMoves, Is.Empty);
            Assert.That(moves, Is.GreaterThan(0), "uptime never moved anyone");
            Assert.That(share, Is.GreaterThanOrEqualTo(InReachShare));
        });
    }

    // A move is unsafe when its destination or settle spot lies in a hazard that goes off while the bot can still be there:
    // before the strat next gives it a spot.
    private static string? Unsafe(UptimeMove move, List<GuidanceHazard> hazards, float until)
    {
        foreach (var hazard in hazards.Where(h => h.FiresAt >= move.At && h.FiresAt <= until))
            foreach (var (what, spot) in new[] { ("destination", move.Destination), ("settle spot", move.Settle) })
                if (hazard.Shape.SignedDistance(GuidanceLogic.Xz(spot)) <= 0f)
                    return $"{move.Role} at t={move.At:F2}: its {what} ({spot.X:F1},{spot.Z:F1}) is in {hazard.Label} going off at {hazard.FiresAt:F2}";
        return null;
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
