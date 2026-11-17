using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Fru.FruConstants;
using static AnoMech.Scenarios.Fru.P4DarklitDragonsong.FruP4DarklitDragonsongState;

namespace AnoMech.Scenarios.Fru.P4DarklitDragonsong;

public sealed class FruP4DarklitDragonsongAi(bool playerBaitsSomberDance) : IScenarioAi<FruP4DarklitDragonsongState>
{
    public string Name => playerBaitsSomberDance ? "NA · you take Somber Dance" : "NA";
    public string Group => "NA";

    private const float S = 1f / 2.358f;
    private const float AkhRhaiAt = 6.6f;
    private const float AkhRhaiSnapshotAt = 8.8f;
    private const float LastAkhRhaiAt = 14.5f;
    private const float SwapAt = 28.2f;
    private const float PathOfLightAimedAt = 33f;
    private const float PathOfLightAt = 33.1f;
    private const float SpiritTakerAt = 35.6f;
    private const float HallowedWingsAt = 40f;
    private const float SomberDanceFarAt = 43.8f;
    private const float OracleLandsAt = 45f;
    private const float SomberDanceNearAt = 47f;
    private const float AkhMornAt = 57.7f;
    // Akh Rhai drops a puddle on every spot of the middle stack: clear of its own and its neighbours'.
    private const float AkhRhaiClearance = 5.5f;

    private FruP4DarklitDragonsongState state = null!;
    private SimWorld world = null!;
    private PartyRole[] swapped = [];
    private PartyRole somberTank;

    // The tank who takes Somber Dance this run.
    internal PartyRole SomberTank => somberTank;

    public void Run(FruP4DarklitDragonsongState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        swapped = SwapFreeWaterToItsGroup();
        somberTank = ChooseSomberTank();
        var ai = new AiManager(world);
        ai.Move(2f, () => ByRole(Middle), arrivalTime: AkhRhaiAt, stagingMove: true, knownAt: 2f);
        ai.Move(AkhRhaiAt, () => ByRole(OpeningSpread), knownAt: AkhRhaiAt, outBy: AkhRhaiSnapshotAt, exitDistance: AkhRhaiClearance, room: 1f);
        ai.Move(LastAkhRhaiAt, () => ByRole(Lineup), knownAt: LastAkhRhaiAt, knownBy: CueKind.Resolve, arrivalTime: ChainsAt, room: 1f);
        ai.Move(ChainsAt, () => ByRole(role => Bowtie(role, state.LineupOrder.ToArray())), knownAt: ChainsAt, knownBy: CueKind.Debuff,
            arrivalTime: SwapAt, room: 0.5f);
        ai.Move(SwapAt, () => ByRole(FreeWaterSwap), jitter: 0.3f, knownAt: SwapAt, knownBy: CueKind.Debuff, arrivalTime: PathOfLightAimedAt, room: 0.5f);
        ai.Move(PathOfLightAt, () => ByRole(SpiritTakerSpread), knownAt: PathOfLightAt, knownBy: CueKind.Resolve, arrivalTime: SpiritTakerAt, room: 1f);
        ai.Move(SpiritTakerAt, () => ByRole(WaterStack), knownAt: SpiritTakerAt, knownBy: CueKind.Resolve, arrivalTime: HallowedWingsAt, room: 1f);
        // Explicit timing: this tank holds the Dark Water stack until it resolves, and the 26 y run to the far bait takes
        // 3.1 s even sprinting, so it leaves the moment the stack resolves and gets there 0.5 s before Somber Dance.
        ai.Move(DarkWaterAt, () => ByRole(role => role == somberTank ? FarBait() : null), arrivalTime: SomberDanceFarAt, departureDelay: _ => 0f);
        ai.Move(DarkWaterAt, () => ByRole(role => role == somberTank ? null : DanceParty(role)), knownAt: DarkWaterAt, knownBy: CueKind.Resolve,
            arrivalTime: SomberDanceFarAt, room: 1f);
        ai.Move(OracleLandsAt, () => ByRole(role => role == somberTank ? JustPastOracleFromCenter() : null), jitter: 0f, knownAt: OracleLandsAt,
            knownBy: CueKind.Resolve, arrivalTime: SomberDanceNearAt, room: 0.5f);
        ai.Move(SomberDanceNearAt, () => ByRole(AkhMorn), knownAt: SomberDanceNearAt, knownBy: CueKind.Resolve, arrivalTime: AkhMornAt, room: 1f);
    }

