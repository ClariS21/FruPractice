using System.Reflection;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;

namespace AnoMech.Tests;

public class FruMitigationTests
{
    private static IEnumerable<MitHitDef> AllHits() => typeof(FruMitigation).GetNestedTypes()
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.FieldType == typeof(MitHitDef))
        .Select(f => (MitHitDef)f.GetValue(null)!)
        .DistinctBy(h => h.Key);

    private static readonly HashSet<uint> FruActionIds = typeof(FruActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(EnemyAction))
        .Select(f => ((EnemyAction)f.GetValue(null)!).ActionId)
        .ToHashSet();

    [Test]
    public void EveryHitNamesAnActionTheFruScenariosCast()
    {
        foreach (var hit in AllHits().Where(h => h.ActionIds != null))
            foreach (var id in hit.ActionIds!)
                Assert.That(FruActionIds, Does.Contain(id), $"{hit.Name} ({hit.Key}): {id}");
    }

    [Test]
    public void EveryHitDealsDamageOrSetsHp()
    {
        foreach (var hit in AllHits())
            Assert.That(hit.Raw > 0f || hit.ToOneHp, $"{hit.Key}");
    }

    [Test]
    public void ABustersTankRawIsTheReportedOne()
    {
        Assert.That(FruMitigation.P3.BlackHalo.Raw, Is.EqualTo(676000f));
        Assert.That(FruMitigation.P3.DarkestDance.Raw, Is.EqualTo(696000f));
    }

    [Test]
    public void EachHitsKindIsItsActionsAttackType()
    {
        var data = new DataminingGameData(new FakeRsvFunctions());
        foreach (var hit in AllHits().Where(h => h.ActionIds != null))
            foreach (var id in hit.ActionIds!)
            {
                var type = data.Action(id)?.AttackType ?? 0;
                if (type == 0) continue;
                Assert.That(hit.Kind, Is.EqualTo(DamageKinds.FromAttackType((sbyte)type)), $"{hit.Name} ({hit.Key}): {id}");
            }
    }
}
