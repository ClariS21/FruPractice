using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Scenarios.Fru.P4Full;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5Full;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Tests;

// With every seat bot-driven, a run is a clean clear: the plans and raw values are survivable as in
// a real one, so Mitigation Practice never announces a hit that would have killed.
public class FruMitigationSurvivalTests
{
    private static IEnumerable<TestCaseData> Runs()
    {
        var types = new[]
        {
            typeof(FruP3UltimateRelativityScenario), typeof(FruP3ApocalypseScenario), typeof(FruP3FullScenario),
            typeof(FruP4DarklitDragonsongScenario), typeof(FruP4CrystallizeTimeScenario), typeof(FruP4FullScenario),
            typeof(FruP5FulgentBladeScenario), typeof(FruP5ParadiseRegainedScenario), typeof(FruP5FullScenario),
        };
        var catalog = ScenarioCatalog.Create().Scenarios;
        foreach (var type in types)
        {
            var scenario = catalog.Single(s => s.GetType() == type);
            for (var strat = 0; strat < scenario.AiStrats.Count; strat++)
                yield return new TestCaseData(type, strat).SetName($"{scenario.FullName()} [{scenario.AiStrats[strat].Name}]");
        }
    }

    [TestCaseSource(nameof(Runs))]
    public void NoHitWouldHaveKilled(Type scenarioType, int strat)
    {
        var seeds = TestContext.Parameters.Get("MitSeeds", 20);
        var warned = Enumerable.Range(0, seeds)
            .Select(seed => (Seed: seed, Run: ScenarioRun.Execute(scenarioType, strat, seed, new ScenarioRunOptions { UserActionsEnabled = true })))
            .Where(r => r.Run.WouldHaveKilled.Any())
            .ToList();
        Assert.That(warned, Is.Empty, () => $"{warned.Count}/{seeds} seeds warned:{Environment.NewLine}" + string.Join(Environment.NewLine,
            warned.Take(3).Select(r => $"seed {r.Seed}: {r.Run.WouldHaveKilled.Count()} warnings, first: {string.Join(" | ", r.Run.WouldHaveKilled.Take(2))}")));
    }
}
