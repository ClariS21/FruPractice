using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using static AnoMech.Core.Game.Party.PartyRole;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;
using TetherId = AnoMech.Scenarios.Umad.UmadConstants.TetherId;

namespace AnoMech.Tests;

// The Earthquake's lines as the 27 clean clears of the timelines hand them out: First, Second and Third
// in Line on 3, 3 and 2 players, each line one support and one DPS besides Accretion, which goes to one
// healer and one DPS on the first two lines. The group's own strat resolves the black holes by line:
// the first line's two, then its Accretion, and so on, the higher seat in the party list first, each
// holder 68 degrees clockwise of its hole and 8.6 y from the middle as the hole first fires.
public class UmadP3BlackHoleLineTests
{
    private static readonly ushort[] Lines = [StatusId.FirstInLine, StatusId.SecondInLine, StatusId.ThirdInLine];
    private const int ByLineNumber = 0;
    private const int DpsSupportAccretion = 1;

    [Test]
    public void TheLinesGoOutAsInTheRealFight()
    {
        Assert.That(Lines, Is.EqualTo(new ushort[] { 3004, 3005, 3006 }));
        var seen = new HashSet<(PartyRole Role, int Line)>();
        for (var seed = 0; seed < 60; seed++)
        {
            int[] line = [];
            PartyRole[] accretion = [];
            var run = ScenarioRun.Execute(typeof(UmadP3BlackHoleScenario), ByLineNumber, seed, new ScenarioRunOptions
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
    public void ByLineNumberIsTheDefaultAndTheRoleOrdersStayOptions()
    {
        Assert.That(new UmadP3BlackHoleScenario().AiStrats.Select(s => s.Name),
            Is.EqualTo(new[] { "Black Hole: By line number", "Black Hole: D>S>A", "Black Hole: S>D>A" }));
        Assert.That(new UmadP3FullScenario().AiStrats.Select(s => s.Name),
            Is.EqualTo(new[] { "Standard · Black Hole: By line number", "Standard · Black Hole: D>S>A", "Standard · Black Hole: S>D>A" }));
    }

    // Each holder spends their crust on their third hit: the first line by the second set, the second
    // by the third, the last at the fourth.
    [TestCaseSource(typeof(PerRole), nameof(PerRole.All))]
    public void EverySeatTakesItsTethersInLineOrder(PartyRole player)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var line = new Dictionary<PartyRole, int>();
            var spent = new Dictionary<PartyRole, float>();
            var run = ScenarioRun.Execute(typeof(UmadP3BlackHoleScenario), ByLineNumber, seed, new ScenarioRunOptions
            {
                PlayerRole = player,
                Probe = p =>
                {
                    if (p.Crossed(8.6f))
                        foreach (var r in PerRole.All) line[r] = Array.FindIndex(Lines, s => p.Member(r)!.HasStatus(s));
                    if (p.Time < 10f) return;
                    foreach (var r in PerRole.All)
                        if (!spent.ContainsKey(r) && p.Member(r)!.HasStatus(StatusId.MeanestExistence) && !p.Member(r)!.HasStatus(StatusId.PrimordialCrust))
                            spent[r] = p.Time;
                },
            });
            Assert.That(run.Passed, () => $"seed {seed}: {run}");
            Assert.That(spent.Keys, Is.EquivalentTo(PerRole.All), $"seed {seed}: a crust never spent");
            for (var l = 0; l < 2; l++)
            {
                var last = PerRole.All.Where(r => line[r] == l).Max(r => spent[r]);
                var next = PerRole.All.Where(r => line[r] == l + 1).Min(r => spent[r]);
                Assert.That(last, Is.LessThan(next), $"seed {seed}: line {l + 1} still holding as line {l + 2} spends");
            }
        }
    }

    // Slot i holds PartyRole i: the first line is MT, M1 and SH (Accretion). MT, higher in the party
    // list, takes the lone first tether; D>S>A gives it to M1.
    [TestCase(ByLineNumber, MainTank)]
    [TestCase(DpsSupportAccretion, MeleeDpsA)]
    public void TheFirstTetherGoesToTheLinesFirstTurn(int strat, PartyRole holder)
    {
        PartyRole[] holding = [];
        var run = ScenarioRun.Execute(typeof(UmadP3BlackHoleScenario), strat, 5, new ScenarioRunOptions
        {
            StopAt = 33f,
            Overrides = o => UmadP3BlackHoleScenarioTests.Pin((UmadP3BlackHoleStateOverrides)o),
            Probe = p => { if (p.Crossed(32.2f)) holding = [.. Tethers(p).Select(t => Role(p, t.B))]; },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(holding, Is.EqualTo(new[] { holder }));
    }

    // As each hole of the second and third sets first fires (62.83 and 97.12).
    [TestCase(62.8f)]
    [TestCase(97.1f)]
    public void EachHolderStandsWhereTheGroupDoes(float at)
    {
        var spots = new List<(float Turn, float Radius)>();
        var run = ScenarioRun.Execute(typeof(UmadP3BlackHoleScenario), ByLineNumber, 5, new ScenarioRunOptions
        {
            StopAt = at + 0.5f,
            Overrides = o => UmadP3BlackHoleScenarioTests.Pin((UmadP3BlackHoleStateOverrides)o),
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var tether in Tethers(p))
                {
                    var hole = Flat(tether.A!.Position);
                    var held = Flat(tether.B!.Position);
                    var turn = (Bearing(held) - Bearing(hole)) * 180f / MathF.PI;
                    spots.Add(((turn % 360f + 360f) % 360f, held.Length()));
                }
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(spots, Has.Count.EqualTo(3));
        Assert.That(spots.Select(s => s.Turn), Has.All.EqualTo(68f).Within(6f), "clockwise of the hole");
        Assert.That(spots.Select(s => s.Radius), Has.All.EqualTo(8.6f).Within(1f), "from the middle");
    }

    private static List<SimTether> Tethers(ScenarioProbe p)
        => [.. p.World.Children.OfType<SimTether>().Where(t => t.IsActive && t.TetherId == TetherId.GrabbyTether && t.A is not null && t.B is not null)];

    private static PartyRole Role(ScenarioProbe p, SimCharacter? who) => PerRole.All.Single(r => ReferenceEquals(p.Member(r), who));

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    // Clockwise from north on the map: x east, z south.
    private static float Bearing(Vector2 v) => MathF.Atan2(v.X, -v.Y);
}
