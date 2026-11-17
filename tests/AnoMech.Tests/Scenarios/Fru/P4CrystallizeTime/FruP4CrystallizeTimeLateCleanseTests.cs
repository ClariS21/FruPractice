using System.Numerics;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Tests;

// A human takes their puddle when they get round to it, not the moment the guide sends them: bots time nothing off them. Here
// the human's stand-in walks up to its puddle, then waits beside it until the last moment it can still step in and get out
// safely, as a slow player would. Nobody else may use the puddle up meanwhile.
public class FruP4CrystallizeTimeLateCleanseTests
{
    private static readonly CrystallizeDebuff[] Blues =
        [CrystallizeDebuff.BlueEruption, CrystallizeDebuff.BlueIce, CrystallizeDebuff.BlueDarkness, CrystallizeDebuff.BlueWater];

    private static IEnumerable<TestCaseData> Cases()
        => from seat in Enum.GetValues<PartyRole>()
           from debuff in Blues
           from corner in Enum.GetValues<CrystallizeCorner>()
           select new TestCaseData(seat, debuff, corner).SetName($"{{m}}({seat}, {debuff}, {corner})");

    [TestCaseSource(nameof(Cases))]
    public void NobodyUsesUpALateHumansPuddleAndTheHumanStillCleanses(PartyRole seat, CrystallizeDebuff debuff, CrystallizeCorner corner)
    {
        var seed = 1 + (int)seat * 100 + Array.IndexOf(Blues, debuff) * 10 + (int)corner;
        var late = new LateCleanser(seat);
        var run = ScenarioRun.Execute(typeof(FruP4CrystallizeTimeScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = seat,
            Overrides = o =>
            {
                var overrides = (FruP4CrystallizeTimeStateOverrides)o;
                overrides.PlayerDebuffs = [debuff];
                overrides.Corner = corner;
            },
            Probe = late.Probe,
        });
        Assert.Multiple(() =>
        {
            Assert.That(late.Spent, Is.Empty, $"puddles used up by someone they cleansed nothing off (seed {seed}): {run}");
            Assert.That(late.WaitedUntil, Is.Not.Null, $"the human never got beside its puddle (seed {seed}): {run}");
            Assert.That(late.CleansedAt, Is.Not.Null, $"the late human never cleansed (seed {seed}): {run}");
            Assert.That(run.Passed, $"seed {seed}: {run}");
        });
    }

    // Drives the human's seat: on the stand-in's orders, except that it stops beside its puddle and only steps in at the last
    // safe moment or once the guide moves it on, then picks the stand-in's orders back up. Also notes every puddle spent without
    // a cleanse.
    internal sealed class LateCleanser(PartyRole human)
    {
        private const float PuddleReach = 1f;
        private const float PuddleLifetime = 17.8f;
        private const float WaitsAt = PuddleReach + 0.8f;
        // The rewind step sends everyone to their rewind spot: by then the human is back on the stand-in's orders.
        private const float RewindAt = 40.55f;
        private const float HazardClear = 0.6f;
        private const float Walk = 6.5f;

        private readonly Dictionary<PartyRole, float> droppedAt = new();
        private readonly Dictionary<PartyRole, Vector3> alive = new();
        private readonly HashSet<PartyRole> fang = [];
        private PartyRole? puddle;
        private Vector3 puddleAt;
        private float? lateAt;
        private float waitingFrom;

        public List<string> Spent { get; } = [];
        public float? WaitedUntil => lateAt;
        public float? CleansedAt { get; private set; }

        public void Probe(ScenarioProbe p)
        {
            var scenario = (FruP4CrystallizeTimeScenario)p.Game.ActiveScenario!;
            var cleansed = NoteCleanses(p, scenario.State);
            Drive(p, scenario, cleansed);
        }

