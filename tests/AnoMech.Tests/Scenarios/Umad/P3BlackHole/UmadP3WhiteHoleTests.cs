using System.Globalization;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

// Exdeath's White Hole: cast at 239.1 of the phase (the drill's 113.38), resolves 244.1, its harmless hit
// lands 245.3. Whoever isn't at full HP is petrified for 3 s, shown with the resolve as a stun is. The sim
// can't tell who is full, so the check is a setting, Pass by default; Fail petrifies the whole party.
public class UmadP3WhiteHoleTests
{
    private const float DrillStart = 125.72f;

    // The player's HP bar takes the hit after its own server tick, 0.05 to 0.35 s on. A run steps in
    // 60 fps frames, so a time lands up to a few frames late.
    [TestCase(false)]
    [TestCase(true)]
    public void WhiteHoleCastsAndLandsOnTheRealClock(bool full)
    {
        var offset = full ? 0f : DrillStart;
        var path = Path.Combine(Path.GetTempPath(), $"umadp3whitehole-{Guid.NewGuid():N}.csv");
        try
        {
            Execute(full, HpCheckMode.Fail, stopAt: 246.5f - offset, timelinePath: path);
            var rows = File.ReadLines(path).Skip(1).Select(l => l.Split(','))
                .Select(c => (Time: float.Parse(c[0], CultureInfo.InvariantCulture) + offset, Kind: c[1], Id: c[4])).ToList();
            var whiteHole = ActionId.WhiteHole.ToString(CultureInfo.InvariantCulture);
            var petrified = rows.Where(r => r.Kind == "status_gain" && r.Id == StatusId.Petrification.ToString(CultureInfo.InvariantCulture)).Select(r => r.Time).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(rows.Where(r => r.Kind == "cast" && r.Id == whiteHole).Select(r => r.Time), Is.EqualTo(new[] { 239.1f }).Within(0.1f), "cast");
                Assert.That(rows.Where(r => r.Kind == "ability" && r.Id == whiteHole).Select(r => r.Time), Is.EqualTo(new[] { 244.1f }).Within(0.1f), "resolve");
                Assert.That(rows.Where(r => r.Kind == "impact" && r.Id == whiteHole).Select(r => r.Time).ToList(), Is.Not.Empty.And.All.InRange(245.2f, 245.7f), "lands");
                Assert.That(petrified, Has.Count.EqualTo(8).And.All.EqualTo(244.1f).Within(0.1f), "petrified");
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TheHpCheckPassesUnlessSetToFail()
    {
        Assert.That(((UmadP3BlackHoleStateOverrides)new UmadP3BlackHoleScenario().SettingsOverrides).HpCheck, Is.EqualTo(HpCheckMode.Pass));
        Assert.That(((UmadP3FullStateOverrides)new UmadP3FullScenario().SettingsOverrides).BlackHoleRolls.HpCheck, Is.EqualTo(HpCheckMode.Pass));
    }

    [TestCase(false, HpCheckMode.Pass)]
    [TestCase(false, HpCheckMode.Fail)]
    [TestCase(true, HpCheckMode.Pass)]
    [TestCase(true, HpCheckMode.Fail)]
    public void AFailedHpCheckPetrifiesTheWholePartyForThreeSeconds(bool full, HpCheckMode check)
    {
        var offset = full ? 0f : DrillStart;
        PartyRole[] petrified = [], after = [];
        var left = new List<float>();
        var run = Execute(full, check, stopAt: 247.5f - offset, probe: p =>
        {
            if (p.Crossed(244.2f - offset))
            {
                petrified = Petrified(p).ToArray();
                left.AddRange(petrified.Select(r => p.Member(r)!.FindStatus(StatusId.Petrification)!.RemainingTime));
            }
            if (p.Crossed(247.2f - offset)) after = Petrified(p).ToArray();
        });
        Assert.Multiple(() =>
        {
            if (check == HpCheckMode.Pass)
            {
                Assert.That(run.Passed, run.ToString);
                Assert.That(petrified, Is.Empty);
            }
            else
            {
                Assert.That(petrified, Is.EquivalentTo(PerRole.All));
                Assert.That(left, Has.All.EqualTo(2.9f).Within(0.1f));
            }
            Assert.That(after, Is.Empty);
        });
    }

    private static IEnumerable<PartyRole> Petrified(ScenarioProbe p) => PerRole.All.Where(r => p.Member(r)!.HasStatus(StatusId.Petrification));

    private static ScenarioRun Execute(bool full, HpCheckMode check, float stopAt, Action<ScenarioProbe>? probe = null, string? timelinePath = null)
        => ScenarioRun.Execute(full ? typeof(UmadP3FullScenario) : typeof(UmadP3BlackHoleScenario), 0, 3, new ScenarioRunOptions
        {
            StopAt = stopAt,
            WriteArtifactsOnFailure = check == HpCheckMode.Pass,
            TimelinePath = timelinePath,
            Probe = probe,
            Overrides = o =>
            {
                var blackHole = o is UmadP3FullStateOverrides f ? f.BlackHoleRolls : (UmadP3BlackHoleStateOverrides)o;
                if (o is UmadP3FullStateOverrides fullRolls) UmadP3FullScenarioTests.Pin(fullRolls);
                else UmadP3BlackHoleScenarioTests.Pin(blackHole);
                blackHole.HpCheck = check;
            },
        });
}
