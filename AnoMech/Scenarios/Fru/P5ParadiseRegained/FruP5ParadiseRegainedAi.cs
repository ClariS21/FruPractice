using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.P5ParadiseRegained.FruP5ParadiseRegainedState;

namespace AnoMech.Scenarios.Fru.P5ParadiseRegained;

public sealed class FruP5ParadiseRegainedAi : IScenarioAi<FruP5ParadiseRegainedState>
{
    public string Name => "H first / M1-R1 NW / M2-R2 NE; lines T-M-R-H";

    private FruP5ParadiseRegainedState state = null!;

    // The line is cast at LinesAt; its first hit snapshots 0.2 s later and the strike lands on the
    // players as the ActionEffect plays, 0.5 s after the cast. The echo repeats it, lethal at its snapshot.
    private const float LineEffectAfter = 0.5f;
    // A tower or a hit has decided who is in it at its snapshot, so its spot is free just after.
    private const float AfterSnapshot = 0.05f;
    // A line is 6 wide; leaving it from its middle is 3 y, plus a body.
    private const float LineExit = 3.5f;
    // Pandora locks onto the wing's bait here, from where the players stand.
    private const float WingBaitLocksAt = 12.4f;

    public void Run(FruP5ParadiseRegainedState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        Func<IAiMove> At(Func<PartyRole, Vector3?> spot) => () => ByRole(spot);

        // The strat's spots are exact for whoever the habit layer doesn't drive (LastMoment bots, and the
        // human's seat when a test drives it), so those moves say jitter: 0. Habit-timed bots settle
        // within `room` of the spot instead.
        // Open staging under the first tower, known from the cast.
        ai.Move(2f, At(StackUnderFirstTower), arrivalTime: 8.3f, stagingMove: true, room: 1.5f);
        // The wing's cast (8.3) gives the order and with it the first spots. The bait is whoever is
        // nearest or farthest from Pandora when she locks on (12.4), and the tanks stand where it makes
        // them the bait, so that is the deadline for everyone; the tower (15.7) and the cone come later.
        ai.Move(8.3f, At(role => state.Rotate(FirstWingSpot(role))), jitter: 0f, knownAt: 8.3f, knownBy: CueKind.Cast, room: 0.5f,
            arrivalTime: WingBaitLocksAt);
        // The first wing landing (the buster, 16.5) frees the second spots; its tower is at TowerHitAt(1).
        ai.Move(16.5f, At(role => state.Rotate(SecondWingSpot(role))), jitter: 0f, knownAt: 16.5f, knownBy: CueKind.Resolve, room: 0.5f,
            arrivalTime: TowerHitAt(1));
        // Polarizing Strikes preposition once the last tower has resolved (the soakers stay for it). It
        // is known from the previous phase; the cast (30.35) only confirms it.
        var towersDone = TowerHitAt(2) + 0.65f;
        ai.Move(towersDone, At(PolarizingPreposition), jitter: 0f, knownAt: towersDone, knownBy: CueKind.Resolve, room: 0.3f, arrivalTime: 30f);
        // The first bait spots: the tanks leave the preposition for them; the rest are already there.
        // The two nearest to Pandora are baited when the lines are aimed, so each bait's side and
        // depth, which the lines are aimed by, are tight.
        ai.Move(30.35f, At(role => LineSpot(role, 0, dodged: false)), jitter: 0f, knownAt: 30.35f, knownBy: CueKind.Cast, room: 0.3f,
            arrivalTime: LinesAimedAt(0));
        for (var round = 0; round < 4; round++)
        {
            var r = round;
            // Next round's baits gather where the last round's echo is about to land, so the spot is
            // dangerous until the echo has decided who is hit, and it is aimed 1.9 s after that.
            if (r > 0) ai.Move(EchoAt(r - 1), At(role => LineSpot(role, r, dodged: false)), jitter: 0f, knownAt: EchoAt(r - 1), knownBy: CueKind.Resolve,
                room: 0.3f, arrivalTime: LinesAimedAt(r), notBefore: EchoAt(r - 1) + AfterSnapshot);
            // The line's stack lands (CueKind.Resolve), and every bait leaves the line before its echo,
            // which is lethal wherever the line went.
            var lineLands = LinesAt(r) + LineEffectAfter;
            ai.Move(lineLands, At(role => LineSpot(role, r, dodged: true)), jitter: 0f, knownAt: lineLands, knownBy: CueKind.Resolve, room: 0.3f,
                outBy: EchoAt(r), exitDistance: LineExit);
        }
    }

    private static float LinesAimedAt(int round) => LinesAt(round) - 0.1f;

    private Vector3? StackUnderFirstTower(PartyRole role) => state.Rotate(new Vector3(((int)role - 3.5f) * 0.35f, 0f, 7f));

    private Vector3 FirstWingSpot(PartyRole role) => role switch
    {
        PartyRole.MainTank => new(state.DarkFirst ? -6.0622f : 6.0622f, 0f, -3.5f),
        PartyRole.OffTank => state.DarkFirst ? new(0f, 0f, 1f) : new(10f, 0f, 10f),
        PartyRole.RegenHealer => new(-0.65f, 0f, 7f),
        PartyRole.ShieldHealer => new(0.65f, 0f, 7f),
        PartyRole.MeleeDpsA => new(-3.5f, 0f, 9.5f),
        PartyRole.PhysRangedDps => new(-3.8f, 0f, 9.7f),
        PartyRole.MeleeDpsB => new(3.5f, 0f, 9.5f),
        _ => new(3.8f, 0f, 9.7f),
    };

    private Vector3 SecondWingSpot(PartyRole role) => role switch
    {
        PartyRole.MainTank => new(0f, 0f, state.DarkFirst ? -14f : -1f),
        PartyRole.OffTank => new(state.DarkFirst ? -6.0622f : 6.0622f, 0f, 3.5f),
        PartyRole.RegenHealer => new(-0.65f, 0f, -7.5f),
        PartyRole.ShieldHealer => new(0.65f, 0f, -7.5f),
        PartyRole.MeleeDpsA => new(-5f, 0f, -4.5f),
        PartyRole.PhysRangedDps => new(-5.2f, 0f, -4.7f),
        PartyRole.MeleeDpsB => new(5f, 0f, -4.5f),
        _ => new(5.2f, 0f, -4.7f),
    };

    private static int BaitOrder(PartyRole role) => role switch
    {
        PartyRole.MainTank or PartyRole.OffTank => 0,
        PartyRole.MeleeDpsA or PartyRole.MeleeDpsB => 1,
        PartyRole.PhysRangedDps or PartyRole.CasterDps => 2,
        _ => 3,
    };

    private static bool StartsWest(PartyRole role)
        => role is PartyRole.MainTank or PartyRole.RegenHealer or PartyRole.MeleeDpsA or PartyRole.PhysRangedDps;

    private static Vector3? PolarizingPreposition(PartyRole role)
        => BaitOrder(role) == 0 ? new Vector3(0f, 0f, -3.5f) : LineSpot(role, 0, dodged: false);

    private static Vector3? LineSpot(PartyRole role, int round, bool dodged)
    {
        var order = BaitOrder(role);
        var crossed = dodged ? order <= round : order < round;
        var sign = StartsWest(role) != crossed ? -1f : 1f;
        if (!dodged)
        {
            var depth = order == round ? 4f : 6f;
            return new Vector3(sign * depth, 0f, depth);
        }
        var next = order == Math.Min(round + 1, 3);
        return new Vector3(sign * (next ? 0.6f : 1.8f), 0f, next ? 6.2f : 8.5f);
    }

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
