using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;

namespace AnoMech.Tests;

// The human's stand-in reaches every spot with a deadline at the last safe moment (ScenarioRunOptions.LatePlayer) while the bots
// keep their own timing, from every seat: no bot may take, use up or stand in the human's tower, puddle, share, tether or spot
// before the human gets there.
public class ScenarioLatePlayerTests
{
    private static IEnumerable<TestCaseData> Drills() => Entries(drillsOnly: true);

    private static IEnumerable<TestCaseData> Everything() => Entries(drillsOnly: false);

    private static IEnumerable<TestCaseData> Entries(bool drillsOnly)
        => from scenario in ScenarioCatalog.Create().Scenarios
           where !drillsOnly || (scenario.Phase.Zone is FruZone && scenario.Name != "Full phase")
           from strat in Enumerable.Range(0, scenario.AiStrats.Count)
           select new TestCaseData(scenario.GetType(), strat).SetName($"{{m}}({scenario.GetType().Name}, {strat})");

    // Every FRU drill hands the human something of their own; the Full phases play the same drills back to back. DMU is left
    // to the Audit.
    [TestCaseSource(nameof(Drills))]
    public void EverySeatKeepsItsPartWhenTheHumanComesLate(Type scenarioType, int strat)
        => AssertAllPassed(Sweep(scenarioType, strat, firstSeed: 1, seeds: 1));

    // The full audit, one scenario per process (LateScenario matches the scenario type's name), e.g. from Bash:
    //   dotnet test tests/AnoMech.Tests --configuration Release --no-build --filter "FullyQualifiedName~ScenarioLatePlayerTests.Audit"
    //     -- 'TestRunParameters.Parameter(name="LateScenario", value="FruP4DarklitDragonsongScenario")'
    //        'TestRunParameters.Parameter(name="LateSeeds", value="16")'
    // LateFirstSeed moves the seed range. A failure's replay line writes its artifacts.
    [Explicit]
    [TestCaseSource(nameof(Everything))]
    public void Audit(Type scenarioType, int strat)
    {
        var only = TestContext.Parameters.Get("LateScenario", "");
        if (only.Length > 0 && !scenarioType.Name.Contains(only, StringComparison.Ordinal))
            Assert.Ignore($"LateScenario is {only}.");
        AssertAllPassed(Sweep(scenarioType, strat, TestContext.Parameters.Get("LateFirstSeed", 1), TestContext.Parameters.Get("LateSeeds", 16)));
    }

    private static List<string> Sweep(Type scenarioType, int strat, int firstSeed, int seeds)
    {
        var scenario = ScenarioCatalog.Create().Scenarios.Single(s => s.GetType() == scenarioType);
        var failures = new List<string>();
        foreach (var seat in Enum.GetValues<PartyRole>())
            for (var seed = firstSeed; seed < firstSeed + seeds; seed++)
            {
                var run = ScenarioRun.Execute(scenarioType, strat, seed, new ScenarioRunOptions
                {
                    PlayerRole = seat,
                    LatePlayer = true,
                    WriteArtifactsOnFailure = false,
                });
                if (!run.Passed)
                    failures.Add($"{scenario.FullName()} [{scenario.AiStrats[strat].Name}], strat {strat}, seat {seat}, seed {seed}: {run}"
                                 + $"{Environment.NewLine}  replay: {run.ReplayCommand} {ScenarioRun.ReplayParameter("Seat", seat.ToString())} "
                                 + ScenarioRun.ReplayParameter("LatePlayer", "true"));
            }
        TestContext.Out.WriteLine($"{scenario.FullName()} [{scenario.AiStrats[strat].Name}]: {failures.Count} of {8 * seeds} late runs failed");
        return failures;
    }

    private static void AssertAllPassed(List<string> failures)
        => Assert.That(failures, Is.Empty, () => string.Join(Environment.NewLine, failures));
}
