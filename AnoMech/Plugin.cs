using System;
using AnoMech.Core;
using AnoMech.Core.ActBridge;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Implementations;
using AnoMech.Core.Native.Implementations.Interop;
using AnoMech.Core.Native.Implementations.Pointers;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Windows;
using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace AnoMech;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private bool commandsRegistered;

    internal static Configuration Config { get; private set; } = null!;
    internal static Game Game { get; private set; } = null!;
    internal static PracticeLoop Practice { get; private set; } = null!;
    internal RunController Runs { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new(PluginIdentity.InternalName);

    // Plugin-lifetime singletons, static like the PluginServices above so sim code reaches them
    // without a Plugin reference.
    internal static LogManager LogManager { get; private set; } = null!;
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private FailureBanner? FailureBanner { get; init; }
    private DeathRecapOverlay DeathRecapOverlay { get; init; } = null!;
    private DeathRecapWindow DeathRecapWindow { get; init; } = null!;
#if DEBUG
    private DamageDebugWindow DamageDebugWindow { get; init; }
#endif

    private OpcodeUpdater? opcodeUpdater;
    private ActBridgeClient? actBridge;
    private ActEmitter? actEmitter;

    public Plugin()
    {
        // First, so every subsequent construction step's own logging is captured from the start.
        DiagnosticLog.Initialize();
        try
        {
            Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            if (Config.Migrate()) PluginInterface.SavePluginConfig(Config);

            LogManager = new LogManager();
            if (Config.EnableEventLogging) LogManager.Open();

            GameNatives.Install();
            actBridge = new ActBridgeClient(typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0");
            actEmitter = ActEmitter.Install(actBridge, () => Config.SendSimEventsToAct);
            opcodeUpdater = new OpcodeUpdater();
            Game = new Game();
            var habitsPath = Core.Game.Ai.Habits.MovementHabits.PathIn(PluginInterface.AssemblyLocation.DirectoryName ?? "");
            var habits = Core.Game.Ai.Habits.MovementHabits.Load(habitsPath);
            if (habits is null) Log.Warning($"Movement habits missing or invalid at {habitsPath}; bots move on the built-in defaults.");
            Game.World.MovementHabits = habits ?? Core.Game.Ai.Habits.MovementHabits.Default;
            actEmitter.Attach(Game);
            Practice = new PracticeLoop(Game);
            Game.World.BotTiming = Config.BotTiming;
            Game.World.SimulatedPingMs = Math.Clamp(Config.SimulatedPingMs, 0, 300);
            Game.World.BotCombatEnabled = Config.BotsAttackBoss;
            Game.World.MitPractice.Enabled = Config.MitigationPractice;
            Runs = new RunController(Game);
            if (Config.EnableUserActions) Natives.UserActions.Enable();
            ConfigWindow = new ConfigWindow(this);
            MainWindow = new MainWindow(this);

            windowSystem.AddWindow(ConfigWindow);
            windowSystem.AddWindow(MainWindow);
            windowSystem.AddWindow(MainWindow.ScenarioPanel);
            FailureBanner = new FailureBanner();
            windowSystem.AddWindow(FailureBanner);
            DeathRecapOverlay = new DeathRecapOverlay(Game);
            DeathRecapWindow = new DeathRecapWindow(Game);
            windowSystem.AddWindow(DeathRecapOverlay);
            windowSystem.AddWindow(DeathRecapWindow);
            windowSystem.AddWindow(new GuidanceOverlay(Game));
            windowSystem.AddWindow(new GuidanceResultWindow(Game));
#if DEBUG
            DamageDebugWindow = new DamageDebugWindow(this);
            windowSystem.AddWindow(DamageDebugWindow);
#endif

            if (Config.OpenSimMenuOnInn && ZoneControl.IsInInn())
                MainWindow.IsOpen = true;

            CommandManager.AddHandler(PluginIdentity.Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open FRU Practice. Subcommands: config, capture [name], looks, looks remove <number>, start, reset, leave"
            });
            commandsRegistered = true;

            PluginInterface.UiBuilder.Draw += windowSystem.Draw;
            Framework.Update += OnFrameworkUpdate;

            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            ClientState.TerritoryChanged += OnTerritoryChanged;
            ClientState.Logout += OnLogout;
            DutyState.DutyStarted += OnDutyStarted;
            DutyState.DutyWiped += OnDutyWiped;
            DutyState.DutyCompleted += OnDutyCompleted;

            // Initialize Pointers
            EventObjectManagerPointers.Initialize();
            GameMainPointers.Initialize();
            ModelContainerPointers.Initialize();
            PacketDispatcherPointers.Initialize();
            ResolvedStringPointers.Initialize();

            Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
            // The diagnostic log's own header is written before any hook exists, so it alone
            // doesn't prove a load succeeded.
            DiagnosticLog.Info("[Plugin] constructed OK -- all hooks and pointers initialized.");
        }
        catch (Exception)
        {
            // Dalamud never disposes an instance whose constructor threw; the log writer's open
            // handle would then block every later load from rotating the active log.
            Log.Warning("[Plugin] Load failed during construction -- tearing down partial state.");
            try
            {
                Dispose();
            }
            catch (Exception teardown)
            {
                Log.Warning($"[Plugin] Partial teardown after failed load threw: {teardown.Message}");
            }
            throw;
        }
    }

    // Null-tolerant throughout: the constructor's failure path calls this on a half-built
    // instance, where anything past the throwing step was never assigned.
    public void Dispose()
    {
        try
        {
            DisposeSubsystems();
        }
        finally
        {
            // Dalamud disposes this plugin's hooks once Dispose returns or throws, so a run must be
            // stopped or lifted by then even when a step above threw. A no-op after a clean Game.Dispose.
            ZoneControl.Current?.Dispose();
        }

        // Last, so it captures every other subsystem's teardown logging before the DLL unloads.
        DiagnosticLog.Shutdown();
    }

    private void DisposeSubsystems()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.Logout -= OnLogout;
        DutyState.DutyStarted -= OnDutyStarted;
        DutyState.DutyWiped -= OnDutyWiped;
        DutyState.DutyCompleted -= OnDutyCompleted;

        windowSystem.RemoveAllWindows();

        TimelineDebug.Shutdown();
        Game?.Dispose();
        actEmitter?.Uninstall();
        actBridge?.Dispose();
        Natives.Dispose();
        opcodeUpdater?.Dispose();
        LogManager?.Dispose();
        ConfigWindow?.Dispose();
        MainWindow?.Dispose();
        FailureBanner?.Dispose();
#if DEBUG
        DamageDebugWindow?.Dispose();
#endif

        if (commandsRegistered)
        {
            CommandManager.RemoveHandler(PluginIdentity.Command);
        }
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        // FrameDeltaTime, not framework.UpdateDelta: UpdateDelta is wall-clock
        // truncated to whole ms, so summing it drifts. FrameDeltaTime is the
        // full-precision delta the game ticks its own animations with.
        var fw = CSFramework.Instance();
        if (fw == null) return;
        // First: the zone guard must run when Game.Tick is paused or throwing.
        Natives.Tick(fw->FrameDeltaTime);
        try { Runs.Tick(); }
        catch (Exception e) { DiagnosticLog.Warn($"[Plugin] Runs.Tick threw: {e}"); }
        try { Game.Tick(fw->FrameDeltaTime); }
        catch (Exception e) { DiagnosticLog.Warn($"[Plugin] Game.Tick threw: {e}"); }
#if DEBUG
        try { DamageDebugWindow.Instance?.Tick(fw->FrameDeltaTime); }
        catch (Exception e) { DiagnosticLog.Warn($"[Plugin] DamageDebugWindow.Tick threw: {e}"); }
#endif
    }

    private void OnTerritoryChanged(uint territory)
    {
        ZoneControl.NoteTerritoryChanged(territory);
        var row = DataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territory);
        var isInn = row?.TerritoryIntendedUse.RowId == 2; // TerritoryIntendedUse.Inn
        if (!isInn)
        {
            var name = row?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
            LogManager.LogEnterInstance(territory, name);
        }

        if (!isInn)
        {
            MainWindow.IsOpen = false;
            return;
        }
        if (Config.OpenSimMenuOnInn)
            MainWindow.IsOpen = true;
    }

    private void OnLogout(int type, int code) => ZoneControl.NoteLogout(type, code);

    private void OnDutyStarted(IDutyStateEventArgs args)
        => LogManager.LogCombatStart(args.TerritoryType.RowId);

    private void OnDutyWiped(IDutyStateEventArgs args)
        => LogManager.LogCombatEnd(args.TerritoryType.RowId, wipe: true);

    private void OnDutyCompleted(IDutyStateEventArgs args)
        => LogManager.LogCombatEnd(args.TerritoryType.RowId, wipe: false);

    private void OnCommand(string command, string args)
    {
        var text = args.Trim();
        var space = text.IndexOf(' ');
        var verb = space < 0 ? text : text[..space];
        var rest = space < 0 ? "" : text[(space + 1)..].Trim();
        if (verb == "looks")
        {
            if (rest.StartsWith("remove", StringComparison.Ordinal)) MainTankLookCapture.Remove(rest["remove".Length..].Trim());
            else MainTankLookCapture.List();
            return;
        }
        switch (verb == "capture" ? "capture" : text)
        {
            case "config":
                ConfigWindow.Toggle();
                break;
            case "capture":
                MainTankLookCapture.Capture(rest.Length == 0 ? null : rest);
                break;
            case "start":
                Runs.Start(MainWindow.Selection, solo: false);
                break;
            case "start solo":
                Runs.Start(MainWindow.Selection, solo: true);
                break;
            case "reset":
                Runs.Reset();
                break;
            case "leave":
                Runs.Leave();
                break;
            default:
                MainWindow.Toggle();
                break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    public void OpenSettings()
    {
        ConfigWindow.IsOpen = true;
        ConfigWindow.BringToFront();
    }
}
