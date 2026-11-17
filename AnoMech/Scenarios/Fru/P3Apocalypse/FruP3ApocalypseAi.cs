using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruConstants;
using static AnoMech.Scenarios.Fru.P3Apocalypse.FruP3ApocalypseState;

namespace AnoMech.Scenarios.Fru.P3Apocalypse;

public sealed class FruP3ApocalypseAi : IScenarioAi<FruP3ApocalypseState>
{
    public string Name => "NA priority / static spreads / OT bait";
    public string Group => "NA";

    private const float Scale = 20f / 47.4f;
    // The party takes Darkest Dance's knockback hugging the Oracle, inside its hitbox, so it lands short of the arena edge.
    private const float HugDistance = 1.0f;
    private const float HugOfHitbox = 0.5f;
    // The Oracle lands this short of its farthest target: mirrors the scenario's DanceLandingShort.
    private const float OracleLandsShort = 6.3f;
    // sqrt(10.5^2 - 4^2): with each group 4 y off the inward line, each stack sits 10.5 y from the Oracle (user's call).
    private const float ReturnStackDistance = 9.71f;
    // A helper's ability resolves a frame or two after the time it is scheduled at.
    private const float Hit = 0.05f;
    // Darkest Dance picks the farthest player, and the Oracle's landing, at this time.
    private const float DarkestDanceHitAt = 44f;

    // Wave 2 comes 1.3 s after the eruption that holds the party on its spread spots, which leaves 0.35 s of sprint
    // before the margin: the first yalms of the walk are all that can be asked to clear the blast, so the move goes at once.
    private const float PostEruptionExit = 2.5f;
    // From an outer spot to near the centre, out of the spoke blast (leaving it takes under 9 y); a flexer crossing over from an inner spot walks 8.4 y.
    private const float LeaveSpokeZone = 7.5f;
    private static float Danger(int wave) => WaveAt(wave) + ApocalypseHazards.SnapshotOffset;

    private FruP3ApocalypseState state = null!;
    private SimWorld world = null!;
    private readonly int[] slots = new int[8];

