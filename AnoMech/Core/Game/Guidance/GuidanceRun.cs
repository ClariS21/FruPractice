using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;

namespace AnoMech.Core.Game.Guidance;

public sealed record GuidanceDebrief(Vector3 Origin, IReadOnlyList<(float t, Vector2 p)> Path,
    IReadOnlyList<StratTarget> Spots, IReadOnlyList<GuidanceHazard> Hazards,
    IReadOnlyList<PathFinding> Findings, IReadOnlyList<PathSafety> Colors);

public sealed class GuidanceRun(Game game)
{
    private readonly PlayerPathRecorder recorder = new();
    private readonly HashSet<GuidanceHazard> hazards = [];
    private float lastTime;
    public PlayerPathRecorder Recorder => recorder;
    public GuidanceDebrief? Debrief { get; private set; }
    public bool Recording { get; private set; }
    public IGuidanceHazardSource? HazardSource => game.ActiveScenario as IGuidanceHazardSource;

    public void Start()
    {
        Debrief = null;
        hazards.Clear();
        lastTime = 0;
        Recording = game.Player is not null;
        recorder.Start(GuidanceLogic.Xz(game.Player?.Position ?? Vector3.Zero));
        Sample();
    }

    public void Sample()
    {
        if (!Recording || game.Player is not { } player) return;
        var now = game.Events.Elapsed;
        recorder.Sample(now, GuidanceLogic.Xz(player.Position));
        if (HazardSource is { } source)
            foreach (var hazard in source.Upcoming(lastTime, now - lastTime + 30f)) hazards.Add(hazard);
        lastTime = now;
    }

    public GuidanceFrame? Live()
    {
        if (!Recording || game.Player is not { } player) return null;
        return GuidanceLogic.Compute(player.Role, GuidanceLogic.Xz(player.Position), game.Events.Elapsed,
            game.World.Strat, hazards);
    }

    public void Finish()
    {
        if (!Recording || game.Player is not { } player) return;
        Sample();
        Recording = false;
        var path = recorder.Samples.ToList();
        // Keep the exact end point too: a death between 10 Hz samples must still be analyzable.
        if (path.Count == 0 || path[^1].t < game.Events.Elapsed)
            path.Add((game.Events.Elapsed, GuidanceLogic.Xz(player.Position)));
        var spots = game.World.Strat.Entries.Where(s => s.Role == player.Role).ToArray();
        var seen = hazards.Where(h => h.FiresAt <= game.Events.Elapsed).ToArray();
        Debrief = new(game.World.Coordinates.ToGlobal(Vector3.Zero), path, spots, seen,
            PathDebrief.Analyze(path, spots, seen), PathDebrief.Colors(path, seen));
    }

    public void Close() => Debrief = null;
}
