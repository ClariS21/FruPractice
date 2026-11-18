using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P3BlackHole;
using static AnoMech.Scenarios.Umad.P3Full.UmadP3Opening;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P3Full;

public sealed class UmadP3OpeningAi
{
    private const string Fae = "Fae doc";
    private const string YourGroup = "your group's pulls";
    private const float Tight = 0.3f;
    private const float ImplosionLean = 0.35f;
    private const float StackImplosionDistance = 6f;
    private const float RangedImplosionDistance = 7f;

    private UmadP3OpeningState state = null!;
    private UmadP3Cast cast = null!;
    private SimWorld world = null!;
    private Placement? implosionChaos;

    internal void Run(UmadP3OpeningState stateParam, UmadP3Cast castParam, SimWorld worldParam)
    {
        state = stateParam;
        cast = castParam;
        world = worldParam;
        implosionChaos = null;
        var ai = new AiManager(world);

        ai.Move(0f, DecisiveBattleGroups, jitter: Tight, knownAt: 0f, room: 1f, arrivalTime: 2.9f,
            cue: StratCue.ForAll("The Decisive Battle", "Chaos tank, H1 and the melee by Chaos; Exdeath tank, H2 and the ranged by Exdeath", YourGroup));
        ai.Move(4.5f, TanksWalkPastTheMiddleSoTheBossesStopThere, jitter: Tight, stagingMove: true, knownAt: 3.15f, room: Tight, arrivalTime: 12f);
        ai.Move(23.2f, UptimeBeforeTheBlast, jitter: Tight, knownAt: BowelsResolvesAt, knownBy: CueKind.Debuff, room: Tight, arrivalTime: 30f,
            cue: StratCue.ForAll("Bowels of Agony", "Exdeath stays in the middle; uptime on the wind side of him", YourGroup));
        ai.Move(26.5f, ChaosToTheWindWall, jitter: Tight, knownAt: BowelsResolvesAt, knownBy: CueKind.Debuff, room: 0.5f, arrivalTime: 33.5f,
            cue: StratCue.ForAll("Bowels of Agony", "The Chaos tank drags Chaos to the wall at the wind crystal", YourGroup));
        ai.Move(34.9f, () => ElementSpots(state.ShortElement, ranged: null), jitter: Tight, knownAt: ThunderBlastAt, room: Tight, arrivalTime: 41.0f,
            cue: ElementCue(state.ShortElement));
        ai.Move(43.6f, EntropySpreadersBackToTheStack, jitter: Tight, knownAt: ShortCrystalAt, knownBy: CueKind.Resolve, room: 0.5f, arrivalTime: 46f);
        ai.Move(45.95f, FirstHitTankOnExdeathOtherWaitsByChaos, jitter: Tight, knownAt: 45.89f, room: 0.5f, arrivalTime: ThunderHitsAt[0] - 0.6f,
            cue: StratCue.ForAll("Thunder III", "The first hit's tank stands on Exdeath; the other waits by Chaos", YourGroup));
        ai.Move(ThunderHitsAt[0] + 0.1f, TanksTradePlacesOnExdeath, jitter: Tight, knownAt: ThunderHitsAt[0], knownBy: CueKind.Resolve, room: 0.5f,
            arrivalTime: ThunderHitsAt[1] - 0.6f, outBy: ThunderHitsAt[1] - 0.6f,
            cue: StratCue.ForAll("Thunder III", "The tanks trade places on Exdeath for the second hit", YourGroup));
        ai.Move(ThunderHitsAt[1] + 0.2f, TanksBackToTheWall, jitter: Tight, knownAt: ThunderHitsAt[1], knownBy: CueKind.Resolve, room: 0.5f,
            arrivalTime: 57.5f, cue: StratCue.ForAll("Thunder III", "The Exdeath tank drags Exdeath to the wall", YourGroup));
        ai.Move(ImplosionAt + 0.1f, () => DiagonalsLeaningOutOfTheCones(0), jitter: Tight, knownAt: ImplosionAt, room: Tight, arrivalTime: ShockwavesAt[0] - 0.3f,
            cue: ImplosionCue(0));
        ai.Move(ShockwavesAt[0] + 0.08f, () => DiagonalsLeaningOutOfTheCones(1), jitter: Tight, knownAt: ShockwavesAt[0], knownBy: CueKind.Resolve, room: Tight,
            arrivalTime: ShockwavesAt[1] - 0.3f, notBefore: ShockwavesAt[0], cue: ImplosionCue(1));
        ai.Move(ShockwavesAt[1] + 0.1f, () => ElementSpots(state.LongElement, ranged: false), jitter: Tight, knownAt: ShockwavesAt[1],
            knownBy: CueKind.Resolve, room: 0.5f, arrivalTime: 68.3f, cue: ElementCue(state.LongElement));
        ai.Move(ShockwavesAt[1] + 0.1f, () => ElementSpots(state.LongElement, ranged: true), jitter: Tight, knownAt: ShockwavesAt[1],
            knownBy: CueKind.Resolve, room: 0.5f, arrivalTime: LongCrystalAt - 0.5f, cue: ElementCue(state.LongElement));
    }

