using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru.P5FulgentBlade;

namespace AnoMech.Tests;

public class FruP5FulgentBladeAiTests
{
    [Test]
    public void BotsGatherAndLeaveTheFirstStackAtDifferentTimes()
    {
        var departures = new Dictionary<PartyRole, float>();
        var firstStack = new Dictionary<PartyRole, Vector3>();
        var largestHoldingSpread = 0f;
        var largestMovingSpread = 0f;
        var run = ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 22.55f,
            Probe = probe =>
            {
                var positions = Enum.GetValues<PartyRole>()
                    .Select(role => (Role: role, Position: probe.Member(role)?.Position))
                    .Where(item => item.Position.HasValue)
                    .Select(item => (item.Role, Position: item.Position!.Value))
                    .ToArray();
                if (positions.Length != 8) return;
                var center = positions.Aggregate(Vector3.Zero, (sum, item) => sum + item.Position) / positions.Length;
                var spread = positions.Average(item => Vector3.Distance(item.Position, center));
                if (probe.Time is >= 18f and < 20.75f) largestHoldingSpread = MathF.Max(largestHoldingSpread, (float)spread);
                if (probe.Time is >= 20.8f and < 22.5f) largestMovingSpread = MathF.Max(largestMovingSpread, (float)spread);
                if (probe.Crossed(20.75f))
                    foreach (var (role, position) in positions) firstStack[role] = position;
                if (probe.Time < 20.8f) return;
                foreach (var (role, position) in positions)
                    if (!departures.ContainsKey(role) && firstStack.TryGetValue(role, out var start)
                        && Vector3.Distance(position, start) > 0.08f)
                        departures[role] = probe.Time;
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(largestHoldingSpread, Is.LessThanOrEqualTo(1.2f));
        Assert.That(largestMovingSpread, Is.LessThanOrEqualTo(3f));
        Assert.That(departures, Has.Count.EqualTo(8));
        // Still visibly staggered, but every bot also arrives at least 0.8 s early, which narrows the spread.
        Assert.That(departures.Values.Max() - departures.Values.Min(), Is.GreaterThanOrEqualTo(0.35f));
    }

    [Test]
    public void TwentyDrillSeedsClearTheExalines()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var run = ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, seed,
                new ScenarioRunOptions { StopAt = 31.9f, WriteArtifactsOnFailure = false });
            Assert.That(run.Passed, Is.True, run.ToString());
        }
    }
}
