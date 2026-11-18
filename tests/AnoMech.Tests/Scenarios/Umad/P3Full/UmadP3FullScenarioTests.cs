using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad.P3Full;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.NegativeRun;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;
using BNpcBaseId = AnoMech.Scenarios.Umad.UmadConstants.BNpcBaseId;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

// The opening, the bridge and the end, on the run's clock. Every roll is pinned: fire first, its
// crystal on the right of the wind crystal (NE, so fire SE and water NW), Longitudinal Implosion,
// Entropy on SH and R, Dynamic Fluid on MT and M2; the drills pinned as their own tests pin them.
// The default pair (Warrior, Paladin) puts MT on Chaos and OT on Exdeath.
//
//    3.15  The four nearest Chaos (-8,0) take Epic Hero: MT (-6.7,0), RH, M1, M2.
//   22.56  Entropy 19 s, Dynamic Fluid 46 s, the winds 68 s; the wind crystal at (9.9,-9.9).
//   41.47  Exdeath's blast, r 14.8 round him in the middle; the party is on the wind wall.
//   41.66  Stray Flames on SH (16.3,-6.4) and R (6.4,16.3); 42.73 Inferno on R and C (17,7.1).
//   50.97  Thunder III on MT at (1.7,-1.7); 54.00 on OT.
//   64.52  Shockwaves along Chaos's facing, toward the wind wall; 66.52 across it.
//   68.66  Stray Spray on MT (12.9,-12.2) and M2 in the stack; 69.70 Tsunami on R (-16.3,-6.4) and C (-7.1,-17).
//  126.80  Thunder III on OT at Exdeath; 129.83 on MT.
//  286.72  Blizzard III freezes whoever stands still; 288.04 Big Bang on the middle.
public class UmadP3FullScenarioTests
{
    internal static void Pin(UmadP3FullStateOverrides o)
    {
        var opening = o.OpeningRolls;
        opening.ShortElement = UmadP3Element.Fire;
        opening.FireOnRight = true;
        opening.Implosion = ActionId.LongitudinalImplosion;
        opening.Debuff[ShieldHealer] = UmadP3Element.Fire;
        opening.Debuff[PhysRangedDps] = UmadP3Element.Fire;
        opening.Debuff[MainTank] = UmadP3Element.Water;
        opening.Debuff[MeleeDpsB] = UmadP3Element.Water;
        UmadP3LimitCutScenarioTests.Pin(o.LimitCutRolls);
        UmadP3BlackHoleScenarioTests.Pin(o.BlackHoleRolls);
    }

    private static void PinWaterFirst(UmadP3FullStateOverrides o)
    {
        Pin(o);
        o.OpeningRolls.ShortElement = UmadP3Element.Water;
    }

    private static NegativeRun<UmadP3FullScenario> Full(PartyRole player, Action<UmadP3FullStateOverrides>? pin = null)
        => Negative<UmadP3FullScenario>(player).Overrides(pin ?? Pin);

    // The wind frame of the pinned NE wind crystal: a toward it, b to its right from the middle.
    private static Vector2 Wind(float a, float b) => new(0.70710677f * (a + b), 0.70710677f * (b - a));

