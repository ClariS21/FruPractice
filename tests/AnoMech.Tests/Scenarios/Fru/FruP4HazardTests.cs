using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Scenarios.Fru.P4Full;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Tests;

// Every P4 hazard the guide reports is the area the sim resolves: a bot put half a yalm inside one just before it goes off
// is hit by it, and one put half a yalm outside is not.
public class FruP4HazardTests
{
    private const float Margin = 0.5f;
    // The bot is held on its point for the frames this long before the hazard goes off.
    private const float Lead = 0.3f;
    // The sim resolves on its frame clock: a cast scheduled from inside another event goes off a few frames after its nominal snapshot.
    private const float Frames = 4 * ScenarioRun.FrameSeconds;

    // Which hazard: its label, the first time it goes off at or after `After`, and which of the simultaneous ones by position.
    // `Ray` gives a point inside it and a way out: the boundary probed is where that ray leaves it.
    public sealed record Case(string Name, Type Scenario, int Seed, string Label, uint[] ActionIds, float After, int Instance,
        Func<GuidanceHazard, (Vector2 From, Vector2 Way)> Ray, Action<object>? Overrides = null)
    {
        public override string ToString() => Name;
    }

    private static IEnumerable<Case> Cases()
    {
        var ct = typeof(FruP4CrystallizeTimeScenario);
        var darklit = typeof(FruP4DarklitDragonsongScenario);
        Action<object> Ct(bool? slowNorthwest = null, CrystallizeCorner? corner = null) => o =>
        {
            var s = (FruP4CrystallizeTimeStateOverrides)o;
            s.SlowNorthwest = slowNorthwest;
            s.Corner = corner;
        };
        uint[] maelstrom = [ActionId.Maelstrom];
        yield return new("Maelstrom, quickened pair", ct, 1, "Maelstrom", maelstrom, 21f, 0, Tangent, Ct(slowNorthwest: true));
        yield return new("Maelstrom, second pair", ct, 2, "Maelstrom", maelstrom, 26.5f, 1, Tangent, Ct(slowNorthwest: false));
        yield return new("Maelstrom, slowed pair", ct, 3, "Maelstrom", maelstrom, 31.5f, 0, Tangent, Ct(slowNorthwest: true));
        uint[] tidal = [ActionId.TidalLightFirst, ActionId.TidalLightRest];
        yield return new("Tidal Light, first strip going west, far edge", ct, 1, "Tidal Light", tidal, 29f, 0, h => Across(h, far: true), Ct(corner: CrystallizeCorner.NE));
        yield return new("Tidal Light, third strip going east, near edge", ct, 2, "Tidal Light", tidal, 35.5f, 0, h => Across(h, far: false), Ct(corner: CrystallizeCorner.SW));
        yield return new("Tidal Light, second strip going south, far edge", ct, 3, "Tidal Light", tidal, 39f, 0, h => Across(h, far: true), Ct(corner: CrystallizeCorner.NW));
        yield return new("Tidal Light, last strip going north, near edge", ct, 4, "Tidal Light", tidal, 44f, 0, h => Across(h, far: false), Ct(corner: CrystallizeCorner.SE));
        uint[] blizzard = [ActionId.DarkBlizzard];
        yield return new("Dark Blizzard, the hole", ct, 1, "Dark Blizzard", blizzard, 25f, 0, h => Ring(h, inner: true));
        yield return new("Dark Blizzard, the rim", ct, 2, "Dark Blizzard", blizzard, 25f, 2, h => Ring(h, inner: false));
        yield return new("Dark Eruption", ct, 3, "Dark Eruption", [ActionId.DarkEruption], 25f, 0, Tangent);
        yield return new("Spirit Taker, Crystallize Time", ct, 4, "Spirit Taker", [ActionId.SpiritTaker], 48f, 0, Tangent);
        uint[] akhRhai = [ActionId.AkhRhaiCast, ActionId.AkhRhai];
        yield return new("Akh Rhai, first drop", darklit, 1, "Akh Rhai", akhRhai, 8f, 0, Tangent);
        yield return new("Akh Rhai, a later hit", darklit, 2, "Akh Rhai", akhRhai, 12f, 5, Tangent);
        uint[] pathOfLight = [ActionId.PathOfLight];
        yield return new("Path of Light, one cone", darklit, 1, "Path of Light", pathOfLight, 32.9f, 1, Side);
        yield return new("Path of Light, another cone", darklit, 3, "Path of Light", pathOfLight, 32.9f, 3, Side);
        Action<object> Wing(bool east) => o => ((FruP4DarklitDragonsongStateOverrides)o).EastWing = east;
        uint[] wings = [ActionId.HallowedWingsLeft, ActionId.HallowedWingsRight];
        yield return new("Hallowed Wings, east", darklit, 1, "Hallowed Wings", wings, 35f, 0, _ => (new(10, 0), new(-1, 0)), Wing(east: true));
        yield return new("Hallowed Wings, west", darklit, 2, "Hallowed Wings", wings, 35f, 0, _ => (new(-10, 0), new(1, 0)), Wing(east: false));
        yield return new("Spirit Taker, Darklit Dragonsong", darklit, 3, "Spirit Taker", [ActionId.SpiritTaker], 35f, 0, Tangent);
    }