        private HashSet<PartyRole> NoteCleanses(ScenarioProbe p, FruP4CrystallizeTimeState state)
        {
            var lost = new HashSet<PartyRole>();
            foreach (var member in p.World.Party.ActiveMembers())
            {
                var role = ((ISimPartyMember)member).Role;
                if (member.HasStatus(StatusId.Wyrmfang)) fang.Add(role);
                else if (fang.Remove(role)) lost.Add(role);
            }
            foreach (var (catcher, at) in alive.ToList())
            {
                if (state.DragonPuddles.ContainsKey(catcher)) continue;
                alive.Remove(catcher);
                if (p.Time - droppedAt[catcher] >= PuddleLifetime - 2 * ScenarioRun.FrameSeconds) continue;
                var inside = p.World.Party.ActiveMembers()
                    .Where(m => ((ISimPartyMember)m).Role != catcher && Flat(m.JudgedPosition, at) <= PuddleReach + 0.01f)
                    .Select(m => ((ISimPartyMember)m).Role).ToList();
                if (!inside.Any(lost.Contains))
                    Spent.Add($"t={p.Time:F2} {catcher}'s puddle at ({at.X:F2},{at.Z:F2}) by {string.Join("/", inside)}");
            }
            foreach (var (catcher, at) in state.DragonPuddles)
                if (alive.TryAdd(catcher, at)) droppedAt[catcher] = p.Time;
            return lost;
        }

        private void Drive(ScenarioProbe p, FruP4CrystallizeTimeScenario scenario, HashSet<PartyRole> cleansed)
        {
            if (p.Member(human) is not { } me || !me.IsAlive()) return;
            if (cleansed.Contains(human)) CleansedAt = p.Time;
            if (CleansedAt is not null || p.World.Strat.Entries.LastOrDefault(s => s.Role == human) is not { } latest) return;
            if (puddle is null)
            {
                foreach (var (catcher, at) in alive)
                    if (Flat(latest.Spot, at) < 0.05f) (puddle, puddleAt) = (catcher, at);
                return;
            }
            if (lateAt is null)
            {
                if (Flat(me.Position, puddleAt) > WaitsAt) return;
                DebugBotControl.Enabled = false;
                me.StopMoving();
                var away = new Vector2(me.Position.X - puddleAt.X, me.Position.Z - puddleAt.Z);
                away = away.LengthSquared() > 0f ? Vector2.Normalize(away) : Vector2.UnitY;
                var spot = puddleAt + new Vector3(away.X, 0f, away.Y) * WaitsAt;
                me.SetPosition(spot);
                waitingFrom = p.Time;
                lateAt = LatestSafeStep(p, scenario, spot);
                p.Log($"late human waits at ({spot.X:F2},{spot.Z:F2}) beside its puddle at ({puddleAt.X:F2},{puddleAt.Z:F2}) until t={lateAt:F2}");
                return;
            }
            if (DebugBotControl.Enabled) return;
            var movedOn = latest.IssuedAt > waitingFrom;
            if (p.Time < lateAt && !movedOn) return;
            // The last stride in at once, so no order the stand-in still had pending walks it off first. The stand-in is back in
            // charge from the cleanse on, so the AI's reaction to it moves the seat.
            DebugBotControl.Enabled = true;
            me.SetPosition(puddleAt);
            me.StopMoving();
            if (movedOn) me.MoveTo(latest.Spot, Walk);
            p.Log($"late human steps in at t={p.Time:F2}" + (movedOn ? $", then heads for ({latest.Spot.X:F2},{latest.Spot.Z:F2})" : ""));
        }

        // As late as the puddle lasts and the rewind allows, with the wait beside it and the step in clear of every hit.
        private float LatestSafeStep(ScenarioProbe p, FruP4CrystallizeTimeScenario scenario, Vector3 waitSpot)
        {
            var hazards = ((IGuidanceHazardSource)scenario).Upcoming(p.Time, 60f).ToList();
            var latest = MathF.Min(droppedAt[puddle!.Value] + PuddleLifetime - 1f, RewindAt - 0.5f);
            for (var t = latest; t > p.Time; t -= 0.05f)
            {
                var step = t;
                if (hazards.All(h => h.FiresAt > step + 0.3f || Clear(h, waitSpot) > HazardClear)
                    && hazards.All(h => h.FiresAt < step - 0.2f || h.FiresAt > step + 1.2f || Clear(h, puddleAt) > PuddleReach + HazardClear))
                    return step;
            }
            return p.Time;
        }

        private static float Clear(GuidanceHazard hazard, Vector3 point) => hazard.Shape.SignedDistance(new Vector2(point.X, point.Z));
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
