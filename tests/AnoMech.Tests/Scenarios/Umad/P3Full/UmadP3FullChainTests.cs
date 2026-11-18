using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using static AnoMech.Core.Game.Party.PartyRole;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;
using BNpcBaseId = AnoMech.Scenarios.Umad.UmadConstants.BNpcBaseId;
using EObjId = AnoMech.Scenarios.Umad.UmadConstants.EObjId;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

// The hand-overs between the slices: one set of actors, the opening's winds and crystal carried into
// the Limit Cut, one hero status each, and the casts on the real timeline's times.
public class UmadP3FullChainTests
{
    private static readonly float[] Checkpoints = [10f, 75f, 124f, 130f, 200f, 285f];

    private static ScenarioRun Run(Action<ScenarioProbe> probe, string? timelinePath = null)
        => ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = CasterDps,
            Overrides = o => UmadP3FullScenarioTests.Pin((UmadP3FullStateOverrides)o),
            Probe = probe,
            TimelinePath = timelinePath,
        });

    private static int Count(ScenarioProbe p, uint bnpcBaseId) => p.World.Children.OfType<SimEnemy>().Count(e => e.BNpcBaseId == bnpcBaseId && e.IsActive);

    [Test]
    public void OneChaosExdeathAndKefkaPlayTheWholePhase()
    {
        var counts = new List<(float At, int Chaos, int Exdeath, int Kefka)>();
        var run = Run(p =>
        {
            foreach (var at in Checkpoints)
                if (p.Crossed(at)) counts.Add((at, Count(p, BNpcBaseId.ChaosP3), Count(p, BNpcBaseId.Exdeath), Count(p, BNpcBaseId.KefkaP3)));
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(counts, Has.Count.EqualTo(Checkpoints.Length));
        Assert.That(counts, Has.All.Matches<(float At, int Chaos, int Exdeath, int Kefka)>(c => c is { Chaos: 1, Exdeath: 1, Kefka: 1 }), string.Join("; ", counts));
    }

    // The Limit Cut adds no winds of its own and holds the bosses where the opening left them, at the wind crystal.
    [Test]
    public void TheLimitCutTakesOverTheOpeningsWindsCrystalAndBosses()
    {
        var winds = new Dictionary<PartyRole, (int Count, float Remaining)>();
        var crystals = -1;
        Vector3 chaos = default, exdeath = default;
        var run = Run(p =>
        {
            if (!p.Crossed(71.5f)) return;
            foreach (var role in PerRole.All)
            {
                var statuses = p.Member(role)!.ActiveStatusSnapshot.Where(s => s.StatusId is StatusId.Headwind or StatusId.Tailwind).ToList();
                winds[role] = (statuses.Count, statuses.FirstOrDefault().RemainingTime);
            }
            crystals = p.World.Children.OfType<SimEventObject>().Count(o => o.EObjRowId == EObjId.WindCrystal);
            chaos = p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == BNpcBaseId.ChaosP3).Position;
            exdeath = p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == BNpcBaseId.Exdeath).Position;
        });
        Assert.That(run.Passed, run.ToString);
        var hold = UmadP3LimitCutState.SpotHeading(3);
        Assert.Multiple(() =>
        {
            Assert.That(winds.Values.Select(w => w.Count), Has.All.EqualTo(1));
            Assert.That(winds.Values.Select(w => w.Remaining), Has.All.EqualTo(90.56f - 71.5f).Within(0.1f));
            Assert.That(crystals, Is.EqualTo(1));
            Assert.That(Vector3.Distance(chaos, UmadP3LimitCutState.OnCircle(hold, 9f)), Is.LessThan(1.5f), "Chaos at the wind crystal");
            Assert.That(Vector3.Distance(exdeath, UmadP3LimitCutState.OnCircle(hold, 10.6f) + UmadP3LimitCutState.OnCircle(hold + MathF.PI / 2f, 1.6f)),
                Is.LessThan(1.5f), "Exdeath at the wind crystal");
        });
    }

    // The second Decisive Battle replaces the heroes the first one handed out, and the Black Hole adds none.
    [Test]
    public void EachSeatHoldsOneHeroAtATime()
    {
        var heroes = new List<(float At, PartyRole Role, int Count)>();
        var run = Run(p =>
        {
            foreach (var at in new[] { 20f, 118.5f, 140f, 270f })
                if (p.Crossed(at))
                    foreach (var role in PerRole.All)
                        heroes.Add((at, role, p.Member(role)!.ActiveStatusSnapshot.Count(s => s.StatusId is StatusId.EpicHero or StatusId.FatedHero)));
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(heroes.Where(h => h.Count != 1), Is.Empty);
    }

    // The real clears' resolve times (cast bar end plus its release) for the casts this phase adds, and its
    // hits, on the run's clock.
    private static readonly (string Name, uint ActionId, float[] Times)[] KeyCasts =
    [
        ("The Decisive Battle (Chaos)", ActionId.DecisiveBattle_Chaos, [3.15f, 117.5f]),
        ("Bowels of Agony", ActionId.BowelsOfAgony, [22.56f]),
        ("Thunder III (blast)", ActionId.ThunderIII_Blast, [41.75f]),
        ("Stray Flames", ActionId.StrayFlames, [41.70f]),
        ("Inferno", ActionId.Inferno_Crystal, [42.73f]),
        ("Thunder III (cast)", ActionId.ThunderIII_Cast, [50.87f, 109.5f, 126.71f, 168.3f, 209.5f]),
        ("Thunder III (hits)", ActionId.ThunderIII_Resolve, [50.97f, 54.00f, 109.6f, 112.6f, 126.80f, 129.83f, 168.4f, 171.4f, 209.6f, 212.6f]),
        ("Trance", ActionId.Trance, [62.58f]),
        ("Longitudinal Implosion", ActionId.LongitudinalImplosion, [63.81f, 244.1f]),
        ("Shockwave", ActionId.Shockwave, [64.52f, 66.52f, 244.8f, 246.8f]),
        ("Stray Spray", ActionId.StraySpray, [68.63f]),
        ("Tsunami", ActionId.Tsunami_Crystal, [69.66f]),
        ("Umbra Smash", ActionId.UmbraSmash, [84.1f]),
        ("Max", ActionId.Max, [128.61f]),
        ("Big Bang", ActionId.BigBang, [288.04f]),
    ];

    private const float Tolerance = 0.25f;

    [Test]
    public void KeyCastsResolveOnTheTimelinesTimes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umadp3full-{Guid.NewGuid():N}.csv");
        try
        {
            var run = Run(_ => { }, path);
            Assert.That(run.Passed, run.ToString);
            var abilities = File.ReadLines(path).Skip(1).Select(ParseRow).Where(row => row.Kind == "ability").ToLookup(row => row.ActionId, row => row.Time);
            Assert.Multiple(() =>
            {
                foreach (var (name, actionId, times) in KeyCasts)
                {
                    var actual = DistinctMoments(abilities[actionId]);
                    Assert.That(actual, Has.Count.EqualTo(times.Length), $"{name} resolutions: {string.Join(", ", actual)}");
                    for (var i = 0; i < Math.Min(times.Length, actual.Count); i++)
                        Assert.That(actual[i], Is.EqualTo(times[i]).Within(Tolerance), $"{name} #{i + 1}");
                }
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<float> DistinctMoments(IEnumerable<float> times)
    {
        var moments = new List<float>();
        foreach (var time in times.Order())
            if (moments.Count == 0 || time - moments[^1] > 0.5f) moments.Add(time);
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
