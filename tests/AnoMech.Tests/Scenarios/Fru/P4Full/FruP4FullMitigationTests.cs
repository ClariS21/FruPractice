using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P4Full;

namespace AnoMech.Tests;

public class FruP4FullMitigationTests
{
    private static readonly Dictionary<PartyRole, JobId> DefaultComp = new()
    {
        [PartyRole.MainTank] = JobId.Warrior, [PartyRole.OffTank] = JobId.Paladin, [PartyRole.RegenHealer] = JobId.WhiteMage,
        [PartyRole.ShieldHealer] = JobId.Scholar, [PartyRole.MeleeDpsA] = JobId.Dragoon, [PartyRole.MeleeDpsB] = JobId.Monk,
        [PartyRole.PhysRangedDps] = JobId.Bard, [PartyRole.CasterDps] = JobId.BlackMage,
    };

    [Test]
    public void TheChainedPlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP4FullScenario.MitigationPlan(), "Full P4");
    }

    [Test]
    public void FeintAndThePhysRangedPartyMitGoOnBothAkhMornAfahsNinetySecondsApartAndNotOnHallowedWings()
    {
        var data = FruP4FullScenario.MitigationPlan();
        var plan = MitPlan.Resolve(data.Entries, DefaultComp);
        foreach (var (role, name) in new[] { (PartyRole.MeleeDpsA, "Feint"), (PartyRole.PhysRangedDps, "Troubadour") })
        {
            var rows = plan.Where(p => p.Slot == role && p.Action.Name == name).OrderBy(p => p.At).ToList();
            Assert.That(rows, Has.Count.EqualTo(2), name);
            TestContext.Out.WriteLine($"{name}: " + string.Join("; ", rows.Select(r => $"{r.Label} press {r.At:F2} window {r.Open:F2}-{r.Close:F2}")));
            Assert.That(rows.All(r => r.Label.StartsWith("Akh Morn Afah")), name + " only on the Akh Morn Afahs");
            Assert.That(rows[1].At - rows[0].At, Is.GreaterThanOrEqualTo(90f), name);
            Assert.That(rows[1].At + 15f, Is.GreaterThanOrEqualTo(FruP4FullMitigation.MornAfah2At - 1.8f + 1f), name + " covers the last Morn Afah snapshot");
            Assert.That(rows[0].At + 15f, Is.GreaterThanOrEqualTo(FruP4FullMitigation.MornAfah1At - 1.8f), name + " covers the first Morn Afah snapshot");
        }
    }

    [Test]
    public void MeleeOnesFeintIsExpectedOnShivaAndMornAfahAndEdgeComeFromTheRightSources()
    {
        var data = FruP4FullScenario.MitigationPlan();
        var feints = MitPlan.Resolve(data.Entries, DefaultComp).Where(p => p.Slot == PartyRole.MeleeDpsA && p.Action.Name == "Feint").ToList();
        Assert.That(feints, Has.Count.EqualTo(2));
        // MitGrader grades the press's time only; the boss it goes on is the row's target, which is Shiva.
        Assert.That(feints.Select(f => data.Entries[f.Entry].On), Is.All.EqualTo(FruMitigation.Usurper));

        Assert.That(FruMitigation.P4.MornAfah.Source, Is.EqualTo(FruMitigation.Usurper));
        Assert.That(FruMitigation.P4.EdgeOfOblivion.Source, Is.EqualTo(FruMitigation.FragmentOfFate));
        Assert.That(FruMitigation.FragmentOfFate, Is.Not.EqualTo(FruMitigation.Usurper).And.Not.EqualTo(FruMitigation.Oracle));
    }
}
