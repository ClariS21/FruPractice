using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P5Full;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.NegativeRun;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;

namespace AnoMech.Tests;

// The default comp is a Warrior main tank and a Paladin off tank: the Paladin starts with Kefka
// (provoking at 1.5), so it takes the first Surprise Flare, and the Warrior the first Surprise Holy
// (provoke 39.9, invuln 44.6). Pinned: Fell Forces' healer stack on RH and DPS stack on C, the first
// notes' Holy on RH, SH and M1, every Forsaken marker on RH.
//
//   13.07  Fell Forces 1: tanks (0,-7), healers (-4.7,5.3), DPS (5.8,6).
//   38.72  First notes: MT (-3.5,-11) OT (3.5,-11) Flare; RH (-11,3.5) SH (-11,-3.5) M1 (3.5,11) Holy.
//   41.89  Second notes: tanks (+-0.5,-9.5) Chaotic Flare; R1 E, C S, M2 W bait Holy 5 out.
//   45.37  Finale: OT (0,-18.5) Flare Diffusion r25; MT Chaotic Holy r6; the rest at (12,6), (0,12.5)
//          and (-12,6).
//   50.00  Fell Forces 4: MT alone in the tank stack, OT waits at (-4.5,-10).
//  159.73  Forsaken set 1: orange on C's marker at (0,13.5), black holes at the centre and (-13.5,13.5);
//          the party goes to waymark 4 (-6.75,6.75). Bonds at 164.75.
//
// A run pauses 5s after its first death.
public class UmadP5FullScenarioTests
{
    private static void Pin(UmadP5FullStateOverrides o)
    {
        o.FellForcesHealer = RegenHealer;
        o.FellForcesDps = CasterDps;
        o.OrchestraHoly = [RegenHealer, ShieldHealer, MeleeDpsA];
        o.ForsakenMarker = RegenHealer;
        o.ForsakenSide = 1;
        o.ForsakenPick = 0;
    }

    private static NegativeRun<UmadP5FullScenario> Full(PartyRole player, Action<UmadP5FullStateOverrides>? tweak = null)
        => Negative<UmadP5FullScenario>(player)
            .Overrides<UmadP5FullStateOverrides>(o =>
            {
                Pin(o);
                tweak?.Invoke(o);
            });

    // -- Fell Forces --

    // West of the tank stack, 8 y from the healers'.
    [Test]
    public void LeavingTheDpsStackKillsTheRestOfIt()
        => Full(MeleeDpsA)
            .TeleportAt(12.6f, to: new(-8f, -2f))
            .ShouldKill(ActionId.FellForcesDps, MeleeDpsB, PhysRangedDps, CasterDps);

    [Test]
    public void AHealerInTheTankStackDies()
        => Full(RegenHealer, o => o.FellForcesHealer = ShieldHealer)
            .TeleportAt(12.6f, to: new(0f, -7f))
            .ShouldKill(ActionId.FellForcesTank, RegenHealer);

    // The off tank holds Kefka, so the tank stack is on it, alone.
    [Test]
    public void ATankAloneInItsStackWithoutInvulnerabilityDies()
        => Full(OffTank)
            .WithMitigationChecks()
            .TeleportAt(12.6f, to: new(6f, -10f))
            .ShouldKill(ActionId.FellForcesTank, OffTank);

    // -- Maddening Orchestra --

    [Test]
    public void TwoHolyCirclesOnOnePlayerKillBoth()
        => Full(RegenHealer)
            .TeleportAt(38.4f, to: new(-11f, -2.8f))
            .ShouldKill(ActionId.Holy, RegenHealer, ShieldHealer);

    [Test]
    public void ANonTankInATanksFlareDies()
        => Full(MeleeDpsB)
            .TeleportAt(38.4f, to: new(-3.5f, -10.5f))
            .ShouldKill(ActionId.Flare, MeleeDpsB);

    // The main tank provoked at 39.9: Chaotic Flare is on it, alone.
    [Test]
    public void TanksApartForChaoticFlareKillTheOneItIsOn()
        => Full(OffTank)
            .TeleportAt(41f, to: new(8f, -8f))
            .ShouldKill(ActionId.ChaoticFlare, MainTank);

    [Test]
    public void StayingNearKefkaForTheFinaleDiesToFlareDiffusion()
        => Full(MeleeDpsB)
            .TeleportAt(44.9f, to: new(0f, 3f))
            .ShouldKill(ActionId.FlareDiffusion, MeleeDpsB);

