using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Tests;

// In game the human walks their own seat, which the AI never drives. Their reaction spots still go on their trail the
// moment the reaction happens, for the guide and the death recap: the early soak once their Aero catches a head, and the
// spot after the puddle once their Water is cleansed. Here the stand-in walks the seat up to the reaction, then the
// "human" takes it over: an Aero where the stand-in stands, a Water stepping into the puddle the guide just sent it to.
public class FruP4CrystallizeTimeHumanTrailTests
{
    private const PartyRole Human = PartyRole.CasterDps;

    [TestCase(CrystallizeDebuff.RedAero, StatusId.Wyrmclaw, 1)]
    [TestCase(CrystallizeDebuff.RedAero, StatusId.Wyrmclaw, 2)]
    [TestCase(CrystallizeDebuff.BlueWater, StatusId.Wyrmfang, 1)]
    [TestCase(CrystallizeDebuff.BlueWater, StatusId.Wyrmfang, 2)]
    public void TheHumansReactionSpotGoesOnTheirTrailAsItHappens(CrystallizeDebuff debuff, ushort status, int seed)
    {
        float? tookOverAt = null, lostAt = null;
        StratTarget? noted = null;
        var run = ScenarioRun.Execute(typeof(FruP4CrystallizeTimeScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = Human,
            WriteArtifactsOnFailure = false,
            StopAt = 45f,
            Overrides = o => ((FruP4CrystallizeTimeStateOverrides)o).PlayerDebuffs = [debuff],
            Probe = p =>
            {
                var human = p.Member(Human)!;
                if (tookOverAt is null && human.HasStatus(status) && TakeOverAt(p, human, debuff) is { } spot)
                {
                    DebugBotControl.Enabled = false;
                    human.SetPosition(spot);
                    tookOverAt = p.Time;
                }
                if (tookOverAt is not null && lostAt is null && !human.HasStatus(status)) lostAt = p.Time;
                if (lostAt is { } lost && noted is null)
                    noted = p.World.Strat.Entries.LastOrDefault(s => s.Role == Human && s.IssuedAt >= lost - 0.001f);
            },
        });
        Assert.That(tookOverAt, Is.Not.Null, $"the stand-in never got to its spot: {run}");
        Assert.That(lostAt, Is.Not.Null, $"the human, standing where the stand-in left them at {tookOverAt:F2}, never lost {status}: {run}");
        Assert.That(noted, Is.Not.Null, "no reaction spot on the human's trail");
        Assert.That(noted!.IssuedAt - lostAt!.Value, Is.LessThanOrEqualTo(0.1f), "the reaction spot went on the trail late");
    }

    // An Aero takes over on its catch spot as the head comes; a Water as soon as its trail points at a dropped puddle.
    private static Vector3? TakeOverAt(ScenarioProbe p, SimCharacter human, CrystallizeDebuff debuff)
    {
        if (p.World.Strat.Entries.LastOrDefault(s => s.Role == Human) is not { } latest) return null;
        if (debuff == CrystallizeDebuff.RedAero) return p.Time >= 29.5f && Flat(human.Position, latest.Spot) < 0.3f ? human.Position : null;
        var state = ((FruP4CrystallizeTimeScenario)p.Game.ActiveScenario!).State;
        return state.DragonPuddles.Values.Any(puddle => Flat(latest.Spot, puddle) < 0.05f) ? latest.Spot : null;
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
