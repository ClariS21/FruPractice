using Dalamud.Configuration;
using System;

namespace AnoMech;

public enum BotTiming { HumanLike, LastMoment }

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = CurrentVersion;

    
    public bool OpenSimMenuOnInn { get; set; } = true;
    public bool OpenSimMenuOnSupportedInstanceSolo { get; set; } = false;
    public bool EnableEventLogging { get; set; } = false;
    public bool SuppressBgm { get; set; } = true;
    // Mirrors the sim's casts, statuses, tethers and head markers to ACT as network-log lines, for cactbot. See ActBridge.
    public bool SendSimEventsToAct { get; set; } = true;
    public bool EnableMechanicResultMarks { get; set; } = false;
    // On your death: where you were, where the strat had you, and why.
    public bool ShowDeathRecap { get; set; } = true;
    public bool AutoCollapseWhileRunning { get; set; } = false;
    // Party bots auto-attack and use job actions on the boss; animation only.
    public bool BotsAttackBoss { get; set; } = true;
    public BotTiming BotTiming { get; set; } = BotTiming.HumanLike;
    // Mechanics judge the player where the server would see them: this many ms earlier. 0 to 300.
    public int SimulatedPingMs { get; set; } = 80;
    // See MitPractice.
    public const int CurrentVersion = 1;

    // Brings a saved configuration up to date; true when it changed and wants saving. Version 0
    // saved Mitigation Practice off, back when it was off by default.
    public bool Migrate()
    {
        if (Version >= CurrentVersion) return false;
        if (Version < 1) MitigationPractice = true;
        Version = CurrentVersion;
        return true;
    }

    public bool MitigationPractice { get; set; } = true;
    public string LastSelectedScenario { get; set; } = "";

    // Resolve the player's own actions client-side, since the sim firewall blocks
    // the server responses that normally grant them. Sprint is always resolved;
    // this gates everything else (shared actions + per-job kits).
    public bool EnableUserActions { get; set; } = true;

    // Seconds before a player cast finishes during which it can no longer be
    // interrupted by move/jump/cancel — the slidecast window the server's
    // ActionEffect ack opens (measured ~0.5s from replay data). Gated by
    // EnableUserActions.
    public float CastInterruptThreshold { get; set; } = 0.5f;

    // Survivable hits kill a human player whose mitigation is below the hit's threshold; off, they always survive. Gated by EnableUserActions.
    public bool EnableTankMitigation { get; set; } = true;

    // Firewall opcode config — updated automatically by OpcodeUpdater on game version change.
    public uint[] ZoneDownOpcodes { get; set; } = [];
    public string ZoneFirewallGameVersion { get; set; } = "";

    // Safe mode (incoming packet firewall):
    //   true  — only ZoneDownOpcodes pass; cuts you off from server traffic
    //           (no party join/leave updates, no ready checks, no duty pops).
    //   false — all incoming packets pass to the engine. You'll see popups
    //           and party updates, but it's easier to break the sim zone.
    // The send-side firewall stays on either way: nothing the client does in
    // the sim zone leaks back to the server.
    public bool SafeMode { get; set; } = true;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
