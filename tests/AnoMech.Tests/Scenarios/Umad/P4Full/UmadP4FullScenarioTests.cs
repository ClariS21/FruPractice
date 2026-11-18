using System.Globalization;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Umad.P4Full;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.NegativeRun;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;

namespace AnoMech.Tests;

// P4 Full wraps the Kefka Says drill on the real clock (drill + 3.75 s), plus the tail the drill lacks. Times
// below are the real phase's (docs/superpowers/dmu/timelines/dmu-p4.json medians).
public class UmadP4FullScenarioTests
{
    private const float Tolerance = 0.3f;

    private static ScenarioRun Run(HpCheckMode? mode, string? timeline = null, Action<ScenarioProbe>? probe = null, PartyRole role = CasterDps, int seed = 1)
        => ScenarioRun.Execute(typeof(UmadP4FullScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = role,
            TimelinePath = timeline,
            Probe = probe,
            Overrides = o =>
            {
                var overrides = (UmadP4KefkaSaysStateOverrides)o;
                UmadP4KefkaSaysScenarioTests.Pin(overrides);
                if (mode is { } m) overrides.HpCheck = m;
            },
        });

    [Test]
    public void TheDrillKeepsItsRecordedTailAndTheFullPhaseDefaultsToAPass()
    {
        Assert.That(new UmadP4KefkaSaysStateOverrides().HpCheck, Is.EqualTo(HpCheckMode.Drill));
        Assert.That(((UmadP4KefkaSaysStateOverrides)new UmadP4FullScenario().SettingsOverrides).HpCheck, Is.EqualTo(HpCheckMode.Pass));
    }

    [Test]
    public void PassContinuesWithTheTailAndEndsCleanlyAtThePhaseEnd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmup4full-{Guid.NewGuid():N}.csv");
        try
        {
            var targetable = new Dictionary<float, bool>();
            var run = Run(HpCheckMode.Pass, path, probe: p =>
            {
                foreach (var t in new[] { 124.3f, 131.9f, 132.6f })
                    if (p.Crossed(t) && p.Game.Catalog.Scenarios.OfType<UmadP4FullScenario>().Single().Kefka is { } kefka)
                        targetable[t] = kefka.Targetable;
            });
            Assert.That(run.Passed, run.ToString);
            Assert.That(run.Elapsed, Is.InRange(UmadP4FullScenario.PhaseEnd, UmadP4FullScenario.PhaseEnd + 5f), "the run ends at the phase end");
            Assert.That(targetable, Is.EqualTo(new Dictionary<float, bool> { [124.3f] = true, [131.9f] = true, [132.6f] = false }),
                "Kefka stays targetable through the HP check and is untargetable from 132.2");

            var rows = File.ReadLines(path).Skip(1).Select(Parse).ToList();
            var poofs = Moments(rows.Where(r => r.Id == ActionId.KefkaPoof && r.Kind == "ability").Select(r => r.Time));
            Assert.That(poofs, Has.Count.EqualTo(2), "two Poofs");
            Assert.That(poofs[1], Is.EqualTo(125.8f).Within(Tolerance), "second Poof");

            var upsurgeCasts = Moments(rows.Where(r => r.Id == ActionId.UltimaUpsurge && r.Kind == "cast").Select(r => r.Time));
            Assert.That(upsurgeCasts, Has.Count.EqualTo(2), "two Ultima Upsurges");
            Assert.That(upsurgeCasts[0], Is.EqualTo(88.3f).Within(Tolerance), "first Upsurge");
            Assert.That(upsurgeCasts[1], Is.EqualTo(127.2f).Within(Tolerance), "second Upsurge");

            var hits = rows.Where(r => r.Id == ActionId.UltimaUpsurge && r.Kind == "ability" && r.Time > 128f).ToList();
            Assert.That(hits, Is.Not.Empty, "the second Upsurge resolves");
            Assert.That(hits[0].Time, Is.InRange(132.0f, 133.4f), "and its damage lands about 133");
            Assert.That(hits[0].Detail, Does.StartWith("hits=8"), "on all eight");
            Assert.That(rows.Where(r => r.Id == ActionId.LightOfJudgment_Enrage && r.Kind == "cast"), Is.Empty, "no enrage on a pass");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void FailMakesTheEnrageLethalAndFailsTheRunWithItsReason()
    {
        var run = Run(HpCheckMode.Fail);
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(run.MechanicFailures, Has.Count.EqualTo(1), run.ToString());
        Assert.That(run.MechanicFailures[0].Reason, Does.Contain("25% HP").And.Contain("Light of Judgment"));
        Assert.That(run.MechanicFailures[0].Time, Is.EqualTo(123.78f).Within(Tolerance));
        Assert.That(run.Passed, Is.False);
    }

    [Test]
    public void FailThroughTheNegativeRunHarness()
        => Negative<UmadP4FullScenario>(CasterDps)
            .Overrides<UmadP4KefkaSaysStateOverrides>(o => { UmadP4KefkaSaysScenarioTests.Pin(o); o.HpCheck = HpCheckMode.Fail; })
            .ShouldFail();

    [Test]
    public void FailEndsBeforeTheSecondPoofAndUpsurge()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmup4full-{Guid.NewGuid():N}.csv");
        try
        {
            Run(HpCheckMode.Fail, path);
            var rows = File.ReadLines(path).Skip(1).Select(Parse).ToList();
            Assert.That(rows.Count(r => r.Id == ActionId.UltimaUpsurge && r.Kind == "cast"), Is.EqualTo(1), "only the first Upsurge");
            Assert.That(rows.Any(r => r.Id == ActionId.LightOfJudgment_Enrage && r.Kind == "cast"), Is.True, "the enrage is cast");
        }
        finally { File.Delete(path); }
    }

