using System.Globalization;
using AnoMech.Scenarios.Fru.P5Full;

namespace AnoMech.Tests;

public class FruP5FullScenarioTests
{
    private const float Tolerance = 0.25f;

    // Run times from the cactbot timeline, which lists ability resolutions. Only a cast's first
    // resolutions are checked. The second Paradise Regained is pinned by its Polarizing Strikes,
    // so its own cast is 5.4s ahead of the timeline's 164.0.
    private static readonly (string Name, uint ActionId, float[] Times)[] KeyCasts =
    [
        ("Fulgent Blade", 40306, [11.8f, 124.8f, 215.3f]),
        ("Akh Morn", 40310, [38.8f, 151.7f, 242.3f]),
        ("Paradise Regained", 40319, [47.0f, 158.6f]),
        ("Polarizing Strikes", 40316, [78.4f, 190.2f]),
        ("Polarizing Paths", 40234, [83.0f, 87.6f, 92.2f, 194.8f, 199.4f, 204.0f]),
        ("Pandora's Box", 40326, [112.7f]),
        ("Paradise Lost", 40328, [272.3f]),
    ];

    [Test]
    public void KeyCastsResolveOnTheTimelinesTimes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frup5full-{Guid.NewGuid():N}.csv");
        try
        {
            var run = ScenarioRun.Execute(typeof(FruP5FullScenario), 0, 1, new ScenarioRunOptions { TimelinePath = path });
            Assert.That(run.Passed, run.ToString);
            var abilities = File.ReadLines(path).Skip(1).Select(ParseRow)
                .Where(row => row.Kind == "ability" && row.Actor == "Pandora")
                .ToLookup(row => row.ActionId, row => row.Time);
            foreach (var (name, actionId, times) in KeyCasts)
            {
                var actual = DistinctMoments(abilities[actionId]);
                Assert.That(actual, Has.Count.AtLeast(times.Length), $"{name} resolutions");
                for (var i = 0; i < times.Length; i++)
                    Assert.That(actual[i], Is.EqualTo(times[i]).Within(Tolerance), $"{name} #{i + 1}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A cast and its helpers resolve together.
    private static List<float> DistinctMoments(IEnumerable<float> times)
    {
        var moments = new List<float>();
        foreach (var time in times.Order())
            if (moments.Count == 0 || time - moments[^1] > 0.1f) moments.Add(time);
        return moments;
    }

    // Columns t, kind, actor, actor_name, id; no quoted commas before them.
    private static (float Time, string Kind, string Actor, uint ActionId) ParseRow(string line)
    {
        var cells = line.Split(',');
        var parsed = uint.TryParse(cells[4], out var id);
        return (float.Parse(cells[0], CultureInfo.InvariantCulture), cells[1], cells[3], parsed ? id : 0);
    }
}
