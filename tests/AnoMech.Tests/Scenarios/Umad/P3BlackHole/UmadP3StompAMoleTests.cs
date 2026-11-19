using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// Stomp-a-Mole's towers go west then east in Kefka's frame, twice. The role group the first Knock Down
// marker is not on soaks the first pair, the other the second, each by light party: the Exdeath tank's
// (with H2, M2 and R2) on the first-hit west tower, the Chaos tank's (H1, M1, R1) on the east one.
public class UmadP3StompAMoleTests
{
    private const float DrillStart = 125.72f;
    private static readonly float[] SnapshotsAt = [151.91f, 153.20f, 154.49f, 155.83f];

    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public void TheExdeathTanksLightPartyTakesTheFirstHitTower(bool chaosOnTheOffTank, bool supportsStackFirst)
    {
        var offset = chaosOnTheOffTank ? DrillStart : 0f;
        var soakers = new List<PartyRole[]>();
        var tanks = UmadP3Tanks.MainOnChaos;
        var run = ScenarioRun.Execute(chaosOnTheOffTank ? typeof(UmadP3FullScenario) : typeof(UmadP3BlackHoleScenario), 0, 4, new ScenarioRunOptions
        {
            StopAt = offset + 157f,
            PlayerRole = CasterDps,
            JobOverrides = chaosOnTheOffTank ? new Dictionary<PartyRole, JobId> { [MainTank] = JobId.Gunbreaker, [OffTank] = JobId.DarkKnight } : null,
            Overrides = o =>
            {
                var blackHole = o is UmadP3FullStateOverrides f ? f.BlackHoleRolls : (UmadP3BlackHoleStateOverrides)o;
                if (o is UmadP3FullStateOverrides fullRolls) UmadP3FullScenarioTests.Pin(fullRolls);
                else UmadP3BlackHoleScenarioTests.Pin(blackHole);
                blackHole.StackTargets = supportsStackFirst ? [ShieldHealer, PhysRangedDps] : [PhysRangedDps, ShieldHealer];
            },
            Probe = p =>
            {
                if (p.Crossed(offset + 1f) && chaosOnTheOffTank) tanks = UmadP3Tanks.For(p.World.Party);
                for (var k = 0; k < SnapshotsAt.Length; k++)
                {
                    if (!p.Crossed(offset + SnapshotsAt[k] - 0.05f)) continue;
                    var tower = new Vector3(k % 2 == 0 ? -10f : 10f, 0f, 0f);
                    soakers.Add([.. PerRole.All.Where(r => Vector3.Distance(p.Member(r)!.Position, tower) < 5f)]);
                }
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(tanks, Is.EqualTo(chaosOnTheOffTank ? new UmadP3Tanks(OffTank, MainTank) : UmadP3Tanks.MainOnChaos));
        PartyRole[] westSupports = [tanks.Exdeath, ShieldHealer], eastSupports = [tanks.Chaos, RegenHealer];
        PartyRole[] westDps = [MeleeDpsB, CasterDps], eastDps = [MeleeDpsA, PhysRangedDps];
        var expected = supportsStackFirst ? new[] { westDps, eastDps, westSupports, eastSupports } : [westSupports, eastSupports, westDps, eastDps];
        Assert.That(soakers, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            for (var k = 0; k < 4; k++)
                Assert.That(soakers[k], Is.EquivalentTo(expected[k]), $"{(k % 2 == 0 ? "west" : "east")} tower {k / 2 + 1}");
        });
    }
}