    // The drill's recorded wipe: untargetable at 123.65, a Light of Judgment that does nothing, and a run that still ends as a success.
    [Test]
    public void DrillModeKeepsTheRecordedTailAndEndsAsASuccess()
    {
        var run = Run(HpCheckMode.Drill);
        Assert.That(run.Passed, run.ToString());
    }

    // Real-clock conformance: the drill's casts land on the real timeline's medians (drill + 3.75 s).
    [TestCase(ActionId.KefkaSays, 5.2f)]
    [TestCase(ActionId.KefkaPoof, 13.3f)]
    [TestCase(ActionId.MysteryMagic, 14.7f)]
    [TestCase(ActionId.KefkaRest, 53.0f)]
    [TestCase(ActionId.ManaCharge, 72.0f)]
    [TestCase(ActionId.ManaRelease, 107.4f)]
    public void TheDrillsCastsLandOnTheRealClock(uint action, float time)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmup4full-{Guid.NewGuid():N}.csv");
        try
        {
            Run(HpCheckMode.Pass, path);
            var first = File.ReadLines(path).Skip(1).Select(Parse).Where(r => r.Id == action).Min(r => r.Time);
            Assert.That(first, Is.EqualTo(time).Within(Tolerance));
        }
        finally { File.Delete(path); }
    }

    private static List<float> Moments(IEnumerable<float> times)
    {
        var moments = new List<float>();
        foreach (var time in times.Order())
            if (moments.Count == 0 || time - moments[^1] > 0.5f) moments.Add(time);
        return moments;
    }

    // Columns t, kind, actor, actor_name, id, name, target, target_name, x, z, rot, detail.
    private static (float Time, string Kind, uint Id, string Detail) Parse(string line)
    {
        var cells = line.Split(',');
        _ = uint.TryParse(cells[4], out var id);
        return (float.Parse(cells[0], CultureInfo.InvariantCulture), cells[1], id, cells.Length > 11 ? cells[11] : "");
    }
}
