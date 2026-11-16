using AnoMech.Scenarios.Fru.P3UltimateRelativity;

namespace AnoMech.Tests;

public class FruP3UltimateRelativityMitigationTests
{
    [Test]
    public void ThePlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP3UltimateRelativityMitigation.Plan(), "Ultimate Relativity");
    }
}
