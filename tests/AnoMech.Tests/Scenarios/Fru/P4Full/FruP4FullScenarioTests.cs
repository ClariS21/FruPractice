using System.Globalization;
using AnoMech.Scenarios.Fru.P4Full;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.PartyHp;
using AnoMech.Scenarios.Fru;

namespace AnoMech.Tests;

public class FruP4FullScenarioTests
{
    private const float Tolerance = 0.25f;

    [TestCase(JobId.Warrior, JobId.Paladin)]
    [TestCase(JobId.Gunbreaker, JobId.Paladin)]
    public void TankCompsSurviveP4WithoutMitigationDeathWarnings(JobId main, JobId off)
    {
        var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.CasterDps,
            JobOverrides = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off },
            Configure = c => c.MitigationPractice = true,
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(run.WouldHaveKilled, Is.Empty, run.ToString);
    }

    [Test]
    public void FinalMornAfahStacksEightAtCentreOnTenSeeds()
    {
        for (var seed = 1; seed <= 10; seed++)
        {
            var positions = new List<Vector3>();
            var path = Path.Combine(Path.GetTempPath(), $"frup4final-{Guid.NewGuid():N}.csv");
            try
            {
                var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, seed, new ScenarioRunOptions
                {
                    TimelinePath = path,
                    Probe = p =>
                    {
                        if (!p.Crossed(163.2f)) return;
                        positions.AddRange(Enum.GetValues<PartyRole>().Select(r => p.Member(r)!.Position));
                    },
                });
                Assert.That(run.Passed, run.ToString);
                Assert.That(positions, Has.Count.EqualTo(8), $"seed {seed}");
                Assert.That(positions.All(p => MathF.Sqrt(p.X * p.X + p.Z * p.Z) <= 1.5f), Is.True, $"seed {seed}");
                var ability = File.ReadLines(path).Skip(1).Select(line => line.Split(','))
                    .Single(c => c[1] == "ability" && c[4] == "40304" && float.Parse(c[0], CultureInfo.InvariantCulture) > 160f);
                Assert.That(ability[11], Does.StartWith("hits=8 "), $"seed {seed}: final Morn Afah targets");
            }
            finally { File.Delete(path); }
        }
    }

    // Run times from the cactbot timeline, which lists ability resolutions. A cast's first
    // resolutions are checked; a drill's own later ones (a second Spirit Taker) are not.
    private static readonly (string Name, uint ActionId, float[] Times)[] KeyCasts =
    [
        ("Materialization", 40246, [9.2f]),
        ("Akh Rhai", 40237, [22.9f]),
        ("Edge of Oblivion", 40174, [25.4f, 64.4f, 102.4f, 161.3f]),
        ("Darklit Dragonsong", 40239, [34.9f]),
        ("Spirit Taker", 40288, [49.1f]),
        ("Somber Dance (far)", 40284, [57.7f]),
        ("Somber Dance (close)", 40285, [61.0f]),
        ("Akh Morn", 40302, [70.7f, 153.3f]),
        ("Morn Afah", 40304, [80.8f, 163.3f]),
        ("Crystallize Time", 40240, [96.3f]),
        ("Memory's End", 40305, [176.5f]),
        ("Absolute Zero", 40245, [176.5f]),
    ];

    [Test]
    public void KeyCastsResolveOnTheTimelinesTimes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frup4full-{Guid.NewGuid():N}.csv");
        try
        {
            var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, 1, new ScenarioRunOptions { TimelinePath = path });
            Assert.That(run.Passed, run.ToString);
            var abilities = File.ReadLines(path).Skip(1).Select(ParseRow)
                .Where(row => row.Kind == "ability")
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

    // Real clears (FFLogs): the Morn Afah bar is 5.7 s and its damage lands 7.5 s after it begins; the last Edge of
    // Oblivion's bar is 4.7 s and its damage lands 5.6 s after it begins; the two damages are 3.0-3.3 s apart. The sim
    // lands a damage its DamageDelay after the cast snapshots, and each target sees it after its own server tick, a random
    // 0.05-0.35 s. So the times checked are the scheduled ones, and each impact only has to land within its tick.
    [Test]
    public void TheEdgeAndMornAfahKeepTheirRealBarsAndDamageTimes()
    {
        const float frame = ScenarioRun.FrameSeconds;
        var path = Path.Combine(Path.GetTempPath(), $"frup4full-{Guid.NewGuid():N}.csv");
        try
        {
            var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, 1, new ScenarioRunOptions { TimelinePath = path });
            Assert.That(run.Passed, run.ToString);
            var lines = File.ReadLines(path).Skip(1).ToList();
            List<float> Begins(uint id) => DistinctMoments(lines.Select(ParseRow).Where(r => r.Kind == "cast" && r.ActionId == id).Select(r => r.Time));
            float FirstImpactAfter(uint id, float time) => lines.Select(ParseRow).Where(r => r.Kind == "impact" && r.ActionId == id && r.Time > time && r.Time < time + 10f).Min(r => r.Time);
            float BarOf(uint id, float begin)
            {
                var cast = lines.First(line => ParseRow(line) is { Kind: "cast" } r && r.ActionId == id && MathF.Abs(r.Time - begin) < 0.01f);
                var text = cast.Split(',').First(cell => cell.StartsWith("cast=", StringComparison.Ordinal));
                return float.Parse(text["cast=".Length..].Split('s')[0], CultureInfo.InvariantCulture);
            }
            float Damage(EnemyAction action, float begin) => begin + BarOf(action.ActionId, begin) + action.Timing.ResolveSnapshotOffset + action.Timing.DamageDelay;
            void LandsWithinItsTick(EnemyAction action, float damage, string what)
                => Assert.That(FirstImpactAfter(action.ActionId, damage - 1f),
                    Is.InRange(damage + PartyHp.TickDelayMin - frame, damage + PartyHp.TickDelayMax + 3 * frame), $"{what}: the impact");

            var mornAfahs = Begins(FruActions.MornAfah.ActionId);
            Assert.That(mornAfahs, Has.Count.EqualTo(2));
            foreach (var begin in mornAfahs)
            {
                Assert.That(BarOf(FruActions.MornAfah.ActionId, begin), Is.EqualTo(5.7f).Within(0.05f), $"Morn Afah at {begin:F1}: the bar");
                var damage = Damage(FruActions.MornAfah, begin);
                Assert.That(damage - begin, Is.EqualTo(7.5f).Within(0.3f), $"Morn Afah at {begin:F1}: the damage");
                LandsWithinItsTick(FruActions.MornAfah, damage, $"Morn Afah at {begin:F1}");
            }
            var edgeBegin = Begins(FruActions.EdgeOfOblivion.ActionId).Last();
            Assert.That(BarOf(FruActions.EdgeOfOblivion.ActionId, edgeBegin), Is.EqualTo(4.7f).Within(0.05f), "Edge of Oblivion's bar");
            var edgeDamage = Damage(FruActions.EdgeOfOblivion, edgeBegin);
            Assert.That(edgeDamage - edgeBegin, Is.EqualTo(5.6f).Within(0.3f), "Edge of Oblivion's damage");
            LandsWithinItsTick(FruActions.EdgeOfOblivion, edgeDamage, "Edge of Oblivion");
            Assert.That(Damage(FruActions.MornAfah, mornAfahs[1]) - edgeDamage, Is.InRange(3.0f, 3.3f), "Edge to Morn Afah");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A cast and its animation helper resolve together.
    private static List<float> DistinctMoments(IEnumerable<float> times)
    {
        var moments = new List<float>();
        foreach (var time in times.Order())
            if (moments.Count == 0 || time - moments[^1] > 0.1f) moments.Add(time);
        return moments;
    }

    // Columns t, kind, actor, actor_name, id; no quoted commas before them.
    private static (float Time, string Kind, uint ActionId) ParseRow(string line)
    {
        var cells = line.Split(',');
        var parsed = uint.TryParse(cells[4], out var id);
        return (float.Parse(cells[0], CultureInfo.InvariantCulture), cells[1], parsed ? id : 0);
    }
}