    public void Run(FruP3ApocalypseState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        AdjustWaterGroups();
        var ai = new AiManager(world);
        // Every move is scheduled when its spot becomes known (knownAt) and declares the hit it must beat, taken
        // from the scenario's timeline. The human-like layer picks each bot's departure, path and settle spot.
        // (jitter is kept where there was one: it only places the human seat of a test and Last moment bots.)
        ai.Move(2.2f, () => ByRole(role => Setup(role, adjusted: false)), knownAt: 2.2f, knownBy: CueKind.Cast, room: 1f, arrivalTime: 12.3f);
        // The flex swap: whoever Dark Water moved to the other group goes to its new slot until the collapse.
        ai.Move(WaterAppliedAt, () => ByRole(role => Slot(role) == Slot(role, adjusted: false) ? null : Setup(role)),
            knownAt: WaterAppliedAt, knownBy: CueKind.Debuff, room: 1f);
        // Apocalypse starts casting: each group collapses onto its own stack for the first Dark Water.
        ai.Move(15.3f, () => ByRole(FirstStack), knownAt: 15.3f, knownBy: CueKind.Cast, room: 1.5f, jitter: 0.3f, arrivalTime: WaterAt(1));
        // Spirit Taker knocks back anyone within 5 y of its target and the spots are 6.8 y apart: a tight room.
        // Spread as soon as the stack has gone off.
        ai.Move(WaterAt(1) + Hit, () => ByRole(role => Setup(role, spread: true)),
            knownAt: WaterAt(1) + Hit, knownBy: CueKind.Resolve, room: 0.5f, jitter: 0.3f, arrivalTime: 24.8f);
        // Only the flexers have a different spread spot to go back to.
        ai.Move(24.85f, () => ByRole(role => Slot(role) == Slot(role, adjusted: false) ? null : Setup(role, adjusted: false, spread: true)),
            knownAt: 24.85f, knownBy: CueKind.Resolve, room: 0.5f, jitter: 0.3f);
        // The Oracle repositions; the centre goes off first, so the spread spots near it are left before wave 0.
        ai.Move(27.6f, () => ByRole(Spread), knownAt: 27.6f, knownBy: CueKind.Cast, room: 0.3f, jitter: 0.3f, arrivalTime: Danger(0));
        ai.Move(36f + Hit, () => ByRole(PostEruption), knownAt: 36f + Hit, knownBy: CueKind.Resolve, room: 0.5f, jitter: 0.3f,
            outBy: Danger(2), exitDistance: PostEruptionExit);
        // The outer spots lie on the spoke pair that wave 3 lights, so they are left before it. The inner spots,
        // and this stack, are never hit after the centre's two waves, so no entry has to wait.
        // Whoever already stands on its stack (an inner spot, no flex) has nothing to do.
        ai.Move(37f, () => ByRole(role => Vector3.Distance(PostEruption(role)!.Value, SecondStack(role)!.Value) < 0.1f ? null : SecondStack(role)),
            knownAt: 37f, knownBy: CueKind.Cast, room: 0.5f, jitter: 0.3f, outBy: Danger(3), exitDistance: LeaveSpokeZone);
        // The off tank baits Darkest Dance far out, on the ray its old near spot lay on. It can leave the stack no earlier
        // than the water, and the spot lies in a spoke's blast until wave 5 has gone off, so only an immediate sprint
        // (a Move-style departure, not a habit one) clears the waves on the way and reaches it for the Oracle's pick at 44 s.
        ai.Move(WaterAt(2) + Hit, () => ByRole(role => role == PartyRole.OffTank ? TankBait(near: false) : null),
            jitter: 0.3f, arrivalTime: DarkestDanceHitAt, departureDelay: _ => 0f);
        // The rest stand in the middle, so the off tank is the farthest.
        ai.Move(WaterAt(2) + Hit, () => ByRole(role => role == PartyRole.OffTank ? null : Vector3.Zero),
            knownAt: WaterAt(2) + Hit, knownBy: CueKind.Resolve, room: 2f, jitter: 0.3f, arrivalTime: DarkestDanceHitAt);
        // The Oracle lands next to the farthest player at DarkestDanceHitAt; the party runs to it at once and hugs it.
        ai.Move(DarkestDanceHitAt, () => ByRole(KnockbackStack), knownAt: DarkestDanceHitAt, knownBy: CueKind.Resolve, room: 0.4f, jitter: 0.3f,
            arrivalTime: DarkestDanceKnockbackCastAt,
            cue: StratCue.ForAll("Darkest Dance knockback", "Hug the boss: stand inside its hitbox for the knockback", "NAUR 1.3"));
        world.Events.Add(DarkestDanceLandsAt, GapCloseAfterKnockback);
        // The knockback has thrown the party across the arena; the last Dark Water stacks on the Oracle's far side.
        ai.Move(DarkestDanceLandsAt, () => ByRole(ReturnStack), knownAt: DarkestDanceLandsAt, knownBy: CueKind.Resolve, room: 0.8f, jitter: 0.3f,
            arrivalTime: WaterAt(3));
    }

