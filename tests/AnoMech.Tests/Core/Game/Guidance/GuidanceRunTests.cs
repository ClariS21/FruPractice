using System.Reflection;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P3Apocalypse;

namespace AnoMech.Tests;

public class GuidanceRunTests
{
    private static IEnumerable<Type> HazardScenarios()
        => ScenarioCatalog.Create().Scenarios.OfType<IGuidanceHazardSource>().Select(s => s.GetType());

    [TestCaseSource(nameof(HazardScenarios))]
    public void GuidanceHazardSetStaysBounded(Type scenario)
    {
        HashSet<GuidanceHazard>? retained = null;
        var run = ScenarioRun.Execute(scenario, 0, 1, new ScenarioRunOptions
        {
            Configure = c => c.PathGuidance = PathGuidance.FullGuide,
            WriteArtifactsOnFailure = false,
            Probe = p => retained ??= (HashSet<GuidanceHazard>)typeof(GuidanceRun)
                .GetField("hazards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(p.Game.Guidance)!,
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(retained, Is.Not.Null);
        TestContext.Progress.WriteLine($"{scenario.Name}: {retained!.Count} retained hazards");
        var topLabels = string.Join(", ", retained.GroupBy(h => h.Label).OrderByDescending(g => g.Count())
            .Take(8).Select(g => $"{g.Key}={g.Count()}"));
        Assert.That(retained.Count, Is.LessThanOrEqualTo(400), topLabels);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeathOrClearRetainsDebriefUntilClosed(bool death)
    {
        GuidanceRun? guidance = null;
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.CasterDps, StopAt = death ? 34f : null,
            Configure = c => c.PathGuidance = PathGuidance.FullGuide,
            WriteArtifactsOnFailure = false,
            Probe = p =>
            {
                guidance = p.Game.Guidance;
                if (death && p.Crossed(33.4f)) p.Game.Kill(p.World.Party.Player!, "test death");
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(guidance!.Debrief, Is.Not.Null);
        var result = guidance.Debrief!;
        Assert.That(result.Path.Count, Is.GreaterThan(300));
        Assert.That(result.Spots, Is.Not.Empty);
        Assert.That(result.Hazards, Is.Not.Empty);
        Assert.That(guidance.Recording, Is.False);
        guidance.Close();
        Assert.That(guidance.Debrief, Is.Null);
    }

    [Test]
    public void RestartClearsDebriefAndRecordedPath()
    {
        var restarted = false;
        var checkedRestart = false;
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 3, Configure = c => c.PathGuidance = PathGuidance.FullGuide,
            Probe = p =>
            {
                if (restarted && p.Time < 0.5f)
                {
                    Assert.That(p.Game.Guidance.Debrief, Is.Null);
                    Assert.That(p.Game.Guidance.Recorder.Samples.Count, Is.LessThan(7));
                    checkedRestart = true;
                }
                if (restarted || p.Time < 1) return;
                restarted = true;
                p.Game.Kill(p.World.Party.Player!, "test death");
                Assert.That(p.Game.Guidance.Debrief, Is.Not.Null);
                p.Game.StartScenario(new RunScenarioParams(p.Game.ActiveScenario!, new LocalPartyProvider { RoleOverride = PartyRole.CasterDps }, 0, 0, 1));
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(restarted, Is.True);
        Assert.That(checkedRestart, Is.True);
    }
}
