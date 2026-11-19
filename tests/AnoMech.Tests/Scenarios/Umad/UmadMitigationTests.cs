using System.Reflection;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P5Full;

namespace AnoMech.Tests;

public class UmadMitigationTests
{
    private static IEnumerable<IScenario> Dmu() => ScenarioCatalog.Create().Scenarios.Where(s => s.GetType().Namespace!.Contains(".Umad"));

    private static IEnumerable<MitHitDef> AllHits() => typeof(UmadMitigation).GetNestedTypes()
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.FieldType == typeof(MitHitDef))
        .Select(f => (MitHitDef)f.GetValue(null)!)
        .DistinctBy(h => h.Key);

    private static readonly HashSet<uint> UmadActionIds = typeof(UmadActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(EnemyAction))
        .Select(f => ((EnemyAction)f.GetValue(null)!).ActionId)
        .ToHashSet();

    [Test]
    public void EveryDmuScenarioOffersMitigationPractice()
    {
        Assert.That(Dmu().Count(), Is.EqualTo(10));
        Assert.That(Dmu().Where(s => !s.SupportsMitigationPractice).Select(s => s.FullName()), Is.Empty);
    }

    private static IEnumerable<TestCaseData> Scenarios()
        => Dmu().Select(s => new TestCaseData(s.GetType()).SetName($"{s.FullName()} runs its plan"));

    // Mitigation practice starts with the run, its bots press their rows and its hits move the modelled HP.
    [TestCaseSource(nameof(Scenarios))]
    public void TheRunStartsMitigationPracticeAndItsHitsMoveHp(Type scenarioType)
    {
        var active = false;
        var lowest = 1f;
        ScenarioRun.Execute(scenarioType, 0, 0, new ScenarioRunOptions
        {
            UserActionsEnabled = true,
            Probe = p =>
            {
                if (!p.World.MitPractice.MovesHp) return;
                active = true;
                foreach (var member in p.World.Party.AllMembers())
                    if (p.World.MitPractice.HpFraction(member) is { } hp) lowest = MathF.Min(lowest, hp);
            },
        });
        Assert.That(active, "Mitigation Practice never started");
        Assert.That(lowest, Is.LessThan(0.9f), "no modelled hit moved anyone's HP");
    }

    [Test]
    public void EveryHitNamesAnActionTheDmuScenariosCast()
    {
        foreach (var hit in AllHits())
            foreach (var id in hit.ActionIds!)
                Assert.That(UmadActionIds, Does.Contain(id), $"{hit.Name} ({hit.Key}): {id:X}");
    }

    [Test]
    public void EveryHitDealsDamage()
    {
        foreach (var hit in AllHits())
            Assert.That(hit.Raw, Is.GreaterThan(0f), hit.Key);
    }

    // As FRU's: a DPS's median from the clears, a tank buster's on a tank.
    [Test]
    public void TheRawValuesAreTheClearsMedians()
    {
        Assert.That(UmadMitigation.P3.BowelsOfAgony.Raw, Is.EqualTo(166200f));
        Assert.That(UmadMitigation.P3.ThunderIII.Raw, Is.EqualTo(555000f));
        Assert.That(UmadMitigation.P3.ThunderIII.TankBuster, Is.True);
        Assert.That(UmadMitigation.P4.GrandCross.Raw, Is.EqualTo(289300f));
        Assert.That(UmadMitigation.P5.Forsaken.Raw, Is.EqualTo(414000f));
        Assert.That(UmadMitigation.P5.FellForcesTank.SoloRaw, Is.EqualTo(820400f));
    }

    [Test]
    public void EachHitsKindIsItsActionsAttackType()
    {
        var data = new DataminingGameData(new FakeRsvFunctions());
        foreach (var hit in AllHits())
            foreach (var id in hit.ActionIds!)
            {
                var type = data.Action(id)?.AttackType ?? 0;
                if (type == 0) continue;
                Assert.That(hit.Kind, Is.EqualTo(DamageKinds.FromAttackType((sbyte)type)), $"{hit.Name} ({hit.Key}): {id:X}");
            }
    }

    // Unmitigated, the big raidwides take most of a DPS's HP, and the heaviest would kill outright.
    [TestCase("Bowels of Agony", 1, 0.7f, 0.8f)]
    [TestCase("Grand Cross", 1, 1.2f, 1.4f)]
    [TestCase("Ultima Upsurge", 1, 0.9f, 1f)]
    [TestCase("Ultima Repeater", 4, 2f, 2.4f)]
    [TestCase("Forsaken", 1, 1.7f, 1.9f)]
    [TestCase("Forsaken Bonds", 1, 1.2f, 1.3f)]
    public void AnUnmitigatedPartyLosesARealisticShareOfItsHp(string name, int hits, float least, float most)
    {
        var hit = AllHits().First(h => h.Name == name);
        var profile = UmadMitigation.Profile;
        var loss = hits * MitDamage.Apply(hit, MitClass.Physical, profile, [], [], [], assumeTankCooldowns: false).HpLoss / profile.PhysicalMaxHp;
        Assert.That(loss, Is.InRange(least, most));
    }

    // In a run, a mitigated Bowels of Agony still takes a real bite out of the party.
    [Test]
    public void BowelsOfAgonyTakesARealBiteEvenMitigated()
    {
        float before = 1f, lowest = 1f;
        ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, 0, new ScenarioRunOptions
        {
            UserActionsEnabled = true,
            PlayerRole = PartyRole.MainTank,
            StopAt = 26f,
            Probe = p =>
            {
                if (p.Member(PartyRole.MeleeDpsA) is not { } dps || p.World.MitPractice.HpFraction(dps) is not { } hp) return;
                if (p.Time < 22f) before = hp;
                else lowest = MathF.Min(lowest, hp);
            },
        });
        Assert.That(before - lowest, Is.GreaterThan(0.3f));
    }

    // A Warrior with only the sheet's Rampart left takes the last Fell Forces from full HP; the
    // healers' assist on each tank is what keeps it standing.
    [Test]
    public void TheHealersAssistEachTankForTheLastFellForces()
    {
        var plan = UmadP5FullScenario.MitigationPlan(PartyRole.MainTank);
        var assists = plan.Entries.Where(e => e.Target is not null).ToList();
        Assert.That(assists.Select(e => e.Target), Is.EquivalentTo(new PartyRole?[] { PartyRole.MainTank, PartyRole.OffTank }));
        Assert.That(assists.All(e => e.Column == SheetColumn.WHM && e.Covers.Single() == UmadP5FullMitigation.FellForcesAt[^1]));
    }
}