    private void GapCloseAfterKnockback()
    {
        var oracle = world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.OracleP3);
        if (oracle == null) return;
        foreach (var role in new[] { PartyRole.MainTank, PartyRole.OffTank, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB })
            (world.Party.Get(role) as SimPartyNpc)?.GapClose(oracle, sprintInWhenFar: false);
    }

    private void AdjustWaterGroups()
    {
        for (var i = 0; i < slots.Length; i++) slots[i] = i;
        var supports = Adjusters(0);
        var dps = Adjusters(4);
        for (var i = 0; i < supports.Length; i++)
            (slots[supports[i]], slots[dps[i]]) = (slots[dps[i]], slots[supports[i]]);
    }

    private int[] Adjusters(int start) => Enumerable.Range(0, 4)
        .Where(duration => Enumerable.Range(start, 4).Count(i => state.WaterDurations[i] == duration) == 2)
        .Select(duration => Enumerable.Range(start, 4).First(i => state.WaterDurations[i] == duration))
        .ToArray();

    private int Slot(PartyRole role, bool adjusted = true) => adjusted ? slots[(int)role] : (int)role;
    private bool SupportGroup(PartyRole role) => Slot(role) < 4;

    private float SpreadRotation => (state.Clockwise ? new[] { -45, 0, -135, -90 } : new[] { -135, -90, -45, 0 })[state.Rotation];

    private static Vector3 Reference(float x, float z, float degrees = 0)
    {
        var angle = degrees * MathF.PI / 180;
        return new Vector3(x * MathF.Sin(angle) + z * MathF.Cos(angle), 0, -x * MathF.Cos(angle) + z * MathF.Sin(angle)) * Scale;
    }

    private Vector3? Setup(PartyRole role, bool adjusted = true, bool spread = false)
    {
        var slot = Slot(role, adjusted);
        var support = slot < 4;
        var local = slot % 4;
        if (!spread) return InitialSlots[slot];
        var x = (local % 2 == 0 ? 1 : -1) * (support ? 1 : -1) * 8;
        var z = (support ? -1 : 1) * (local < 2 ? 9 : 27);
        return Reference(x, z);
    }

    private static readonly Vector3[] InitialSlots =
    [
        new(-4.43f, 0, -1.95f), new(-4.25f, 0, 3.47f), new(-9.74f, 0, -2.51f), new(-8.74f, 0, 3.01f),
        new(6.22f, 0, 2.50f), new(6.10f, 0, -1.87f), new(9.73f, 0, 2.95f), new(9.85f, 0, -2.17f),
    ];

    // Each group collapses to the centre of its own setup spots, a little off the boss.
    private Vector3? FirstStack(PartyRole role)
    {
        var group = SupportGroup(role) ? InitialSlots[..4] : InitialSlots[4..];
        return new Vector3(group.Average(v => v.X), 0, group.Average(v => v.Z));
    }

    private Vector3? Spread(PartyRole role)
    {
        var slot = Slot(role, adjusted: false);
        var side = slot < 4 ? 1 : -1;
        var (x, z) = (slot % 4) switch
        {
            0 => state.Clockwise ? (23f, 0f) : (16.23f, 16.23f),
            1 => state.Clockwise ? (16.23f, -16.23f) : (23f, 0f),
            2 => (44f, 8f),
            _ => (44f, -8f),
        };
        return Reference(x * side, z * side, SpreadRotation);
    }

    private Vector3? PostEruption(PartyRole role)
    {
        var slot = Slot(role, adjusted: false);
        return Reference((slot < 4 ? 1 : -1) * (slot % 4 < 2 ? 10 : 33), 0, SpreadRotation);
    }

    private Vector3? SecondStack(PartyRole role) => Reference(SupportGroup(role) ? 10 : -10, 0, SpreadRotation);

    private Vector3 TankBait(bool near)
    {
        var angle = (state.Clockwise ? new[] { -45, 0, -135, -90 } : new[] { -45, 0, 45, 90 })[state.Rotation];
        if (Slot(PartyRole.OffTank) >= 4) angle += 180;
        return Reference(near ? 10 : 32, near ? -10 : -32, angle);
    }

    private SimEnemy? Oracle() => world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.OracleP3);

    private Vector3 OraclePosition() => Oracle()?.Position ?? Vector3.Zero;

    // Darkest Dance sends the Oracle to land OracleLandsShort short of the player farthest from it. That is worked
    // out in this very frame, before the Oracle moves, so the party can set off for where it will land.
    private Vector3 OracleLanding()
    {
        if (Oracle() is not { } boss) return Vector3.Zero;
        var target = world.Party.ActiveMembers().MaxBy(m => Vector3.DistanceSquared(m.JudgedPosition, boss.Position));
        if (target is null) return boss.Position;
        var offset = target.JudgedPosition - boss.Position;
        return offset.Length() > OracleLandsShort
            ? boss.Position + Vector3.Normalize(offset) * (offset.Length() - OracleLandsShort)
            : boss.Position;
    }

    // Hugging the Oracle: in its hitbox, so the knockback sends the party back across the arena, not out of it.
    private Vector3? KnockbackStack(PartyRole role)
    {
        var landing = OracleLanding();
        var inward = landing.LengthSquared() > 0.01f ? Vector3.Normalize(-landing) : Vector3.UnitZ;
        var angle = (SupportGroup(role) ? 22.5f : -22.5f) * MathF.PI / 180;
        var direction = new Vector3(inward.X * MathF.Cos(angle) - inward.Z * MathF.Sin(angle), 0,
            inward.X * MathF.Sin(angle) + inward.Z * MathF.Cos(angle));
        var distance = MathF.Min(HugDistance, (Oracle()?.HitboxRadius ?? HugDistance) * HugOfHitbox);
        return landing + direction * distance;
    }

    private Vector3? ReturnStack(PartyRole role)
    {
        var boss = OraclePosition();
        var inward = boss.LengthSquared() > 0.01f ? Vector3.Normalize(-boss) : Vector3.UnitZ;
        var right = new Vector3(inward.Z, 0, -inward.X);
        return boss + inward * ReturnStackDistance + right * (SupportGroup(role) ? -4 : 4);
    }

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
