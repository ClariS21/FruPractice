using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P5Full;
using static AnoMech.Core.Game.Party.PartyRole;
using ActionId = AnoMech.Scenarios.Umad.UmadConstants.ActionId;

namespace AnoMech.Tests;

public class UmadP5FidelityTests
{
    // Set 1's orange stands on (0, 13.5), as in the full run's tests.
    private static void PinForsaken(UmadP5FullStateOverrides o)
    {
        o.ForsakenMarker = RegenHealer;
        o.ForsakenSide = 1;
        o.ForsakenPick = 0;
    }

    // The logs name only who the stack hit (the whole role), never who carried it, and the casts have
    // no target: retail's pick can't be read from them, so each round's healer and DPS stay random.
    [Test]
    public void FellForcesPicksEitherHealerAndAnyDpsAcrossSeeds()
    {
        var healers = new HashSet<PartyRole>();
        var dps = new HashSet<PartyRole>();
        for (var seed = 0; seed < 80; seed++)
        {
            var state = new UmadP5FullState(null!, null!, null!, new Rng(seed), new UmadP5FullStateOverrides(new object(), new object(), new object()));
            foreach (var roll in state.FellForces)
            {
                healers.Add(roll.Healer);
                dps.Add(roll.Dps);
            }
        }
        Assert.Multiple(() =>
        {
            Assert.That(healers, Is.EquivalentTo([RegenHealer, ShieldHealer]));
            Assert.That(dps, Is.EquivalentTo([MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps]));
        });
    }

    // Both bars end 4.7 s after the drop; the damage follows the ActionEffect (0.3 s later) by 0.63 s.
    [Test]
    public void ForsakenPuddlesLandAboutAFullSecondAfterTheirBarEnds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UmadActions.ForsakenGround.Timing.DamageDelay, Is.EqualTo(0.93f).Within(0.01f));
            Assert.That(UmadActions.ForsakenOrange.Timing.DamageDelay, Is.EqualTo(0.93f).Within(0.01f));
        });
        var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 3, new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            WriteArtifactsOnFailure = false,
            Takeovers = [new Takeover(160f, null)],
            Overrides = o => PinForsaken((UmadP5FullStateOverrides)o),
        });
        var death = run.Deaths.Single(d => d.ActionId == ActionId.ForsakenOrange);
        var lands = UmadP5Forsaken.PuddlesAt[0] + UmadP5Forsaken.PuddlesLandAfter + UmadActions.ForsakenOrange.Timing.DamageDelay;
        Assert.That(death.Time, Is.InRange(lands, lands + 0.4f), run.ToString);
    }

    // The orange's circle is the sheet's 8 y: just inside kills, just outside lives.
    [Test]
    public void TheOrangeKillsInsideItsEightYardRadiusAndSparesOutside()
    {
        var spot = new System.Numerics.Vector2(0f, 13.5f);
        bool Dies(float distance) => ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 3, new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            WriteArtifactsOnFailure = false,
            Takeovers = [new Takeover(164.2f, spot + new System.Numerics.Vector2(distance, 0f))],
            Overrides = o => PinForsaken((UmadP5FullStateOverrides)o),
        }).Deaths.Any(d => d.ActionId == ActionId.ForsakenOrange);
        Assert.Multiple(() =>
        {
            Assert.That(Dies(7.8f), Is.True, "0.2 y inside");
            Assert.That(Dies(8.5f), Is.False, "0.5 y outside");
        });
    }

    [Test]
    public void ForsakenNullDrillRunsWithABotPartyThatSurvives()
    {
        Assert.That(new UmadP5ForsakenNull().AiStrats, Has.Count.EqualTo(1));
        var run = ScenarioRun.Execute(typeof(UmadP5ForsakenNull), 0, 17);
        Assert.That(run.Passed, Is.True, run.ToString);
    }
}
