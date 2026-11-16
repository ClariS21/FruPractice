using AnoMech.Scenarios.Fru.P5FulgentBlade;

namespace AnoMech.Tests;

public class FruP5FulgentBladeMitigationTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void TheRoundsPlanHoldsForEveryComp(int round)
    {
        MitigationPlanAssert.IsSound(FruP5FulgentBladeMitigation.Plan(round), $"Fulgent Blade {round + 1}");
    }
}
