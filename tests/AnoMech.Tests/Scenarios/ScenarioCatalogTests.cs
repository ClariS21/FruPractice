using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class ScenarioCatalogTests
{
    [Test]
    public void TheCatalogHoldsFruAndUmadP3ToP5Entries()
    {
        var entries = ScenarioCatalog.Create().Scenarios
            .Select(s => (s.Phase.Zone.Name, s.Phase.Name, s.Name,
                string.Join(" | ", s.AiStrats.Select(ai => ai.Name))))
            .ToList();

        Assert.That(entries, Is.EqualTo(new[]
        {
            ("Futures Rewritten", "P3", "Ultimate Relativity", "NA"),
            ("Futures Rewritten", "P3", "Apocalypse", "NA priority / static spreads / OT bait"),
            ("Futures Rewritten", "P3", "Full phase", "NA · NA priority / static spreads / OT bait"),
            ("Futures Rewritten", "P4", "Darklit Dragonsong", "NA | NA · you take Somber Dance"),
            ("Futures Rewritten", "P4", "Crystallize Time", "NA priority / 7-1 Akh Morn (MT solo)"),
            ("Futures Rewritten", "P4", "Full phase", "NA · NA priority / 7-1 Akh Morn (MT solo) | NA · you take Somber Dance · NA priority / 7-1 Akh Morn (MT solo)"),
            ("Futures Rewritten", "P5", "Fulgent Blade", "Standard"),
            ("Futures Rewritten", "P5", "Paradise Regained", "H first / M1-R1 NW / M2-R2 NE; lines T-M-R-H"),
            ("Futures Rewritten", "P5", "Full phase", "Standard · H first / M1-R1 NW / M2-R2 NE; lines T-M-R-H"),
            ("Dancing Mad (Ultimate)", "P3", "Black Hole", "Black Hole: D>S>A | Black Hole: S>D>A"),
            ("Dancing Mad (Ultimate)", "P3", "Limit Cut", "Standard"),
            ("Dancing Mad (Ultimate)", "P3", "Full phase", "Standard · Black Hole: D>S>A | Standard · Black Hole: S>D>A"),
            ("Dancing Mad (Ultimate)", "P4", "Kefka Says", "Kefka Says, gazes: supports N / DPS S | Kefka Says"),
            ("Dancing Mad (Ultimate)", "P4", "Full phase", "Kefka Says, gazes: supports N / DPS S | Kefka Says"),
            ("Dancing Mad (Ultimate)", "P5", "Celestriad", "Standard"),
            ("Dancing Mad (Ultimate)", "P5", "Exaflares", "Spread"),
            ("Dancing Mad (Ultimate)", "P5", "Flood", "Rotate"),
            ("Dancing Mad (Ultimate)", "P5", "Forsaken Null", "Waymarks clockwise past the orange"),
            ("Dancing Mad (Ultimate)", "P5", "Full phase", "Role stacks, Omnitank · Rotate · Standard · Spread"),
        }));
    }

    [Test]
    public void OnlySupportedDrillsOfferAnEmptyPartyRun()
    {
        var solo = ScenarioCatalog.Create().Scenarios.Where(s => s.SupportsSolo).Select(s => s.Name);
        Assert.That(solo, Is.EqualTo(new[] { "Fulgent Blade", "Limit Cut", "Exaflares", "Flood" }));
    }

    private static IEnumerable<TestCaseData> ScenarioStrats()
        => from scenario in ScenarioCatalog.Create().Scenarios
           from strat in scenario.AiStrats.Select((ai, index) => (ai, index))
           select new TestCaseData(scenario.GetType(), strat.index)
               .SetName($"{scenario.GetType().Name}: {scenario.FullName()} [{strat.ai.Name}]");

    [TestCaseSource(nameof(ScenarioStrats))]
    public void SunnyDay(Type scenarioType, int strat)
    {
        var seeds = TestContext.Parameters.Get("SunnySeeds", 1);
        var failures = Enumerable.Range(0, seeds)
            .Select(_ => ScenarioRun.Execute(scenarioType, strat, Random.Shared.Next()))
            .Where(run => !run.Passed)
            .ToList();

        Assert.That(failures, Is.Empty, () => $"{failures.Count}/{seeds} seeds failed:{Environment.NewLine}" + string.Join(Environment.NewLine,
            failures.Select(run => $"{run}{Environment.NewLine}  replay: {run.ReplayTestCase}")));
    }

    // Paste the replay line of a failing seed here to debug it.
    [Explicit]
    [TestCase(typeof(global::AnoMech.Scenarios.Fru.P3UltimateRelativity.FruP3UltimateRelativityScenario), 0, 285776436)]
    [TestCase(typeof(global::AnoMech.Scenarios.Fru.P4CrystallizeTime.FruP4CrystallizeTimeScenario), 0, 1428128475)]
    [TestCase(typeof(global::AnoMech.Scenarios.Fru.P4Full.FruP4FullScenario), 0, 1492080477)]
    public void Replay(Type scenarioType, int strat, int seed)
    {
        var run = ScenarioRun.Execute(scenarioType, strat, seed, new ScenarioRunOptions { AlwaysWriteArtifacts = true });
        Assert.That(run.Passed, run.ToString);
    }

    // Replay driven by run parameters (Scenario = type full name, Strat, Seed, optional StopAt, Seat = a PartyRole and
    // LatePlayer); ScenarioRun.ReplayCommand prints the invocation.
    [Explicit]
    [Test]
    public void ReplayFromParameters()
    {
        var name = TestContext.Parameters.Get("Scenario");
        var scenarioType = name is null ? null : typeof(Game).Assembly.GetType(name);
        Assert.That(scenarioType, Is.Not.Null, $"Scenario parameter '{name}' is not a scenario type's full name.");
        var stopAt = TestContext.Parameters.Get("StopAt") is { } text ? float.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : (float?)null;
        var seat = TestContext.Parameters.Get("Seat") is { } role ? Enum.Parse<PartyRole>(role) : (PartyRole?)null;
        var run = ScenarioRun.Execute(scenarioType!, TestContext.Parameters.Get("Strat", 0), TestContext.Parameters.Get("Seed", 0),
            new ScenarioRunOptions { AlwaysWriteArtifacts = true, StopAt = stopAt, PlayerRole = seat, LatePlayer = TestContext.Parameters.Get("LatePlayer", false) });
        Assert.That(run.Passed, run.ToString);
    }
}