    private static ScenarioRun Sunny(PartyRole player, Action<ScenarioProbe> probe, Action<UmadP3FullStateOverrides>? pin = null, float? stopAt = null)
        => ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            Overrides = o => (pin ?? Pin)((UmadP3FullStateOverrides)o),
            Probe = probe,
        });

    private static IEnumerable<PartyRole> Holding(ScenarioProbe p, ushort statusId)
        => PerRole.All.Where(r => p.Member(r) is { } m && m.IsAlive() && m.HasStatus(statusId));

    private static SimEnemy Boss(ScenarioProbe p, uint bnpcBaseId)
        => p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == bnpcBaseId);

    // -- The Decisive Battle --

    [Test]
    public void TheFourNearestChaosFightIt()
    {
        PartyRole[] epic = [], fated = [];
        var run = Sunny(CasterDps, p =>
        {
            if (!p.Crossed(3.6f)) return;
            epic = [.. Holding(p, StatusId.EpicHero)];
            fated = [.. Holding(p, StatusId.FatedHero)];
        }, stopAt: 4f);
        Assert.That(run.Passed, run.ToString);
        Assert.That(epic, Is.EquivalentTo(new[] { MainTank, RegenHealer, MeleeDpsA, MeleeDpsB }));
        Assert.That(fated, Is.EquivalentTo(new[] { OffTank, ShieldHealer, PhysRangedDps, CasterDps }));
    }

    // R stands by Chaos instead of Exdeath: R fights Chaos, and whichever of the Chaos group stood
    // farthest from it fights Exdeath.
    [Test]
    public void StandingByTheWrongBossJoinsItsSide()
    {
        PartyRole[] epic = [], nearest = [];
        ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = PhysRangedDps,
            StopAt = 4f,
            Overrides = o => Pin((UmadP3FullStateOverrides)o),
            Takeovers = [new Takeover(2.6f, new Vector2(-7f, 1f))],
            Probe = p =>
            {
                if (p.Crossed(3.15f))
                {
                    var chaos = Boss(p, BNpcBaseId.ChaosP3).Position;
                    nearest = [.. PerRole.All.OrderBy(r => Vector3.DistanceSquared(p.Member(r)!.Position, chaos)).Take(4)];
                }
                if (p.Crossed(3.6f)) epic = [.. Holding(p, StatusId.EpicHero)];
            },
        });
        Assert.That(epic, Does.Contain(PhysRangedDps).And.Not.Contain(OffTank));
        Assert.That(epic, Is.EquivalentTo(nearest));
    }

    [Test]
    public void TheHeroesComeOffAsExdeathBeginsHisBlast()
    {
        var left = -1;
        var run = Sunny(OffTank, p => { if (p.Crossed(35f)) left = Holding(p, StatusId.EpicHero).Concat(Holding(p, StatusId.FatedHero)).Count(); }, stopAt: 36f);
        Assert.That(run.Passed, run.ToString);
        Assert.That(left, Is.Zero);
    }

    // -- Bowels of Agony --

    [Test]
    public void BowelsOfAgonyHandsOutTheElementsAndTheWinds()
    {
        var statuses = new Dictionary<PartyRole, (float? Entropy, float? Fluid, float? Wind)>();
        var run = Sunny(MeleeDpsA, p =>
        {
            if (!p.Crossed(23f)) return;
            foreach (var role in PerRole.All)
            {
                var member = p.Member(role)!;
                statuses[role] = (member.FindStatus(StatusId.EntropyP3)?.RemainingTime, member.FindStatus(StatusId.DynamicFluidP3)?.RemainingTime,
                    (member.FindStatus(StatusId.Headwind) ?? member.FindStatus(StatusId.Tailwind))?.RemainingTime);
            }
        }, stopAt: 24f);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(PerRole.All.Where(r => statuses[r].Entropy is not null), Is.EquivalentTo(new[] { ShieldHealer, PhysRangedDps }));
            Assert.That(PerRole.All.Where(r => statuses[r].Fluid is not null), Is.EquivalentTo(new[] { MainTank, MeleeDpsB }));
            Assert.That(statuses[ShieldHealer].Entropy, Is.EqualTo(41.56f - 23f).Within(0.1f));
            Assert.That(statuses[MainTank].Fluid, Is.EqualTo(68.56f - 23f).Within(0.1f));
            Assert.That(PerRole.All.Select(r => statuses[r].Wind), Has.All.EqualTo(90.56f - 23f).Within(0.1f));
        });
    }

    // -- Exdeath's Thunder III blast --

    [Test]
    public void StayingByExdeathThroughHisBlastDies()
        => Full(MeleeDpsA)
            .TeleportAt(40.5f, to: Wind(5f, 0f))
            .ShouldKill(ActionId.ThunderIII_Blast, MeleeDpsA);

    // -- The elements --

    // Positive: each element's hits find their holders and the crystal's two only; nobody is thrown.
    [TestCase(UmadP3Element.Fire)]
    [TestCase(UmadP3Element.Water)]
    public void EachElementHitsOnlyItsOwnTargets(UmadP3Element first)
    {
        var vulnerable = new List<(float At, PartyRole[] Roles)>();
        var run = Sunny(ShieldHealer, p =>
        {
            foreach (var at in new[] { 42.1f, 43.2f, 69.1f, 70.2f })
                if (p.Crossed(at)) vulnerable.Add((at, [.. Holding(p, StatusId.MagicVulnerabilityUp)]));
        }, first == UmadP3Element.Fire ? Pin : PinWaterFirst, stopAt: 71f);
        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Announcements.Where(a => a.Contains("off their wind")), Is.Empty);
        PartyRole[] fire = [ShieldHealer, PhysRangedDps], water = [], crystals = [PhysRangedDps, CasterDps];
        var (short1, long1) = first == UmadP3Element.Fire ? (fire, water) : (water, fire);
        Assert.Multiple(() =>
        {
            Assert.That(vulnerable[0].Roles, Is.EquivalentTo(short1), "the first element's own hits");
            Assert.That(vulnerable[1].Roles, Is.EquivalentTo(first == UmadP3Element.Fire ? short1 : crystals), "after its crystal");
            Assert.That(vulnerable[2].Roles, Is.EquivalentTo(long1), "the second element's own hits");
            Assert.That(vulnerable[3].Roles, Is.EquivalentTo(first == UmadP3Element.Fire ? crystals : long1), "after its crystal");
        });
    }

    // M1 stands on SH's Entropy, then in the ring of Inferno's donut round C with the vulnerability up.
    [Test]
    public void StrayFlamesThenTheInfernoRingKills()
        => Full(MeleeDpsA)
            .TeleportAt(41.3f, to: Wind(16.5f, 7f))
            .TeleportAt(42.3f, to: Wind(10.9f, 11.2f))
            .ShouldKill(ActionId.Inferno_Crystal, MeleeDpsA);

    // M1 steps off the stack into both Dynamic Fluid holders' rings, once Exdeath's blast is past.
    [Test]
    public void StandingInTwoDynamicFluidRingsDies()
        => Full(MeleeDpsA, PinWaterFirst)
            .TeleportAt(41.55f, to: Wind(10.2f, 0.3f))
            .ShouldKill(ActionId.StraySpray, MeleeDpsA);

    // With Dynamic Fluid on MT and R, M1 steps into MT's ring, then into C's Tsunami circle with the
    // vulnerability up, farther from the crystal than either ranged.
    [Test]
    public void StraySprayThenATsunamiCircleKills()
        => Full(MeleeDpsA, o =>
            {
                PinWaterFirst(o);
                o.OpeningRolls.Debuff[MeleeDpsB] = UmadP3Element.Fire;
                o.OpeningRolls.Debuff[PhysRangedDps] = UmadP3Element.Water;
            })
            .TeleportAt(41.55f, to: Wind(11.2f, 0.5f))
            .TeleportAt(42.3f, to: Wind(9.5f, -14.5f))
            .ShouldKill(ActionId.Tsunami_Crystal, MeleeDpsA);

    // M1 stands in C's circle but farther from the crystal than either ranged: one hit, thrown, and the
    // throw strips the wind, so the wind crystal fires its Cyclone early.
    [Test]
    public void ACrystalThrowStripsTheWindAndTheCycloneComesEarly()
    {
        var windAfter = true;
        var cycloneAt = 0f;
        var run = ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            StopAt = 47f,
            Overrides = o => PinWaterFirst((UmadP3FullStateOverrides)o),
            Takeovers = [new Takeover(42.3f, Wind(9.5f, -14.5f))],
            Probe = p =>
            {
                if (p.Crossed(44f)) windAfter = p.Member(MeleeDpsA)!.HasStatus(StatusId.Headwind) || p.Member(MeleeDpsA)!.HasStatus(StatusId.Tailwind);
                if (cycloneAt == 0f && Holding(p, StatusId.WindResistanceDownII).Any()) cycloneAt = p.Time;
            },
        });
        Assert.Multiple(() =>
        {
            Assert.That(run.Announcements.Where(a => a.Contains("MeleeDpsA off their wind")), Is.Not.Empty, run.ToString);
            Assert.That(windAfter, Is.False, "M1 keeps its wind");
            Assert.That(cycloneAt, Is.InRange(44.3f, 45.2f), "the early Cyclone");
        });
    }

    // -- The first buster --

    [Test]
    public void TheTanksTradeTheFirstBuster()
    {
        PartyRole[] first = [], second = [];
        var run = Sunny(CasterDps, p =>
        {
            if (p.Crossed(51.4f)) first = [.. Holding(p, StatusId.LightningResistanceDownII)];
            if (p.Crossed(54.4f)) second = [.. Holding(p, StatusId.LightningResistanceDownII)];
        }, stopAt: 55f);
        Assert.That(run.Passed, run.ToString);
        Assert.That(first, Is.EquivalentTo(new[] { MainTank }));
        Assert.That(second, Is.EquivalentTo(new[] { MainTank, OffTank }));
    }

    [Test]
    public void NonTankClosestToExdeathTakesTheFirstBuster()
        => Full(MeleeDpsA)
            .TeleportAt(50.4f, to: new Vector2(0.3f, 0.2f))
            .ShouldKill(ActionId.ThunderIII_Resolve, MeleeDpsA);

    // MT stays on Exdeath after the first hit, still carrying its Lightning Resistance Down, with no invuln up.
    [Test]
    public void TankNotSwappingTakesTheSecondHit()
        => Full(MainTank)
            .WithMitigationChecks()
            .TeleportAt(52.0f, to: new Vector2(0f, 0f))
            .ShouldKill(ActionId.ThunderIII_Resolve, MainTank);

    // -- Implosion --

    // Longitudinal fires along Chaos's facing first: toward the wind wall and back at the middle.
    [Test]
    public void StandingBehindChaosInTheFirstShockwaveDies()
        => Full(MeleeDpsA)
            .TeleportAt(64.0f, to: new Vector2(0f, 0f))
            .ShouldKill(ActionId.Shockwave, MeleeDpsA);

    [Test]
    public void StandingBesideChaosInTheSecondShockwaveDies()
        => Full(MeleeDpsA)
            .TeleportAt(64.7f, to: Wind(11f, -8f))
            .ShouldKill(ActionId.Shockwave, MeleeDpsA);

    // -- The bridge --

    [Test]
    public void TheTanksTradeTheBridgeBusterAndKefkaGrows()
    {
        PartyRole[] first = [], second = [];
        ushort maxStacks = 0;
        var run = Sunny(CasterDps, p =>
        {
            if (p.Crossed(127.2f)) first = [.. Holding(p, StatusId.LightningResistanceDownII)];
            if (p.Crossed(130.2f)) second = [.. Holding(p, StatusId.LightningResistanceDownII)];
            if (p.Crossed(133f)) maxStacks = Boss(p, BNpcBaseId.KefkaP3).FindStatus(StatusId.Max)?.Stacks ?? 0;
        }, stopAt: 134f);
        Assert.That(run.Passed, run.ToString);
        Assert.That(first, Is.EquivalentTo(new[] { OffTank }));
        Assert.That(second, Is.EquivalentTo(new[] { MainTank, OffTank }));
        Assert.That(maxStacks, Is.EqualTo(506));
    }

    // M1 walks onto Exdeath just before the first hit; the hits follow whoever is nearest him.
    [Test]
    public void NonTankClosestToExdeathTakesTheBridgeBuster()
        => Full(MeleeDpsA)
            .At(126.4f, p => p.Member(MeleeDpsA)!.SetPosition(Boss(p, BNpcBaseId.Exdeath).Position))
            .ShouldKill(ActionId.ThunderIII_Resolve, MeleeDpsA);

    // -- The end --

    [Test]
    public void StandingStillThroughBlizzardFreezes()
        => Full(MeleeDpsA)
            .FreezeAt(285.4f)
            .ShouldDieOfStatus(StatusId.DeepFreeze, MeleeDpsA);

    [Test]
    public void StandingInTheMiddleForBigBangDies()
        => Full(MeleeDpsA)
            .TeleportAt(287.2f, to: new Vector2(0.5f, 0f))
            .ShouldKill(ActionId.BigBang, MeleeDpsA);

    [Test]
    public void BothBossesFallAndThePhaseEnds()
    {
        bool chaosTargetable = true, exdeathTargetable = true;
        var run = Sunny(OffTank, p =>
        {
            if (!p.Crossed(292.5f)) return;
            chaosTargetable = Boss(p, BNpcBaseId.ChaosP3).Targetable;
            exdeathTargetable = Boss(p, BNpcBaseId.Exdeath).Targetable;
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(chaosTargetable || exdeathTargetable, Is.False);
        Assert.That(run.Elapsed, Is.InRange(297.7f, 302f));
    }
}
