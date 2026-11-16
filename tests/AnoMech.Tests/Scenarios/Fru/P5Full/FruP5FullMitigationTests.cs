using AnoMech.Scenarios.Fru.P5Full;

namespace AnoMech.Tests;

public class FruP5FullMitigationTests
{
    [Test]
    public void TheChainedPlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP5FullScenario.MitigationPlan(), "Full P5");
    }
}
