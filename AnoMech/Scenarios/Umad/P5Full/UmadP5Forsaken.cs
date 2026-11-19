using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P5Full;

// Which black holes a Forsaken set throws, relative to where its marker stands: Side picks a
// neighbour, Pick one of the set's patterns.
public sealed record UmadP5ForsakenRoll(PartyRole Marker, int Side, int Pick);

// P5's Forsaken: Kefka's long raidwide, then four sets. Each puts a stack marker on a non-tank, an
// orange under the marker and two black holes; they land with Forsaken Bonds on the marker, and the
// next set's raidwide opens it. A black hole leaves a void: whoever touches one, then or for the rest
// of the fight, is Forsaken. Forsaken Null is the enrage.
internal sealed class UmadP5Forsaken
{
    internal const float RaidwideAt = 149.66f;
    internal static readonly float[] MarkerAt = [159.64f, 167.82f, 175.97f, 184.11f];
    internal static readonly float[] PuddlesAt = [159.73f, 167.89f, 176.04f, 184.19f];
    internal static readonly float[] BondsAt = [164.75f, 172.89f, 181.03f, 189.19f];
    // The orange and the black holes snapshot as their bars end.
    internal const float PuddlesLandAfter = 4.7f;
    internal const float NullAt = 194.25f;
    // Its blast lands this long after the bar, unless Kefka is dead by then.
    internal const float NullLandsAt = 226.65f;

