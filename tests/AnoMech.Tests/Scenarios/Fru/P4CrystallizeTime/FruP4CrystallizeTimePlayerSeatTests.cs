using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4Full;

namespace AnoMech.Tests;

// Seeds where the player seat's rewind move waited for its latest departure. That departure was worked out from where the
// stand-in stood, but the stand-in kept walking its after-cleanse order away from the rewind spot in the meantime. It got there
// after the return was saved, and Hallowed Wings caught it where the rewind carried it back to. The move also went on the seat's
// trail only when it set off, so the path guidance and the death recap had the Hallowed Wings spot late.
public class FruP4CrystallizeTimePlayerSeatTests
{
    // The rewind step comes 4.25 s before the return is saved.
    private const float RewindStepAhead = 4f;
    private const float OnSpot = 1f;

    [TestCase(typeof(FruP4CrystallizeTimeScenario), 0, 1428128475, 0f)]
    [TestCase(typeof(FruP4FullScenario), 0, 1492080477, FruP4FullScenario.CrystallizeTimeAt)]
    public void ThePlayerSeatHasItsRewindSpotFromTheRewindStepAndStandsOnItWhenTheReturnIsSaved(Type scenario, int strat, int seed,
        float crystallizeTimeAt)
    {
        var saved = crystallizeTimeAt + FruP4CrystallizeTimeState.ReturnSavedAt;
        StratTarget? rewind = null;
        var offBy = float.PositiveInfinity;
        var run = ScenarioRun.Execute(scenario, strat, seed, new ScenarioRunOptions
        {
            WriteArtifactsOnFailure = false,
            Probe = p =>
            {
                if (!p.Crossed(saved)) return;
                var role = p.World.Party.PlayerRole;
                rewind = p.World.Strat.Entries.LastOrDefault(s => s.Role == role);
                if (rewind is not null && p.Member(role) is { } player)
                    offBy = Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(rewind.Spot.X, rewind.Spot.Z));
            },
        });
        Assert.Multiple(() =>
        {
            Assert.That(run.Passed, run.ToString);
            Assert.That(rewind, Is.Not.Null, "nothing on the player seat's trail by the time the return is saved");
            Assert.That(saved - rewind?.IssuedAt, Is.GreaterThan(RewindStepAhead), "the rewind spot went on the trail late");
            Assert.That(offBy, Is.LessThan(OnSpot), "off the rewind spot when the return was saved");
        });
    }
}
