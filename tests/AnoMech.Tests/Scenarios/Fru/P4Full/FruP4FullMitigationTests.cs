using AnoMech.Scenarios.Fru.P4Full;

namespace AnoMech.Tests;

public class FruP4FullMitigationTests
{
    [Test]
    public void TheChainedPlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP4FullScenario.MitigationPlan(), "Full P4");
    }
}