    private static IEnumerable<TestCaseData> Probes()
        => from c in Cases() from inside in new[] { true, false } select new TestCaseData(c, inside).SetName($"{c.Name}, {(inside ? "inside" : "outside")}");

    [TestCaseSource(nameof(Probes))]
    public void TheReportedAreaIsTheOneThatHits(Case c, bool inside)
    {
        GuidanceHazard? locked = null;
        Vector2 at = default;
        PartyRole? bot = null;
        bool? hit = null;
        float? hitAt = null;
        var run = ScenarioRun.Execute(c.Scenario, 0, c.Seed, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.MainTank,
            WriteArtifactsOnFailure = false,
            Overrides = c.Overrides,
            StopAt = c.After + 25f,
            Probe = p =>
            {
                if (hitAt is not null) return;
                var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                var group = source.Upcoming(p.Time, 25f).Where(h => h.Label == c.Label && h.FiresAt >= c.After).ToList();
                if (locked is null)
                {
                    if (group.Count == 0) return;
                    var firesAt = group.Min(h => h.FiresAt);
                    if (firesAt - p.Time > Lead) return;
                    group = group.Where(h => h.FiresAt == firesAt).OrderBy(h => Origin(h).X).ThenBy(h => Origin(h).Y).ToList();
                    locked = group[c.Instance % group.Count];
                    p.World.SimEvents.Listener = new ResolveProbe(p.World.SimEvents.Listener!, resolved =>
                    {
                        if (!c.ActionIds.Contains(resolved.Action.ActionId) || MathF.Abs(p.Time - locked.FiresAt) > Frames) return;
                        if (!SameOrigin(resolved.Origin, locked)) return;
                        hit = resolved.Hits.Contains(p.Member(bot!.Value)!);
                        hitAt = p.Time;
                    });
                }
                else if (source.Upcoming(p.Time, 1f).Where(h => h.Label == c.Label && h.FiresAt == locked.FiresAt)
                             .MinBy(h => Vector2.Distance(Origin(h), Origin(locked))) is { } same)
                    locked = same;
                var (from, way) = c.Ray(locked);
                var (edge, normal) = Edge(locked.Shape, from, way);
                at = edge + normal * (inside ? -Margin : Margin);
                // A bot clear of the area's own holder, and every member held still so the area doesn't move under it.
                bot ??= Enum.GetValues<PartyRole>().Where(r => r != PartyRole.MainTank && p.Member(r) is { } m && m.IsAlive()
                    && Vector2.Distance(GuidanceLogic.Xz(m.Position), Origin(locked)) > 1f).First();
                foreach (var member in p.World.Party.ActiveMembers()) member.StopMoving();
                p.Member(bot.Value)!.SetPosition(new Vector3(at.X, 0, at.Y));
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(locked, Is.Not.Null, "no such hazard reported");
        Assert.That(at.Length(), Is.LessThan(19.5f), "the probe point left the arena");
        Assert.That(locked!.Shape.SignedDistance(at), Is.EqualTo(inside ? -Margin : Margin).Within(0.05f));
        Assert.That(hit, Is.EqualTo(inside), run.ToString());
        Assert.That(hitAt, Is.EqualTo(locked.FiresAt).Within(Frames));
    }

    [Test]
    public void FullPhaseExposesEachSliceOnTheRunClock()
    {
        float? akhRhai = null, maelstrom = null;
        var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = FruP4FullScenario.CrystallizeTimeAt + 21f,
            Probe = p =>
            {
                var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                if (p.Crossed(FruP4FullScenario.DarklitDragonsongAt + 7f))
                    akhRhai = source.Upcoming(p.Time, 5f).Where(h => h.Label == "Akh Rhai").Min(h => (float?)h.FiresAt);
                if (p.Crossed(FruP4FullScenario.CrystallizeTimeAt + 20f))
                    maelstrom = source.Upcoming(p.Time, 5f).Where(h => h.Label == "Maelstrom").Min(h => (float?)h.FiresAt);
            },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(akhRhai, Is.EqualTo(FruP4FullScenario.DarklitDragonsongAt + 8.8f).Within(0.05f));
        Assert.That(maelstrom, Is.EqualTo(FruP4FullScenario.CrystallizeTimeAt + 22.7f).Within(0.05f));
    }

    // The guide keeps every hazard it is shown. One on a player on the move is the same hazard however often it is reported,
    // and once it goes off it stays where it went off. Its own player, who takes it, isn't warned off it.
    [Test]
    public void AHazardOnAPlayerIsOneHazardThatStaysWhereItWentOff()
    {
        var seen = new HashSet<GuidanceHazard>();
        Vector2? wentOffAt = null;
        var holderWarned = false;
        var run = ScenarioRun.Execute(typeof(FruP4CrystallizeTimeScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 30f,
            WriteArtifactsOnFailure = false,
            Probe = p =>
            {
                if (p.Time < 1f) return;
                if (p.World.SimEvents.Listener is not ResolveProbe)
                    p.World.SimEvents.Listener = new ResolveProbe(p.World.SimEvents.Listener!, resolved =>
                    {
                        if (resolved.Action.ActionId == ActionId.DarkEruption) wentOffAt = GuidanceLogic.Xz(resolved.Origin.Position);
                    });
                var scenario = (FruP4CrystallizeTimeScenario)p.Game.ActiveScenario!;
                var holder = p.Member(scenario.State.Holders(CrystallizeDebuff.BlueEruption).Single())!;
                foreach (var hazard in scenario.Upcoming(p.Time - 5f, 30f).Where(h => h.Label == "Dark Eruption"))
                {
                    seen.Add(hazard);
                    if (wentOffAt is null && hazard.Shape.SignedDistance(GuidanceLogic.Xz(holder.Position)) <= 0f) holderWarned = true;
                }
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(seen, Has.Count.EqualTo(1));
        Assert.That(wentOffAt, Is.Not.Null);
        Assert.That(Vector2.Distance(Origin(seen.Single()), wentOffAt!.Value), Is.LessThan(0.001f));
        Assert.That(holderWarned, Is.False, "the Eruption's own holder was warned off it");
    }

    private static Vector2 Origin(GuidanceHazard h) => h.Shape switch
    {
        GuidanceShape.Circle circle => circle.Center,
        GuidanceShape.Area area => GuidanceLogic.Xz(area.Query.Source.Position),
        FruP4Hazards.OnMark mark => mark.Center,
        _ => throw new InvalidOperationException(h.Shape.ToString()),
    };

    // The resolve of this very area: its spot, and for a line or cone its facing too (Path of Light's four share a spot).
    private static bool SameOrigin(Placement origin, GuidanceHazard h)
        => Vector2.Distance(GuidanceLogic.Xz(origin.Position), Origin(h)) < 0.05f
           && (h.Shape is not GuidanceShape.Area area || MathF.Abs(MathF.IEEERemainder(origin.Rotation - area.Query.Source.Rotation, MathF.Tau)) < 0.01f);

    private static Vector2 Forward(GuidanceHazard h)
    {
        var area = (GuidanceShape.Area)h.Shape;
        var rotation = area.Query.Source.Rotation + area.Query.OmenRotate;
        return new(MathF.Sin(rotation), MathF.Cos(rotation));
    }

    // Out of a circle sideways, or towards the arena's middle for one too near the edge for that. The ray starts off the
    // circle's middle, which a mark leaves to its own player.
    private static (Vector2, Vector2) Tangent(GuidanceHazard h)
    {
        var center = Origin(h);
        var reach = Radius(h) + 1f;
        var way = center.Length() < 1f ? new Vector2(1, 0)
            : center.LengthSquared() + reach * reach < 19f * 19f ? Vector2.Normalize(new Vector2(-center.Y, center.X))
            : -Vector2.Normalize(center);
        return (center + way * 1.5f, way);
    }

    private static float Radius(GuidanceHazard h) => h.Shape switch
    {
        GuidanceShape.Circle circle => circle.Radius,
        FruP4Hazards.OnMark mark => -(mark.Mark.Query.SignedDistance(mark.Mark.Query.Source.Position) ?? 0f),
        _ => throw new InvalidOperationException(h.Shape.ToString()),
    };

    // Out of a strip through its far or near edge, a little off its middle line.
    private static (Vector2, Vector2) Across(GuidanceHazard h, bool far)
    {
        var forward = Forward(h);
        var middle = Origin(h) + forward * 5f + new Vector2(-forward.Y, forward.X) * 3f;
        return (middle, far ? forward : -forward);
    }

    // Out of a donut's ring into its hole, or past its rim towards the arena's middle.
    private static (Vector2, Vector2) Ring(GuidanceHazard h, bool inner)
    {
        var center = Origin(h);
        var inwards = center.Length() > 1f ? -Vector2.Normalize(center) : new Vector2(1, 0);
        return (center + inwards * 7.5f, inner ? -inwards : inwards);
    }

    // Out of a cone through its side, at 6 y along it.
    private static (Vector2, Vector2) Side(GuidanceHazard h)
    {
        var forward = Forward(h);
        return (Origin(h) + forward * 6f, new Vector2(-forward.Y, forward.X));
    }

    // Where a ray from inside the shape leaves it, and the outward normal there.
    private static (Vector2 Edge, Vector2 Normal) Edge(GuidanceShape shape, Vector2 from, Vector2 way)
    {
        Assert.That(shape.SignedDistance(from), Is.LessThan(0f), "the ray starts outside the area");
        float lo = 0f, hi = 0.05f;
        while (shape.SignedDistance(from + way * hi) < 0f) (lo, hi) = (hi, hi + 0.05f);
        for (var i = 0; i < 30; i++)
        {
            var mid = (lo + hi) / 2f;
            if (shape.SignedDistance(from + way * mid) < 0f) lo = mid;
            else hi = mid;
        }
        var edge = from + way * hi;
        const float d = 0.01f;
        var gradient = new Vector2(shape.SignedDistance(edge + new Vector2(d, 0)) - shape.SignedDistance(edge - new Vector2(d, 0)),
            shape.SignedDistance(edge + new Vector2(0, d)) - shape.SignedDistance(edge - new Vector2(0, d)));
        return (edge, Vector2.Normalize(gradient));
    }

    private sealed class ResolveProbe(IWorldEventListener inner, Action<ActionResolved> inspect) : IWorldEventListener
    {
        public void OnActionResolved(ActionResolved e) { inspect(e); inner.OnActionResolved(e); }
        public void OnStatusAdded(StatusAdded e) => inner.OnStatusAdded(e);
        public void OnStatusLost(StatusLost e) => inner.OnStatusLost(e);
        public void OnTetherEnded(TetherEnded e) => inner.OnTetherEnded(e);
    }
}