    private PartyRole ChaosTank => state.Tanks.Chaos;
    private PartyRole ExdeathTank => state.Tanks.Exdeath;
    private static bool IsRanged(PartyRole role) => role is PartyRole.PhysRangedDps or PartyRole.CasterDps;

    private IAiMove DecisiveBattleGroups() => ByRole(role => role == ChaosTank ? new Vector2(-6.7f, 0f)
        : role == ExdeathTank ? new Vector2(4.9f, 0.2f)
        : role switch
        {
            PartyRole.RegenHealer => new Vector2(-4.5f, 3.5f),
            PartyRole.MeleeDpsA => new Vector2(-6f, -3f),
            PartyRole.MeleeDpsB => new Vector2(-6f, 3f),
            PartyRole.ShieldHealer => new Vector2(4.5f, 3.5f),
            PartyRole.PhysRangedDps => new Vector2(6f, -3f),
            _ => new Vector2(6.5f, 3f),
        });

    private IAiMove TanksWalkPastTheMiddleSoTheBossesStopThere() => ByRole(role => role == ChaosTank ? new Vector2(6.5f, 0f)
        : role == ExdeathTank ? new Vector2(-4.3f, 0f)
        : role switch
        {
            PartyRole.RegenHealer => new Vector2(-1.5f, 4f),
            PartyRole.ShieldHealer => new Vector2(1.5f, 4f),
            PartyRole.MeleeDpsA => new Vector2(-3f, 2.5f),
            PartyRole.MeleeDpsB => new Vector2(3f, 2.5f),
            PartyRole.PhysRangedDps => new Vector2(-1.5f, 7f),
            _ => new Vector2(1.5f, 7f),
        });

    private IAiMove UptimeBeforeTheBlast() => ByRole(role => role == ChaosTank ? null
        : role == ExdeathTank ? state.Wind(4.3f, 0f)
        : role switch
        {
            PartyRole.RegenHealer => state.Wind(5f, 1.5f),
            PartyRole.ShieldHealer => state.Wind(5f, -1.5f),
            PartyRole.MeleeDpsA => state.Wind(6f, 0.8f),
            PartyRole.MeleeDpsB => state.Wind(6f, -0.8f),
            PartyRole.PhysRangedDps => state.Wind(4.5f, 2.5f),
            _ => state.Wind(4.5f, -2.5f),
        });

    private IAiMove ChaosToTheWindWall() => ByRole(role => role == ChaosTank ? WindWallStackBehindChaos(role) : null);

