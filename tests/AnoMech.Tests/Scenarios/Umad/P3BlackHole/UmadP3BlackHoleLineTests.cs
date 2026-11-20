using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using static AnoMech.Core.Game.Party.PartyRole;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

public class UmadP3BlackHoleLineTests
{
    private static readonly ushort[] Lines = [StatusId.FirstInLine, StatusId.SecondInLine, StatusId.ThirdInLine];

    [Test]
    public void TheLinesGoOutAsInTheRealFight()
    {
        Assert.That(Lines, Is.EqualTo(new ushort[] { 3004, 3005, 3006 }));
        var seen = new HashSet<(PartyRole Role, int Line)>();
        for (var seed = 0; seed < 60; seed++)
        {
            int[] line = [];
            PartyRole[] accretion = [];
            var run = ScenarioRun.Execute(typeof(UmadP3BlackHoleScenario), 0, seed, new ScenarioRunOptions
            {
                StopAt = 9f,
                Probe = p =>
                {
                    if (!p.Crossed(8.6f)) return;
                    line = [.. PerRole.All.Select(r => Array.FindIndex(Lines, s => p.Member(r)!.HasStatus(s)))];
                    accretion = [.. PerRole.All.Where(r => p.Member(r)!.HasStatus(StatusId.Accretion))];
                },
            });
            Assert.That(run.Passed, run.ToString);
            var holders = PerRole.All.Select(r => (Role: r, Line: line[(int)r])).ToList();
            foreach (var h in holders) seen.Add(h);
            Assert.Multiple(() =>
            {
                Assert.That(Enumerable.Range(0, 3).Select(l => holders.Count(h => h.Line == l)), Is.EqualTo(new[] { 3, 3, 2 }), $"seed {seed}: line sizes");
                Assert.That(accretion.Count(r => r is RegenHealer or ShieldHealer), Is.EqualTo(1), $"seed {seed}: one healer on Accretion");
                Assert.That(accretion.Count(r => r.IsDps()), Is.EqualTo(1), $"seed {seed}: one DPS on Accretion");
                Assert.That(accretion.Select(r => line[(int)r]).Order(), Is.EqualTo(new[] { 0, 1 }), $"seed {seed}: Accretion on the first two lines");
                for (var l = 0; l < 3; l++)
                {
                    var pair = holders.Where(h => h.Line == l && !accretion.Contains(h.Role)).Select(h => h.Role).ToList();
                    Assert.That(pair.Count(r => !r.IsDps()), Is.EqualTo(1), $"seed {seed}: one support on line {l + 1} besides Accretion");
                    Assert.That(pair.Count(r => r.IsDps()), Is.EqualTo(1), $"seed {seed}: one DPS on line {l + 1} besides Accretion");
                }
            });
        }
        Assert.That(PerRole.All.SelectMany(r => Enumerable.Range(0, 3).Select(l => (r, l))).Where(x => !seen.Contains(x)), Is.Empty, "a seat never got a line");
    }

    [Test]
    public void DpsSupportAccretionIsTheDefaultInTheDrillAndFullPhase()
    {
        Assert.That(new UmadP3BlackHoleScenario().AiStrats.Select(s => s.Name),
            Is.EqualTo(new[] { "Black Hole: D>S>A", "Black Hole: S>D>A" }));
        Assert.That(new UmadP3FullScenario().AiStrats.Select(s => s.Name),
            Is.EqualTo(new[] { "Standard · Black Hole: D>S>A", "Standard · Black Hole: S>D>A" }));
    }

    [TestCase(typeof(UmadP3BlackHoleScenario))]
    [TestCase(typeof(UmadP3FullScenario))]
    public void OutOfRangeStratFallsBackToDefault(Type scenario)
    {
        var run = ScenarioRun.Execute(scenario, 3, 5);
        Assert.That(run.Passed, run.ToString);
    }

}