    private PartyRole[] SwapFreeWaterToItsGroup()
    {
        var after = state.LineupOrder.ToArray();
        if ((state.TetherWaterIndex < 2) == (state.FreeWaterIndex < 2))
            (after[4 + state.FreeWaterIndex], after[4 + 3 - state.FreeWaterIndex]) = (after[4 + 3 - state.FreeWaterIndex], after[4 + state.FreeWaterIndex]);
        return after;
    }

    private PartyRole ChooseSomberTank()
    {
        var player = world.Party.PlayerRole;
        if (playerBaitsSomberDance && player.IsTank()) return player;
        var mainJob = (JobId)((world.Party.Get(PartyRole.MainTank) as ISimPartyMember)?.ClassJob ?? 0);
        var offJob = (JobId)((world.Party.Get(PartyRole.OffTank) as ISimPartyMember)?.ClassJob ?? 0);
        return FruP4TankPair.SomberBaiter(mainJob, offJob);
    }

    private Vector3 OraclePosition()
        => world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.OracleP4)?.Position ?? Vector3.Zero;

    private Vector3 JustPastOracleFromCenter()
    {
        var oracle = OraclePosition();
        return oracle.LengthSquared() > 0.01f ? oracle + Vector3.Normalize(oracle) * 2f : oracle;
    }

    private Vector3 FarBait()
    {
        var x = OraclePosition().X;
        var baitEast = x < -4f * S || MathF.Abs(x) < 4f * S && state.EastWing;
        return FromSim(0, baitEast ? 43 : -43);
    }

    private static Vector3 FromSim(float north, float east) => new(east * S, 0, -north * S);

    private static Vector3 Quadrant(int index, float north, float east)
        => FromSim(index % 4 < 2 ? north : -north, index % 4 is 1 or 2 ? east : -east);

    private int Index(PartyRole role) => Array.IndexOf(swapped, role);
    private bool North(PartyRole role) => Index(role) % 4 < 2;

    private static Vector3? Middle(PartyRole role) => FromSim((int)role % 2 * 0.3f, (int)role * 0.08f);

    private static Vector3? OpeningSpread(PartyRole role) => role switch
    {
        PartyRole.MainTank => FromSim(16.4f, 10.1f),
        PartyRole.OffTank => FromSim(10.1f, 16.4f),
        PartyRole.RegenHealer => FromSim(10.1f, -16.4f),
        PartyRole.ShieldHealer => FromSim(16.4f, -10.1f),
        PartyRole.MeleeDpsA => FromSim(-16.4f, 10.1f),
        PartyRole.MeleeDpsB => FromSim(-10.1f, 16.4f),
        PartyRole.PhysRangedDps => FromSim(-10.1f, -16.4f),
        _ => FromSim(-16.4f, -10.1f),
    };

    private static Vector3? Lineup(PartyRole role) => role switch
    {
        PartyRole.MainTank => FromSim(4.5f, 5),
        PartyRole.OffTank => FromSim(0.2f, 11.3f),
        PartyRole.RegenHealer => FromSim(0.2f, -11.3f),
        PartyRole.ShieldHealer => FromSim(4.5f, -5),
        PartyRole.MeleeDpsA => FromSim(-25.5f, 5),
        PartyRole.MeleeDpsB => FromSim(-21.2f, 11.3f),
        PartyRole.PhysRangedDps => FromSim(-21.2f, -11.3f),
        _ => FromSim(-25.5f, -5),
    };

    private static Vector3? Bowtie(PartyRole role, PartyRole[] order)
    {
        var index = Array.IndexOf(order, role);
        return index < 4 ? Quadrant(index, 19, 5.2f) : Quadrant(index, 6.7f, 14.6f);
    }

    private Vector3? FreeWaterSwap(PartyRole role)
        => Array.IndexOf(swapped, role) == state.LineupOrder.ToList().IndexOf(role) ? null : Bowtie(role, swapped);

    private Vector3? SpiritTakerSpread(PartyRole role)
    {
        var index = Index(role);
        if (index < 4) return Quadrant(index, 19, 17.2f);
        var west = index is 4 or 7;
        return FromSim(0, west ? ((int)role < 4 ? -38 : -19) : ((int)role < 4 ? 19 : 0));
    }

    private Vector3? WaterStack(PartyRole role)
        => FromSim(North(role) ? 17.3f : -17.3f, state.EastWing ? -15.5f : 15.5f) + Middle(role)!.Value * 0.3f;

    private Vector3? DanceParty(PartyRole role) => FromSim(North(role) ? 15 : -15, 0) + Middle(role)!.Value * 0.3f;

    private static Vector3? AkhMorn(PartyRole role) => role == PartyRole.MainTank ? new Vector3(-9.3f, 0, 0) : Vector3.Zero;

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
