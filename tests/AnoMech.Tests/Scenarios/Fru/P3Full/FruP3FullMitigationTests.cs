using AnoMech.Scenarios.Fru.P3Full;

namespace AnoMech.Tests;

public class FruP3FullMitigationTests
{
    [Test]
    public void TheChainedPlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP3FullScenario.MitigationPlan(), "Full P3");
    }
}
