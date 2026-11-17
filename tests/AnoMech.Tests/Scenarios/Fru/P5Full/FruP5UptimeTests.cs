using System.Numerics;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Tests;

// P5's uptime windows: bots use them without dying or failing a mechanic, melee stay on the boss, and
// no uptime move ends where a hazard fires.
public class FruP5UptimeTests
{
    private const int Seeds = 50;
    private const float MeleeReach = 3.5f;

    private static IEnumerable<TestCaseData> Drills() =>
    [
        new TestCaseData(typeof(FruP5FulgentBladeScenario), new[] { (1f, 8.6f) }).SetName("Fulgent Blade"),
        new TestCaseData(typeof(FruP5ParadiseRegainedScenario), new[] { (FruP5ParadiseRegainedState.TowerHitAt(2) + 0.65f, 28f) }).SetName("Paradise Regained"),
    ];

    [TestCaseSource(nameof(Drills))]
    public void UptimeWindowsKeepBotsAliveOnTheBossAndOutOfHazards(Type scenario, (float From, float To)[] windows)
    {
        var failures = new List<string>();
        var inHazard = new List<string>();
        var moves = 0;
        float inReach = 0, measured = 0;
        for (var seed = 0; seed < Seeds; seed++)
        {
            var issued = new List<UptimeMove>();
            var hazards = new HashSet<GuidanceHazard>();
            var wired = false;
            var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                WriteArtifactsOnFailure = false,
                Probe = p =>
                {
                    if (!wired) { wired = true; p.World.Uptime.Issued += issued.Add; }
                    foreach (var h in ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(p.Time, 60f)) hazards.Add(h);
                    if (!windows.Any(w => p.Time >= w.From && p.Time <= w.To) || p.World.Party.Combat?.FindTarget() is not { } boss) return;
                    foreach (var role in new[] { PartyRole.MeleeDpsA, PartyRole.MeleeDpsB })
                    {
                        if (role == p.World.Party.PlayerRole || p.World.Party.Get(role) is not { } melee || !melee.IsAlive()) continue;
                        measured++;
                        if (Vector2.Distance(new Vector2(melee.Position.X, melee.Position.Z), new Vector2(boss.Position.X, boss.Position.Z)) <= boss.HitboxRadius + MeleeReach) inReach++;
                    }
                },
            });
            if (!run.Passed) failures.Add(run.ToString());
            moves += issued.Count;
            foreach (var move in issued)
                foreach (var hazard in hazards)
                {
                    if (hazard.FiresAt < move.At || hazard.FiresAt > (move.NextStrat ?? float.MaxValue)) continue;
                    foreach (var spot in new[] { move.Destination, move.Settle })
                        if (hazard.Shape.SignedDistance(new Vector2(spot.X, spot.Z)) <= 0f)
                            inHazard.Add($"seed {seed}: {move.Role} to ({spot.X:F1},{spot.Z:F1}) at {move.At:F1}, {hazard.Label} fires {hazard.FiresAt:F2}");
                }
        }
        TestContext.Out.WriteLine($"{scenario.Name}: {moves} uptime moves over {Seeds} seeds; melee in reach {inReach / MathF.Max(1f, measured):P0} of {measured} frames");
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);
            Assert.That(inHazard, Is.Empty);
            if (scenario == typeof(FruP5FulgentBladeScenario)) Assert.That(moves, Is.GreaterThan(0), "uptime never ran");
            if (measured > 0) Assert.That(inReach / measured, Is.GreaterThanOrEqualTo(0.8f));
        });
    }
}
