using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Tests;

public class FruP5ParadiseRegainedMitigationTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void TheRoundsPlanHoldsForEveryComp(int round)
    {
        MitigationPlanAssert.IsSound(FruP5ParadiseRegainedMitigation.Plan(round), $"Paradise Regained {round + 1}");
    }
}
