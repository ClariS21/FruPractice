using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru.P3Full;

namespace AnoMech.Tests;

public class FruP3FullBlackHaloTests
{
    private const float BeforeHit = 86.0f;
    private const float AfterHit = 90f;

    // The main tank's kitchen sink and the off tank's buddy mitigation leave it standing, as in a clear.
    [Test]
    public void TheMainTankLivesBlackHaloOnItsCooldowns()
    {
        var losses = new List<float>();
        for (var seed = 0; seed < 20; seed++)
        {
            float before = 1f, lowest = 1f;
            ScenarioRun.Execute(typeof(FruP3FullScenario), 0, seed, new ScenarioRunOptions
            {
                StopAt = AfterHit,
                Probe = p =>
                {
                    if (p.Member(PartyRole.MainTank)?.Proxy is not { Exists: true } tank) return;
                    var share = (float)tank.Health / tank.MaxHealth;
                    if (p.Time < BeforeHit) before = share;
                    else lowest = MathF.Min(lowest, share);
                },
            });
            losses.Add(before - lowest);
        }
        TestContext.Out.WriteLine($"BHLOSS main tank HP loss over 20 seeds: min {losses.Min():P0}, median {losses.Order().ElementAt(10):P0}, max {losses.Max():P0}");
        Assert.That(losses.Max(), Is.LessThan(1f));
    }

    [Test]
    public void BlackHaloHitsOnlyTheMainTankAndNobodyWouldHaveDied()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bh-{Guid.NewGuid():N}.csv");
            try
            {
                var run = ScenarioRun.Execute(typeof(FruP3FullScenario), 0, seed, new ScenarioRunOptions { TimelinePath = path, UserActionsEnabled = true });
                var hits = File.ReadLines(path).Skip(1).Select(l => l.Split(','))
                    .Where(c => c[1] == "ability" && c[4] == "40290" && c[11].StartsWith("hits=")).Select(c => c[11]).ToList();
                Assert.That(hits, Has.Count.EqualTo(1), $"seed {seed}");
                Assert.That(hits[0], Does.StartWith("hits=1 "), $"seed {seed}: {hits[0]}");
                Assert.That(run.WouldHaveKilled, Is.Empty, $"seed {seed}");
            }
            finally { File.Delete(path); }
        }
    }
}
