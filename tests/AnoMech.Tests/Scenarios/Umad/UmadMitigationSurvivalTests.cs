using AnoMech.Scenarios;

namespace AnoMech.Tests;

// With every seat bot-driven, a run is a clean clear: the plans and raw values are survivable as in
// a real one, so Mitigation Practice never announces a hit that would have killed.
public class UmadMitigationSurvivalTests
{
    private static IEnumerable<TestCaseData> Runs()
    {
        foreach (var scenario in ScenarioCatalog.Create().Scenarios.Where(s => s.GetType().Namespace!.Contains(".Umad")))
            for (var strat = 0; strat < Math.Max(1, scenario.AiStrats.Count); strat++)
                yield return new TestCaseData(scenario.GetType(), strat)
                    .SetName($"{scenario.FullName()} [{(scenario.AiStrats.Count > 0 ? scenario.AiStrats[strat].Name : "no AI")}]");
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
