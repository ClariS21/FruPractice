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
    private const float KnockbackStackDistance = 9.5f;
    // sqrt(10.5^2 - 4^2): with each group 4 y off the inward line, each stack sits 10.5 y from the Oracle (user's call).
    private const float ReturnStackDistance = 9.71f;
    private const float FlexDepartureEarliest = 1.5f;
    private const float FlexDepartureLatest = 2.0f;

    private FruP3ApocalypseState state = null!;
    private SimWorld world = null!;
    private readonly int[] slots = new int[8];

    public void Run(FruP3ApocalypseState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        AdjustWaterGroups();
        var ai = new AiManager(world);
        ai.Move(2.2f, () => ByRole(role => Setup(role, adjusted: false)), arrivalTime: 12.3f);
        FlexSwapAfterDebuffs(ai);
        ai.Move(20.2f, () => ByRole(FirstStack), jitter: 0.3f, arrivalTime: WaterAt(1));
        ai.Move(22.85f, () => ByRole(role => Setup(role, spread: true)), jitter: 0.3f, arrivalTime: 24.8f);
        ai.Move(24.85f, () => ByRole(role => Setup(role, adjusted: false, spread: true)), jitter: 0.3f, arrivalTime: 28f);
        ai.Move(28f, () => ByRole(Spread), jitter: 0.3f, arrivalTime: WaveAt(0));
        ai.Move(36.05f, () => ByRole(PostEruption), jitter: 0.3f);
        ai.Move(37f, () => ByRole(SecondStack), jitter: 0.3f, arrivalTime: WaveAt(3), hold: true);
        ai.Move(41.7f, () => ByRole(role => role == PartyRole.OffTank ? TankBait(near: true) : null), jitter: 0.3f);
        ai.Move(42f, () => ByRole(role => role == PartyRole.OffTank ? TankBait(near: false) : null), jitter: 0.3f, arrivalTime: 43.8f);
        ai.Move(42f, () => ByRole(role => role == PartyRole.OffTank ? null : Vector3.Zero), arrivalTime: 44f);
        ai.Move(45.15f, () => ByRole(KnockbackStack), jitter: 0.3f);
        world.Events.Add(DarkestDanceLandsAt, GapCloseAfterKnockback);
        ai.Move(DarkestDanceLandsAt, () => ByRole(ReturnStack), jitter: 0.3f, sprint: true);
    }

    private void GapCloseAfterKnockback()
    {
        var oracle = world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.OracleP3);
        if (oracle == null) return;
        foreach (var role in new[] { PartyRole.MainTank, PartyRole.OffTank, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB })
            (world.Party.Get(role) as SimPartyNpc)?.GapClose(oracle, sprintInWhenFar: false);
    }

    private void FlexSwapAfterDebuffs(AiManager ai)
    {
        var stream = world.Stream("ai-flex");
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            var departure = WaterAppliedAt + FlexDepartureEarliest + stream.NextSingle() * (FlexDepartureLatest - FlexDepartureEarliest);
            if (Slot(role) == Slot(role, adjusted: false)) continue;
            ai.Move(departure, () => ByRole(other => other == role ? Setup(other) : null));
        }
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

    private Vector3 OraclePosition()
        => world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.OracleP3)?.Position ?? Vector3.Zero;

    private Vector3? KnockbackStack(PartyRole role)
    {
        var boss = OraclePosition();
        var inward = boss.LengthSquared() > 0.01f ? Vector3.Normalize(-boss) : Vector3.UnitZ;
        var angle = (SupportGroup(role) ? 22.5f : -22.5f) * MathF.PI / 180;
        var direction = new Vector3(inward.X * MathF.Cos(angle) - inward.Z * MathF.Sin(angle), 0,
            inward.X * MathF.Sin(angle) + inward.Z * MathF.Cos(angle));
        return boss + direction * KnockbackStackDistance;
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
