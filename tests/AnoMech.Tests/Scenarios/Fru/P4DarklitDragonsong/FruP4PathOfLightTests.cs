using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Scenarios.Fru.P4Full;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Tests;

// Path of Light aims its four cones at the four players nearest the center. In the NA strat those are the four free
// players; a tower soaker who takes one holds a fifth Lightsteeped stack, and on the Fragment's side the cone breaks
// the Fragment of Fate too.
public class FruP4PathOfLightTests
{
    // Darklit Dragonsong's time when the cones turn to their targets.
    private const float AimedAt = 33f;
    // A bot's room: the cones' pick must not hinge on less.
    private const float Spare = 0.5f;
    private const int Seeds = 3;

    // Both wiped: the human's stand-in, soaking a tower, stood nearer the center than a baiting bot and took a cone.
    [TestCase(222885098)]
    [TestCase(262087764)]
    public void TheFullPhaseClearsWhereATowerSoakerOnceTookACone(int seed)
    {
        var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, seed);
        Assert.That(run.Passed, run.ToString);
    }

    // The second strat only changes who baits Somber Dance, after the cones.
    private static IEnumerable<TestCaseData> Runs()
    {
        yield return new TestCaseData(typeof(FruP4DarklitDragonsongScenario), 0f).SetName("{m}(Darklit Dragonsong)");
        yield return new TestCaseData(typeof(FruP4FullScenario), FruP4FullScenario.DarklitDragonsongAt).SetName("{m}(Full phase)");
    }

    // Every seat takes a turn as the human's stand-in, which strays further from its spot than a bot does.
    [TestCaseSource(nameof(Runs))]
    public void TheConesGoToTheFreePlayersWithRoomToSpareAndMissTheFragment(Type scenario, float darklitAt)
    {
        var violations = new List<string>();
        foreach (var seat in Enum.GetValues<PartyRole>())
            for (var seed = 0; seed < Seeds; seed++)
            {
                var watch = new ConeWatch(darklitAt + AimedAt);
                var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
                {
                    PlayerRole = seat,
                    WriteArtifactsOnFailure = false,
                    StopAt = darklitAt + AimedAt + 1f,
                    Probe = watch.Frame,
                });
                if (!run.Passed) violations.Add($"seat {seat}, seed {seed}: {run}");
                if (watch.Cones != 4) violations.Add($"seat {seat}, seed {seed}: {watch.Cones} cones went off");
                violations.AddRange(watch.Violations.Select(v => $"seat {seat}, seed {seed}: {v}"));
            }
        Assert.That(violations, Is.Empty);
    }

    private sealed class ConeWatch(float aimedAt)
    {
        private bool hooked;
        private PartyRole[] free = [];
        public readonly List<string> Violations = [];
        public int Cones;

        public void Frame(ScenarioProbe p)
        {
            if (!hooked && p.World.SimEvents.Listener is { } listener)
            {
                p.World.SimEvents.Listener = new ResolveProbe(listener, Resolved);
                hooked = true;
            }
            if (!p.Crossed(aimedAt)) return;
            var members = Enum.GetValues<PartyRole>().Where(r => p.Member(r) is { } m && m.IsAlive()).ToList();
            var soakers = members.Where(r => p.Member(r)!.HasStatus(StatusId.RefulgentFate)).ToList();
            free = members.Except(soakers).ToArray();
            float FromCenter(PartyRole r) => Flat(p.Member(r)!.JudgedPosition);
            if (soakers.Count != 4 || free.Length != 4)
            {
                Violations.Add($"{soakers.Count} soakers and {free.Length} free players at the aim");
                return;
            }
            var nearestSoaker = soakers.MinBy(FromCenter);
            var farthestBait = free.MaxBy(FromCenter);
            var gap = FromCenter(nearestSoaker) - FromCenter(farthestBait);
            if (gap < Spare)
                Violations.Add($"at the aim the soaker {nearestSoaker} stood {FromCenter(nearestSoaker):F2} y from the center and the baiter "
                               + $"{farthestBait} {FromCenter(farthestBait):F2} y: {gap:F2} y apart");
        }

        private void Resolved(ActionResolved resolved)
        {
            if (resolved.Action.ActionId != ActionId.PathOfLight) return;
            Cones++;
            var hit = resolved.Hits.OfType<ISimPartyMember>().Select(m => m.Role).ToList();
            if (hit.Count != 1 || !free.Contains(hit[0]))
                Violations.Add($"a cone hit [{string.Join(", ", hit)}], not one of the free players [{string.Join(", ", free)}]");
            if (resolved.NpcHits.Any(e => e.BNpcBaseId == BNpcBaseId.FragmentOfFate))
                Violations.Add($"a cone aimed {resolved.Origin.Rotation:F2} rad hit the Fragment of Fate");
        }

        private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();
    }

    private sealed class ResolveProbe(IWorldEventListener inner, Action<ActionResolved> inspect) : IWorldEventListener
    {
        public void OnActionResolved(ActionResolved e) { inspect(e); inner.OnActionResolved(e); }
        public void OnStatusAdded(StatusAdded e) => inner.OnStatusAdded(e);
        public void OnStatusLost(StatusLost e) => inner.OnStatusLost(e);
        public void OnTetherEnded(TetherEnded e) => inner.OnTetherEnded(e);
    }
}