    // Puddles stand on a grid: the centre, the outer points on the cardinals and corners, and the
    // inner corners. Bearings go in eighths, clockwise from north.
    internal const float OuterReach = 13.5f;
    internal const float InnerReach = 6.75f;
    private static readonly int[] StepX = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] StepZ = [-1, -1, 0, 1, 1, 1, 0, -1];
    // The arena's map effects for the voids: the centre, then each outer point by bearing. West's is
    // UNVERIFIED, inferred from the order of the others.
    private const byte CentreVoidEffect = 0x14;
    private static readonly byte[] OuterVoidEffect = [0x16, 0x1A, 0x15, 0x19, 0x18, 0x1C, 0x17, 0x1B];
    private const uint VoidEffectFlags = 0x00200010U;
    private static readonly byte[] SetEffect = [0x1D, 0x1E, 0x1F, 0x20];
    private const float SetEffectAfterMarker = 2.05f;
    private const float VoidEffectAfterResolve = 0.9f;

    private SimWorld world = null!;
    private UmadP5Cast cast = null!;
    private readonly List<Vector3> voids = [];
    private readonly HashSet<SimCharacter> forsaken = [];

    internal static Vector3 OuterPoint(int step) => new(OuterReach * StepX[Mod8(step)], 0f, OuterReach * StepZ[Mod8(step)]);

    internal static Vector3 InnerPoint(int step) => new(InnerReach * StepX[Mod8(step)], 0f, InnerReach * StepZ[Mod8(step)]);

    internal static int BearingStep(Vector3 at) => Mod8((int)MathF.Round(MathF.Atan2(at.X, -at.Z) / (MathF.PI / 4f)));

    private static int Mod8(int step) => (step % 8 + 8) % 8;

    // Where an orange can go: the inner corners and the outer cardinals.
    private static readonly Vector3[] OrangeSpots = [.. Enumerable.Range(0, 8).Select(step => step % 2 == 1 ? InnerPoint(step) : OuterPoint(step))];

    // The black holes of set `set` (0-3) for a marker on bearing `step`, never on a point an earlier
    // set used: the centre and a corner beside the marker; two corners, from the marker's own round
    // the way it goes; a cardinal beside the marker and the last corner; then two of the three cardinals
    // left.
    internal static IReadOnlyList<Vector3> Holes(int set, int step, UmadP5ForsakenRoll roll, IReadOnlyCollection<Vector3> used)
    {
        var corner = step % 2 == 1;
        bool Free(int point) => used.All(v => Vector3.Distance(v, OuterPoint(point)) > 0.1f);
        int[] FreeOf(IEnumerable<int> points) => [.. points.Select(Mod8).Distinct().Where(Free)];
        int[] corners = FreeOf(Enumerable.Range(0, 4).Select(i => step + (corner ? 2 * i : 2 * i + 1)));
        int[] cardinals = FreeOf(Enumerable.Range(0, 4).Select(i => step + (corner ? 2 * i + 1 : 2 * i)));
        int[] points = set switch
        {
            0 => [Mod8(step + roll.Side * (corner ? 2 : 1))],
            1 => Fill(corner ? FreeOf([step, step + 2, step + 4]) : FreeOf([step + roll.Side, step + 3 * roll.Side]), corners, 2, roll.Pick),
            2 => [.. Fill(FreeOf([step + roll.Side * (corner ? 1 : 2)]), cardinals, 1, 0), .. Fill([], corners, 1, roll.Pick)],
            _ => Fill([], cardinals, 2, roll.Pick),
        };
        return set == 0 ? [Vector3.Zero, OuterPoint(points[0])] : [.. points.Select(OuterPoint)];
    }

    // `count` points: `preferred` first (with one too many, the pick leaves one out), then the rest of
    // `pool` in turn from the pick.
    private static int[] Fill(int[] preferred, int[] pool, int count, int pick)
    {
        var chosen = preferred.Length > count ? preferred.Where((_, i) => i != pick % preferred.Length).Take(count).ToList() : [.. preferred];
        var others = pool.Where(p => !chosen.Contains(p)).ToList();
        for (var i = 0; chosen.Count < count && i < others.Count; i++) chosen.Add(others[(pick + i) % others.Count]);
        return [.. chosen];
    }

    // The orange goes on the spot nearest the marker that has no black hole.
    internal static Vector3 Orange(Vector3 marker, IReadOnlyList<Vector3> holes)
        => OrangeSpots.Where(spot => holes.All(hole => Vector3.Distance(hole, spot) > 0.1f)).MinBy(spot => Flat(spot, marker));

    internal static float PuddleRadius => Natives.Data.Action(ActionId.ForsakenGround)?.EffectRange ?? 8f;

    // `origin` is the clock time the run's own zero stands for: 0 inside the full phase, and the
    // standalone drill starts a little before the raidwide.
    public void Schedule(SimWorld worldParam, UmadP5Cast castParam, IReadOnlyList<UmadP5ForsakenRoll> rolls, float origin = 0f)
    {
        world = worldParam;
        cast = castParam;
        voids.Clear();
        forsaken.Clear();
        void At(float time, Action action) => world.Events.Add(time - origin, action);
        At(RaidwideAt, () => cast.Kefka?.Cast(UmadActions.ForsakenP5));
        At(RaidwideAt, () => BattleTalk(0x28));
        for (var set = 0; set < MarkerAt.Length; set++)
        {
            var s = set;
            if (s > 0) At(MarkerAt[s], () => cast.Kefka?.Cast(UmadActions.ForsakenP5Raidwide));
            if (s > 0) At(MarkerAt[s], () => BattleTalk((uint)(0x28 + s)));
            At(MarkerAt[s], () => Marker(rolls[s])?.AttachLockonVfx(LockonId.Stack));
            if (s == 0) At(MarkerAt[s], () => cast.Kefka?.AddStatus(StatusId.DirectionalDisregard));
            At(PuddlesAt[s], () => DropPuddles(s, rolls[s]));
            At(MarkerAt[s] + SetEffectAfterMarker, () => world.Map.AddEffect(packetFlags: 0x00200010U, index: SetEffect[s]));
            At(BondsAt[s], () => Bonds(rolls[s]));
        }
        At(NullAt, () => cast.Kefka?.Cast(ActionId.ForsakenNull, animationLock: 3.1f));
        At(NullAt + 0.85f, ClearSetEffects);
        At(NullAt + 4.37f, () => BattleTalk(0x2C));
    }

    // Kefka's lines, as retail's director sends them.
    private void BattleTalk(uint line)
    {
        if (cast.Kefka is { } kefka) world.Map.DirectorUpdate(0x80000027U, line, 0x2U, BNpcNameId.Kefka, kefka.EntityId);
    }

    private void ClearSetEffects()
    {
        world.Map.AddEffect(packetFlags: 0x00100008U, index: 0x21);
        foreach (var effect in SetEffect) world.Map.AddEffect(packetFlags: 0x00080004U, index: effect);
    }

    // The roll's marker, or another non-tank when it is down.
    private SimCharacter? Marker(UmadP5ForsakenRoll roll)
        => world.Party.Get(roll.Marker) is { } rolled && rolled.IsAlive()
            ? rolled
            : NonTanks.Select(world.Party.Get).FirstOrDefault(member => member.IsAlive());

    private static readonly PartyRole[] NonTanks =
    [
        PartyRole.RegenHealer, PartyRole.ShieldHealer,
        PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps,
    ];

    private void DropPuddles(int set, UmadP5ForsakenRoll roll)
    {
        var marker = Marker(roll)?.Position ?? new Vector3(0f, 0f, OuterReach);
        var holes = Holes(set, BearingStep(marker), roll, voids);
        cast.Kefka?.CastFromHelper(UmadActions.ForsakenOrange, at: new Placement(Orange(marker, holes), 0f));
        foreach (var hole in holes)
            cast.Kefka?.CastFromHelper(UmadActions.ForsakenGround, at: new Placement(hole, 0f));
    }

    private void Bonds(UmadP5ForsakenRoll roll)
    {
        if (Marker(roll) is { } holder)
            cast.Kefka?.CastFromHelper(UmadActions.ForsakenBonds, holder, at: new Placement(Vector3.Zero, MathF.PI));
    }

    public void OnActionResolved(ActionResolved resolved)
    {
        if (resolved.Action.ActionId != ActionId.ForsakenGround) return;
        foreach (var killed in resolved.Killed) forsaken.Add(killed);
        var hole = resolved.Origin.Position with { Y = 0f };
        voids.Add(hole);
        world.Obstacles.Add(new CircleObstacle(new Vector2(hole.X, hole.Z), PuddleRadius + VoidClearance));
        var effect = hole.LengthSquared() < 1f ? CentreVoidEffect : OuterVoidEffect[BearingStep(hole)];
        world.Events.Add(VoidEffectAfterResolve, () => world.Map.AddEffect(packetFlags: VoidEffectFlags, index: effect));
    }

    // Bots keep this much beyond the void's edge and their own body.
    private const float VoidClearance = 0.7f;

    // A body touching a void's edge.
    public void Tick()
    {
        if (voids.Count == 0) return;
        foreach (var member in world.Party.ActiveMembers().ToList())
        {
            if (forsaken.Contains(member)) continue;
            var reach = PuddleRadius + member.HitboxRadius;
            if (!voids.Any(hole => Flat(hole, member.JudgedPosition) <= reach)) continue;
            forsaken.Add(member);
            cast.Kefka?.CastFromHelper(UmadActions.ForsakenVoid, member, at: new Placement(member.Position, 0f));
        }
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
