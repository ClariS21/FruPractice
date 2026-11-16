using AnoMech.Scenarios.Fru.P3Full;

namespace AnoMech.Tests;

public class FruP3FullHealingTests
{
    // The party's average HP in the frame before each raidwide lands, on the full phase with Mitigation Practice.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ThePartyIsMostlyToppedUpBeforeEachRaidwide(int seed)
    {
        var before = new List<(float At, float Average)>();
        var lastId = 0;
        var lastAverage = 1f;
        var run = ScenarioRun.Execute(typeof(FruP3FullScenario), 0, seed, new ScenarioRunOptions
        {
            UserActionsEnabled = true,
            Probe = p =>
            {
                var healing = p.World.Healing;
                if (healing.RaidwideId != lastId)
                {
                    lastId = healing.RaidwideId;
                    before.Add((p.Time, lastAverage));
                }
                lastAverage = healing.Average();
            },
        });
        TestContext.Out.WriteLine($"PRE seed {seed}: " + string.Join(", ", before.Select(b => $"{b.At:F0}s {b.Average:P0}")));
        Assert.That(run.WouldHaveKilled, Is.Empty);
        Assert.That(before.Count, Is.GreaterThan(5));
        Assert.That(before.Skip(1).Average(b => b.Average), Is.GreaterThan(0.85f), "most raidwides land on a healed party");
    }
}