    // The Holy tank holds still on its Chaotic Flare spot from 42.5, its own from then on: a seat a
    // bot drives has no mitigation to check.
    [Test]
    public void TheHolyTankWithoutInvulnerabilityDies()
        => Full(MainTank)
            .WithMitigationChecks()
            .FreezeAt(42.5f)
            .ShouldKill(ActionId.ChaoticHoly, MainTank);

    // Holmgang just before Surprise Holy runs out covers Chaotic Holy and both solo tank stacks.
    [Test]
    public void AnInvulnerabilityJustBeforeSurpriseHolyRunsOutCoversChaoticHolyAndBothSoloStacks()
    {
        var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 3, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            UserActionsEnabled = true,
            StopAt = 54f,
            Takeovers = [new Takeover(42.5f, null)],
            Overrides = o => Pin((UmadP5FullStateOverrides)o),
            Probe = p =>
            {
                if (p.Crossed(44.6f)) p.Member(MainTank)!.AddStatus(Holmgang, 10f);
            },
        });
        Assert.That(run.Passed, run.ToString);
    }

    // Pressed as the second notes land, it runs out before the second solo stack.
    [Test]
    public void AnInvulnerabilityPressedTooEarlyLeavesTheSecondSoloStackLethal()
        => Full(MainTank)
            .WithMitigationChecks()
            .FreezeAt(42.5f)
            .At(42.6f, p => p.Member(MainTank)!.AddStatus(Holmgang, 10f))
            .ShouldKill(ActionId.FellForcesTank, MainTank);

    // -- Forsaken --

    [Test]
    public void StayingUnderTheOrangeDies()
        => Full(MeleeDpsA)
            .FreezeAt(160f)
            .ShouldKill(ActionId.ForsakenOrange, MeleeDpsA);

    [Test]
    public void StandingInABlackHoleDies()
        => Full(MeleeDpsA)
            .TeleportAt(163f, to: new(0f, 1f))
            .ShouldKill(ActionId.ForsakenGround, MeleeDpsA);

    // Set 1's centre black hole stays a void.
    [Test]
    public void TouchingAVoidLaterDies()
        => Full(MeleeDpsA)
            .TeleportAt(170f, to: new(-3f, 3f))
            .ShouldKill(ActionId.ForsakenVoid, MeleeDpsA);

    // North of Kefka, clear of the orange, both black holes and their voids, 16 y from the stack.
    [Test]
    public void LeavingTheBondsStackKillsTheRest()
        => Full(MeleeDpsA)
            .TeleportAt(164.6f, to: new(-4f, -9.5f))
            .ShouldKill(ActionId.ForsakenBonds, AllBut(MeleeDpsA));

    // -- Forsaken Null --

    [Test]
    public void KefkaDiesAtTheKillTimeAndTheRunPasses()
    {
        bool? castingNull = null, deadAfter = null;
        var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 5, new ScenarioRunOptions
        {
            Probe = p =>
            {
                if (p.Crossed(213.9f)) castingNull = Kefka(p) is { IsCasting: true, CastActionId: ActionId.ForsakenNull };
                if (p.Crossed(214.1f)) deadAfter = Kefka(p)?.Dead;
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(castingNull, Is.True, "Forsaken Null's bar is up before the kill");
        Assert.That(deadAfter, Is.True, "Kefka is dead at the kill time");
        Assert.That(run.Elapsed, Is.LessThan(UmadP5Forsaken.NullLandsAt));
    }

    // Clears kill as late as 224 s: the blast lands 6.4 s after the bar.
    [Test]
    public void AKillAfterTheBarEndsStillClears()
    {
        var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 6, new ScenarioRunOptions
        {
            Overrides = o => ((UmadP5FullStateOverrides)o).KillAt = 224f,
        });
        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void WithTheBossSurvivingForsakenNullKillsEveryone()
    {
        var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 7, new ScenarioRunOptions
        {
            PlayerRole = CasterDps,
            WriteArtifactsOnFailure = false,
            Overrides = o => ((UmadP5FullStateOverrides)o).BossSurvives = true,
        });
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(Enum.GetValues<PartyRole>()), run.ToString);
        Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo(ActionId.ForsakenNull), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.InRange(UmadP5Forsaken.NullLandsAt, UmadP5Forsaken.NullLandsAt + 0.2f), run.ToString);
    }

    // -- The timeline --

    private const float Tolerance = 0.3f;

    // Bars Kefka begins, from the clears' medians.
    private static readonly (string Name, uint ActionId, float[] Times)[] KeyCasts =
    [
        ("Ultima Repeater", ActionId.UltimaRepeater, [3.12f, 85.04f]),
        ("Flood", ActionId.FloodCast, [19.66f]),
        ("Maddening Orchestra", ActionId.MaddeningOrchestra, [32.84f, 124.91f]),
        ("Celestriad", ActionId.Celestriad, [54.55f]),
        ("Stray Apocalypse", ActionId.ChaosEnd1, [99.58f]),
        ("Stray Entropy", ActionId.ChaosEnd2, [115.74f]),
        ("Forsaken", ActionId.ForsakenP5, [149.66f]),
        ("Forsaken Null", ActionId.ForsakenNull, [194.25f]),
    ];

    // Hits as they land on the clears.
    private static readonly (string Name, uint ActionId, float[] Times)[] KeyHits =
    [
        ("Ultima Repeater", ActionId.UltimaRepeaterHit, [8.21f, 8.92f, 9.59f, 10.3f, 90.12f, 90.83f, 91.5f, 92.21f]),
        ("Fell Forces", ActionId.FellForcesTank, [13.07f, 16.23f, 19.36f, 50f, 53.12f, 94.98f, 98.15f, 142.07f, 145.19f, 148.32f]),
        ("Flare", ActionId.Flare, [38.73f, 130.79f]),
        ("Chaotic Flare", ActionId.ChaoticFlare, [41.89f, 133.96f]),
        ("Chaotic Holy", ActionId.ChaoticHoly, [45.37f, 137.44f]),
        ("Forsaken", ActionId.ForsakenP5Raidwide, [167.82f, 175.97f, 184.11f]),
        ("Forsaken Bonds", ActionId.ForsakenBonds, [164.75f, 172.89f, 181.03f, 189.19f]),
    ];

    [Test]
    public void KeyCastsAndHitsLandOnTheTimelinesTimes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umadp5full-{Guid.NewGuid():N}.csv");
        try
        {
            var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 1, new ScenarioRunOptions { TimelinePath = path });
            Assert.That(run.Passed, run.ToString);
            var rows = File.ReadLines(path).Skip(1).Select(ParseRow).ToList();
            var casts = rows.Where(row => row.Kind == "cast").ToLookup(row => row.ActionId, row => row.Time);
            var abilities = rows.Where(row => row.Kind == "ability").ToLookup(row => row.ActionId, row => row.Time);
            foreach (var (name, actionId, times) in KeyCasts) AssertMoments(name, DistinctMoments(casts[actionId]), times, exactly: true);
            foreach (var (name, actionId, times) in KeyHits) AssertMoments(name, DistinctMoments(abilities[actionId]), times, exactly: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Catastrophic Choice is cast twice, each its own coin flip.
    [Test]
    public void BothCatastrophicChoicesComeOnTheirTimes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umadp5full-{Guid.NewGuid():N}.csv");
        try
        {
            ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 2, new ScenarioRunOptions { TimelinePath = path, StopAt = 82f });
            var choices = File.ReadLines(path).Skip(1).Select(ParseRow)
                .Where(row => row.Kind == "cast" && row.ActionId is ActionId.CatastrophicChoiceAero or ActionId.CatastrophicChoiceEarth)
                .Select(row => row.Time);
            AssertMoments("Catastrophic Choice", DistinctMoments(choices), [63.75f, 75.85f], exactly: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertMoments(string name, List<float> actual, float[] times, bool exactly)
    {
        Assert.That(actual, exactly ? Has.Count.EqualTo(times.Length) : Has.Count.AtLeast(times.Length), $"{name}: {string.Join(", ", actual)}");
        for (var i = 0; i < times.Length; i++)
            Assert.That(actual[i], Is.EqualTo(times[i]).Within(Tolerance), $"{name} #{i + 1}");
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
    private static (float Time, string Kind, uint ActionId) ParseRow(string line)
    {
        var cells = line.Split(',');
        var parsed = uint.TryParse(cells[4], out var id);
        return (float.Parse(cells[0], CultureInfo.InvariantCulture), cells[1], parsed ? id : 0);
    }

    private const ushort Holmgang = 409;

    private static SimEnemy? Kefka(ScenarioProbe p)
        => p.World.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == UmadConstants.BNpcBaseId.KefkaP5);
}
