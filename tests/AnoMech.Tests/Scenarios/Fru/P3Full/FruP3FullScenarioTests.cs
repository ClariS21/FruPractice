using System.Globalization;
using AnoMech.Scenarios.Fru.P3Full;

namespace AnoMech.Tests;

public class FruP3FullScenarioTests
{
    private const float Tolerance = 0.3f;

    // Run times from the cactbot timeline, which lists ability resolutions.
    private static readonly (string Name, uint ActionId, float[] Times)[] KeyCasts =
    [
        ("Hell's Judgment", 40265, [8.3f]),
        ("Ultimate Relativity", 40266, [22.4f]),
        ("Shell Crusher", 40286, [70.1f]),
        ("Shockwave Pulsar", 40282, [78.5f, 148.2f]),
        ("Black Halo", 40290, [86.8f]),
        ("Spell-in-Waiting Refrain", 40269, [95.9f]),
        ("Apocalypse", 40296, [111.2f]),
        ("Darkest Dance", 40182, [136.0f]),
        ("Memory's End", 40300, [162.0f]),
    ];

    [Test]
    public void KeyCastsResolveOnTheTimelinesTimes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frup3full-{Guid.NewGuid():N}.csv");
        try
        {
            var run = ScenarioRun.Execute(typeof(FruP3FullScenario), 0, 1, new ScenarioRunOptions { TimelinePath = path });
            Assert.That(run.Passed, run.ToString);
            var abilities = File.ReadLines(path).Skip(1).Select(ParseRow)
                .Where(row => row.Kind == "ability")
                .ToLookup(row => row.ActionId, row => row.Time);
            foreach (var (name, actionId, times) in KeyCasts)
            {
                var actual = abilities[actionId].ToArray();
                Assert.That(actual, Has.Length.EqualTo(times.Length), $"{name} resolutions");
                for (var i = 0; i < times.Length; i++)
                    Assert.That(actual[i], Is.EqualTo(times[i]).Within(Tolerance), $"{name} #{i + 1}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Columns t, kind, actor, actor_name, id; no quoted commas before them.
    private static (float Time, string Kind, uint ActionId) ParseRow(string line)
    {
        var cells = line.Split(',');
        var parsed = uint.TryParse(cells[4], out var id);
        return (float.Parse(cells[0], CultureInfo.InvariantCulture), cells[1], parsed ? id : 0);
    }
}
