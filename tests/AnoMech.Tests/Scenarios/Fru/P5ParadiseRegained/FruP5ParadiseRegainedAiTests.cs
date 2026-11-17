using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Tests;

public class FruP5ParadiseRegainedAiTests
{
    [Test]
    public void BotsLeaveTheFirstStackForTheTowersAtDifferentTimes()
    {
        var start = new Dictionary<PartyRole, Vector3>();
        var departures = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(FruP5ParadiseRegainedScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 15f,
            Probe = probe =>
            {
                if (probe.Crossed(8.29f))
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (probe.Member(role)?.Position is { } p) start[role] = p;
                if (probe.Time < 8.3f) return;
                foreach (var (role, from) in start)
                    if (!departures.ContainsKey(role) && probe.Member(role)?.Position is { } now && Vector3.Distance(now, from) > 0.08f)
                        departures[role] = probe.Time;
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(departures.Count, Is.GreaterThanOrEqualTo(6));
        Assert.That(departures.Values.Max() - departures.Values.Min(), Is.GreaterThanOrEqualTo(0.35f));
    }

    [Test]
    public void BotsLeaveTheLinesPrepositionAtDifferentTimes()
    {
        var start = new Dictionary<PartyRole, Vector3>();
        var departures = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(FruP5ParadiseRegainedScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 30f,
            Probe = probe =>
            {
                if (probe.Crossed(23.34f))
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (probe.Member(role)?.Position is { } p) start[role] = p;
                if (probe.Time < 23.35f) return;
                foreach (var (role, from) in start)
                    if (!departures.ContainsKey(role) && probe.Member(role)?.Position is { } now && Vector3.Distance(now, from) > 0.08f)
                        departures[role] = probe.Time;
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(departures.Count, Is.GreaterThanOrEqualTo(6));
        Assert.That(departures.Values.Max() - departures.Values.Min(), Is.GreaterThanOrEqualTo(0.35f));
    }

    [Test]
    public void TwentySeedsTakeNoTowerOrWingHitThatIsntTheirs()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var run = ScenarioRun.Execute(typeof(FruP5ParadiseRegainedScenario), 0, seed,
                new ScenarioRunOptions { WriteArtifactsOnFailure = false, PlayerRole = PartyRole.CasterDps, Configure = c => c.MitigationPractice = true });
            Assert.That(run.Passed, Is.True, run.ToString());
            Assert.That(run.WouldHaveKilled, Is.Empty, $"seed {seed}");
        }
    }
}
