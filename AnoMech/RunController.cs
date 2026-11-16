using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Implementations;

namespace AnoMech;

// The user's Start / Reset / Leave (main window, /frup).
internal sealed class RunController
{
    private readonly Game game;

    // A restart inside a loaded zone never settles.
    private readonly SettleWait startWait = new();

    public RunController(Game game)
    {
        this.game = game;
    }

    public string? StartWaitingOn => startWait.WaitingOn;

    public void Tick() => startWait.Tick();

    public string? StartRefusal(ScenarioSelection? selection, bool solo)
    {
        if (selection is not { Scenario: var scenario }) return "No scenario selected.";
        if (solo && !scenario.SupportsSolo) return $"{scenario.Name} does not support Solo mode.";
        if (startWait.WaitingOn is { } waiting) return $"Waiting for {waiting} to settle before starting...";
        // A settle only delays the start; Start waits it out.
        if (ZoneControl.StartBlockedReason(out var settling) is { } blocked && settling == null)
            return $"Cannot start: {blocked}.";
        if (!solo && selection.Strat is null) return "No strat available for this region yet.";
        return null;
    }

    public void Start(ScenarioSelection? selection, bool solo)
    {
        if (StartRefusal(selection, solo) is { } refusal)
        {
            Plugin.Log.Warning(refusal);
            return;
        }
        var party = new LocalPartyProvider { RoleOverride = selection!.RoleOverride, Solo = solo };
        var p = new RunScenarioParams(selection.Scenario, party, solo ? null : selection.Strat, selection.Waymark, selection.Seed);
        ZoneControl.EndSpeedBuffs();
        if (ZoneControl.StartBlockedReason(out var settling) != null && settling != null)
        {
            DiagnosticLog.Info($"[RunController] Start waiting for {settling} to settle.");
            startWait.Hold(settling, () => game.EnterAndStart(p, SessionMode.Local));
            return;
        }
        game.EnterAndStart(p, SessionMode.Local);
    }

    public void Reset()
    {
        startWait.Cancel();
        game.Reset();
    }

    public void Leave()
    {
        startWait.Cancel();
        if (!game.World.Map.IsInInstance) return;
        game.Leave();
    }
}
