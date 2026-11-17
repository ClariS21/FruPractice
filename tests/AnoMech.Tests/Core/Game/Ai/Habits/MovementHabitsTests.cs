using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class MovementHabitsTests
{
    private static string ShippedPath => MovementHabits.PathIn(TestContext.CurrentContext.TestDirectory);

    [Test]
    public void TheShippedPopulationLoads()
    {
        var habits = MovementHabits.Load(ShippedPath);
        Assert.That(habits, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(habits!.Profiles, Has.Count.EqualTo(480));
            Assert.That(habits.RoleDefaults.Keys, Is.EquivalentTo(Enum.GetValues<HabitRole>()));
            Assert.That(habits.GroupWeights[HabitGroup.Speed], Is.EqualTo(0.667f).Within(0.001f));
            Assert.That(habits.GroupWeights[HabitGroup.Decent], Is.EqualTo(0.333f).Within(0.001f));
            foreach (var role in Enum.GetValues<HabitRole>())
            foreach (var group in Enum.GetValues<HabitGroup>())
                Assert.That(habits.Profiles.Any(p => p.Role == role && p.Group == group), $"no {group} {role} profile");
        });
    }

    [TestCase("{\"schemaVersion\":2,\"groupWeights\":{\"speed\":1},\"roleDefaults\":{},\"profiles\":[{\"id\":\"p1\",\"role\":\"tank\",\"group\":\"speed\"}]}")]
    [TestCase("{\"schemaVersion\":1,\"groupWeights\":{\"speed\":1},\"roleDefaults\":{},\"profiles\":[]}")]
    [TestCase("{\"schemaVersion\":1,\"groupWeights\":{\"speed\":1},\"roleDefaults\":{},\"profiles\":[{\"id\":\"p1\",\"role\":\"bard\",\"group\":\"speed\"}]}")]
    [TestCase("{\"schemaVersion\":1,\"groupWeights\":{\"speed\":1},\"roleDefaults\":{},\"profiles\":[{\"id\":\"p1\",\"role\":\"tank\",\"group\":\"speed\",\"settle\":[-1,2]}]}")]
    [TestCase("{\"schemaVersion\":1,")]
    public void AnInvalidFileGivesNull(string json)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bad-habits-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            Assert.That(MovementHabits.Load(path), Is.Null);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void AMissingFileGivesNull()
        => Assert.That(MovementHabits.Load(ShippedPath + ".missing"), Is.Null);

    [Test]
    public void ANullMetricFallsBackToTheRoleDefaultThenTheBuiltIn()
    {
        var profile = new HabitProfile("test", HabitRole.Tank, HabitGroup.Speed, new HabitMetrics { Bulge = new Quantiles([0.3f]) });
        var habits = new MovementHabits([profile], new Dictionary<HabitRole, HabitMetrics>
        {
            [HabitRole.Tank] = new() { Settle = new Quantiles([0.2f]), Bulge = new Quantiles([0.9f]) }
        }, new Dictionary<HabitGroup, float> { [HabitGroup.Speed] = 1f });
        var resolved = habits.Resolve(profile);
        var builtIn = MovementHabits.Default.Resolve(profile);
        Assert.Multiple(() =>
        {
            Assert.That(resolved.Bulge!.At(0.5f), Is.EqualTo(0.3f), "the profile's own value wins");
            Assert.That(resolved.Settle!.At(0.5f), Is.EqualTo(0.2f), "then the role default");
            Assert.That(resolved.Cast!.At(0.37f), Is.EqualTo(builtIn.Cast!.At(0.37f)), "then the built-in one");
        });
    }

    [Test]
    public void TheShippedFilesNullMetricsResolveToTheirRoleDefault()
    {
        var habits = MovementHabits.Load(ShippedPath)!;
        var profile = habits.Profiles.First(p => p.Metrics.Overshoot is null);
        Assert.That(habits.Resolve(profile).Overshoot, Is.SameAs(habits.RoleDefaults[profile.Role].Overshoot));
    }

    [Test]
    public void QuantilesInterpolateLinearlyAcrossTheSortedSamples()
    {
        var q = new Quantiles([4f, 0f, 2f]);
        Assert.Multiple(() =>
        {
            Assert.That(q.At(0f), Is.EqualTo(0f));
            Assert.That(q.At(0.25f), Is.EqualTo(1f));
            Assert.That(q.At(0.5f), Is.EqualTo(2f));
            Assert.That(q.At(1f), Is.EqualTo(4f));
            Assert.That(new Quantiles([0.7f]).At(0.9f), Is.EqualTo(0.7f));
        });
    }

    [Test]
    public void SamplesStayWithinTheirBoundsAndReplayForASeed()
    {
        var habits = MovementHabits.Load(ShippedPath)!;
        foreach (var profile in habits.Profiles)
        {
            var metrics = habits.Resolve(profile);
            foreach (var q in new[] { metrics.Debuff!, metrics.Cast!, metrics.Resolve!, metrics.SlackUse!, metrics.Settle!, metrics.IdleStep! })
            {
                var a = new Rng(123);
                var b = new Rng(123);
                for (var i = 0; i < 50; i++)
                {
                    var sample = q.Sample(a);
                    Assert.That(sample, Is.InRange(q.Min, q.Max), profile.Id);
                    Assert.That(sample, Is.EqualTo(q.Sample(b)), profile.Id);
                }
            }
        }
    }

    [Test]
    public void TheBuiltInDefaultsAreTodaysBots()
    {
        foreach (var role in Enum.GetValues<HabitRole>())
        {
            var metrics = MovementHabits.Default.Draw(role, new Rng(1)).Metrics;
            Assert.Multiple(() =>
            {
                foreach (var cue in Enum.GetValues<CueKind>())
                {
                    Assert.That(metrics.Reaction(cue).Min, Is.GreaterThanOrEqualTo(0.15f), $"{role} {cue}");
                    Assert.That(metrics.Reaction(cue).Max, Is.LessThanOrEqualTo(1.15f), $"{role} {cue}");
                }
                Assert.That(metrics.SlackUse!.Max, Is.Zero);
                Assert.That(metrics.Bulge!.Max, Is.Zero);
                Assert.That(metrics.PauseChance!.Max, Is.Zero);
                Assert.That(metrics.OvershootChance!.Max, Is.Zero);
                Assert.That(metrics.IdleRate!.Max, Is.Zero);
            });
        }
    }
}