    private Vector2 WindWallStackBehindChaos(PartyRole role) => role == ChaosTank ? state.Wind(18.2f, 0.5f)
        : role == ExdeathTank ? state.Wind(17.6f, 0.4f)
        : role switch
        {
            PartyRole.RegenHealer => state.Wind(17.3f, 0.7f),
            PartyRole.ShieldHealer => state.Wind(17.3f, -0.7f),
            PartyRole.MeleeDpsA => state.Wind(16.9f, 0.3f),
            _ => state.Wind(16.9f, -0.3f),
        };

    private Vector2 ElementSpot(PartyRole role, UmadP3Element element)
    {
        var side = state.Side(element);
        if (role == PartyRole.PhysRangedDps) return state.Wind(-7f, side * 16f);
        if (role == PartyRole.CasterDps) return state.Wind(7f, side * 17f);
        if (element == UmadP3Element.Fire && state.DebuffOf(role) == UmadP3Element.Fire)
            return role.IsDps() ? state.Wind(16f, -7f) : state.Wind(16f, 7f);
        return WindWallStackBehindChaos(role);
    }

    private IAiMove ElementSpots(UmadP3Element element, bool? ranged)
        => ByRole(role => ranged is { } only && only != IsRanged(role) ? null : ElementSpot(role, element));

    private IAiMove EntropySpreadersBackToTheStack()
        => ByRole(role => !IsRanged(role) && state.DebuffOf(role) == UmadP3Element.Fire ? WindWallStackBehindChaos(role) : null);

    private (PartyRole First, PartyRole? Second) ThunderRoles => ThunderIIIPlanning.Roles(state.Thunder);

    private IAiMove FirstHitTankOnExdeathOtherWaitsByChaos()
    {
        var (first, second) = ThunderRoles;
        return ByRole(role => role == first ? state.Wind(2.4f, 0f) : role == second ? state.Wind(12.2f, -0.1f) : null);
    }

    private IAiMove TanksTradePlacesOnExdeath()
    {
        var (first, second) = ThunderRoles;
        if (second is null) return ByRole(_ => null);
        return ByRole(role => role == second ? state.Wind(1.7f, 0.3f) : role == first ? state.Wind(14.1f, 0.1f) : null);
    }

    private IAiMove TanksBackToTheWall() => ByRole(role => role.IsTank() ? WindWallStackBehindChaos(role) : null);

    private IAiMove DiagonalsLeaningOutOfTheCones(int set)
    {
        if (cast.Chaos is not { } chaos) return ByRole(_ => null);
        var at = implosionChaos ??= chaos.Placement();
        var stack = UmadImplosion.NearestDiagonal(at, state.Implosion, state.Wind(16.5f, 6f));
        var ranged = UmadImplosion.NearestDiagonal(at, state.Implosion, state.Crystal(state.LongElement));
        return ByRole(role => IsRanged(role)
            ? UmadImplosion.SafeSpot(at, state.Implosion, set, ranged, RangedImplosionDistance + (role == PartyRole.CasterDps ? 1f : 0f), ImplosionLean)
            : UmadImplosion.SafeSpot(at, state.Implosion, set, stack, StackImplosionDistance, ImplosionLean));
    }

    private StratCue ElementCue(UmadP3Element element) => new(element == UmadP3Element.Fire ? "Stray Flames" : "Stray Spray", role =>
        IsRanged(role) ? $"Ranged on the {element} crystal, one either side, so its two hits find you"
        : element == UmadP3Element.Fire && state.DebuffOf(role) == UmadP3Element.Fire ? "Entropy: spread off the stack, supports left and DPS right facing the bosses"
        : "Stack on the wind crystal's wall behind Chaos", Fae);

    private StratCue ImplosionCue(int set) => StratCue.ForAll("Implosion",
        set == 0 ? "On the diagonal, leaning out of the first cones" : "Step across the diagonal, out of the second cones", YourGroup);

    private static IAiMove ByRole(Func<PartyRole, Vector2?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(at).ToArray()).NaturalOrder();
}
