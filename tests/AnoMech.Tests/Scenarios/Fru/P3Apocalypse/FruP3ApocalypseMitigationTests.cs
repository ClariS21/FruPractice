using AnoMech.Scenarios.Fru.P3Apocalypse;

namespace AnoMech.Tests;

public class FruP3ApocalypseMitigationTests
{
    [Test]
    public void ThePlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP3ApocalypseMitigation.Plan(), "Apocalypse");
    }
}
