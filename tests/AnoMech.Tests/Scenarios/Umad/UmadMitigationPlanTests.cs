using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P4Full;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;
using AnoMech.Scenarios.Umad.P5Full;

namespace AnoMech.Tests;

// Every DMU plan, built for each comp's own tank pair, holds for every seat and job the player can bring.
public class UmadMitigationPlanTests
{
    internal static UmadP3Tanks P3Tanks(IReadOnlyDictionary<PartyRole, JobId> jobs)
        => UmadP3Tanks.For(jobs[PartyRole.MainTank], jobs[PartyRole.OffTank]);

    internal static PartyRole FirstInvuln(IReadOnlyDictionary<PartyRole, JobId> jobs)
        => UmadP5TankPair.HolyTank(jobs[PartyRole.MainTank], jobs[PartyRole.OffTank], 0);

    // P3 Full's default: the group's swap, Chaos tank invuln, swap, Exdeath tank invuln, swap.
    internal static IReadOnlyList<ThunderIIIAssignment> DefaultBusters(UmadP3Tanks tanks)
    {
        var defaults = new UmadP3FullStateOverrides(new UmadP3LimitCutStateOverrides(), new UmadP3BlackHoleStateOverrides());
        return [.. new[] { defaults.Thunder1, defaults.Thunder2, defaults.Thunder3, defaults.Thunder4, defaults.Thunder5 }.Select(tanks.Seats)];
    }

    private const float LimitCutEnds = 56f;

    [TestCase(ThunderIIIAssignment.MtInvulnsBoth)]
    [TestCase(ThunderIIIAssignment.OtInvulnsBoth)]
    [TestCase(ThunderIIIAssignment.ShareMtFirst)]
    [TestCase(ThunderIIIAssignment.ShareOtFirst)]
    public void TheLimitCutPlanHoldsForEveryComp(ThunderIIIAssignment thunder)
        => MitigationPlanAssert.IsSound(jobs => UmadP3LimitCutMitigation.Plan(P3Tanks(jobs), thunder), $"Limit Cut ({thunder})", LimitCutEnds);

    [TestCase(ThunderIIIAssignment.MtInvulnsBoth, ThunderIIIAssignment.ShareMtFirst)]
    [TestCase(ThunderIIIAssignment.OtInvulnsBoth, ThunderIIIAssignment.ShareOtFirst)]
    [TestCase(ThunderIIIAssignment.ShareMtFirst, ThunderIIIAssignment.OtInvulnsBoth)]
    [TestCase(ThunderIIIAssignment.ShareOtFirst, ThunderIIIAssignment.ShareMtFirst)]
    public void TheBlackHolePlanHoldsForEveryComp(ThunderIIIAssignment first, ThunderIIIAssignment second)
        => MitigationPlanAssert.IsSound(jobs => UmadP3BlackHoleMitigation.Plan(P3Tanks(jobs), (first, second)), $"Black Hole ({first}, {second})");

    [Test]
    public void TheFullP3PlanHoldsForEveryComp()
        => MitigationPlanAssert.IsSound(jobs => UmadP3FullScenario.MitigationPlan(P3Tanks(jobs), DefaultBusters(P3Tanks(jobs))), "Full P3");

    // Every set shared: each tank's cooldowns alternate the sheet's two sets around their recasts.
    [Test]
    public void TheFullP3PlanHoldsWhenEverySetIsShared()
        => MitigationPlanAssert.IsSound(jobs => UmadP3FullScenario.MitigationPlan(P3Tanks(jobs),
            [.. Enumerable.Repeat(ThunderIIIAssignment.ShareMtFirst, 5)]), "Full P3, every set shared");

    [TestCase(false)]
    [TestCase(true)]
    public void TheKefkaSaysPlanHoldsForEveryComp(bool secondUpsurge)
        => MitigationPlanAssert.IsSound(_ => UmadP4KefkaSaysMitigation.Plan(secondUpsurge), $"Kefka Says (second Ultima Upsurge: {secondUpsurge})");

    [TestCase(HpCheckMode.Pass)]
    [TestCase(HpCheckMode.Fail)]
    public void TheFullP4PlanHoldsForEveryComp(HpCheckMode hpCheck)
        => MitigationPlanAssert.IsSound(_ => UmadP4FullScenario.MitigationPlan(hpCheck), $"Full P4 ({hpCheck})");

    [Test]
    public void TheFloodPlanHoldsForEveryComp() => MitigationPlanAssert.IsSound(_ => UmadP5FloodMitigation.Plan(), "Flood");

    [Test]
    public void TheCelestriadPlanHoldsForEveryComp()
        => MitigationPlanAssert.IsSound(jobs => UmadP5CelestriadMitigation.Plan(FirstInvuln(jobs)), "Celestriad");

    [Test]
    public void TheExaflaresPlanHoldsForEveryComp()
        => MitigationPlanAssert.IsSound(jobs => UmadP5ExaflaresMitigation.Plan(FirstInvuln(jobs)), "Exaflares");

    [Test]
    public void TheForsakenNullPlanHoldsForEveryComp()
        => MitigationPlanAssert.IsSound(jobs => UmadP5ForsakenNull.MitigationPlan(FirstInvuln(jobs)), "Forsaken Null");

    [Test]
    public void TheFullP5PlanHoldsForEveryComp()
        => MitigationPlanAssert.IsSound(jobs => UmadP5FullScenario.MitigationPlan(FirstInvuln(jobs)), "Full P5");
}
