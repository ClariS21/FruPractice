using AnoMech.Scenarios.Fru.P4CrystallizeTime;

namespace AnoMech.Tests;

public class FruP4CrystallizeTimeMitigationTests
{
    [Test]
    public void ThePlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP4CrystallizeTimeMitigation.Plan(), "Crystallize Time");
    }
}
