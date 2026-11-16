using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Scenarios.Fru.P4Full;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5Full;

namespace AnoMech.Tests;

public class BurstAnchorTests
{
    // Each declared anchor is where the boss really starts that cast.
    [TestCase(typeof(FruP3UltimateRelativityScenario))]
    [TestCase(typeof(FruP3ApocalypseScenario))]
    [TestCase(typeof(FruP3FullScenario))]
    [TestCase(typeof(FruP4DarklitDragonsongScenario))]
    [TestCase(typeof(FruP4CrystallizeTimeScenario))]
    [TestCase(typeof(FruP4FullScenario))]
    [TestCase(typeof(FruP5FulgentBladeScenario))]
    [TestCase(typeof(FruP5FullScenario))]
    public void DeclaredAnchorsMatchTheBossCasts(Type scenarioType)
    {
        IReadOnlyList<BurstAnchor> anchors = [];
        SimTimeline? timeline = null;
        ScenarioRun.Execute(scenarioType, 0, 1, new ScenarioRunOptions
        {
            StopAt = scenarioType == typeof(FruP5FullScenario) ? 215f : 160f,
            Probe = p =>
            {
                timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline;
                if (anchors.Count == 0) anchors = p.Game.Catalog.Scenarios.Single(s => s.GetType() == scenarioType).BurstAnchors;
            },
        });
        Assert.That(anchors, Is.Not.Empty);
        foreach (var group in anchors.GroupBy(a => a.ActionId))
        {
            // A cast with a bar begins at its cast row; an instant action only shows as its effect.
            var rows = timeline!.Rows.Where(r => r["id"] == group.Key.ToString()).ToList();
            var starts = rows.Where(r => r["kind"] == (rows.Any(x => x["kind"] == "cast") ? "cast" : "ability"))
                .Select(r => float.Parse(r["t"], System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var declared = group.Select(a => a.Time).OrderBy(t => t).ToList();
            for (var i = 0; i < declared.Count; i++)
            {
                Assert.That(starts, Has.Count.GreaterThan(i), $"{group.Key} #{i + 1} was never cast");
                Assert.That(starts[i], Is.EqualTo(declared[i]).Within(0.1f), $"{group.Key} #{i + 1} (casts at {string.Join(", ", starts)})");
            }
        }
    }
}
