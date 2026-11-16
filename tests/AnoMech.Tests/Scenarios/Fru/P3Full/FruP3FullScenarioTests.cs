using AnoMech.Core.Native.Interfaces;
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

public class FruP3FullHourglassCleanupTests
{
    private const uint HourglassBaseId = 17832;
    private const int FirstHourglassSlot = 26;
    private const int HourglassHideState = 4;

    // After Relativity the hourglasses must be gone: their actors despawned and their scenery hidden.
    [Test]
    public void HourglassesAreGoneAfterRelativityResolves()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frup3full-hg-{Guid.NewGuid():N}.csv");
        try
        {
            var loaded = false;
            var run = ScenarioRun.Execute(typeof(FruP3FullScenario), 0, 1, new ScenarioRunOptions
            {
                TimelinePath = path,
                StopAt = 80f,
                Probe = _ =>
                {
                    if (loaded) return;
                    loaded = true;
                    Natives.MapEffects.Loaded = true;
                },
            });
            Assert.That(run.Passed, run.ToString);
            var rows = File.ReadLines(path).Skip(1).Select(l => l.Split(',')).ToList();
            var time = (string[] r) => float.Parse(r[0], CultureInfo.InvariantCulture);
            var hourglasses = rows.Where(r => r[1] == "spawn" && r[4] == HourglassBaseId.ToString()).Select(r => r[2]).ToList();
            Assert.That(hourglasses, Has.Count.EqualTo(8));
            foreach (var id in hourglasses)
            {
                var despawn = rows.Where(r => r[1] == "despawn" && r[2] == id).ToList();
                Assert.That(despawn, Has.Count.EqualTo(1), $"hourglass {id} despawned");
                Assert.That(time(despawn[0]), Is.LessThan(74f));
            }
            for (var slot = FirstHourglassSlot; slot < FirstHourglassSlot + 8; slot++)
            {
                var hide = rows.Where(r => r[1] == "map_effect" && r[4] == slot.ToString() && r[11].Contains($"state={HourglassHideState} ")).ToList();
                Assert.That(hide, Has.Count.EqualTo(1), $"slot {slot} hidden");
                Assert.That(time(hide[0]), Is.LessThan(74f));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
