using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P5Full;

// P5 from Ultima Kefka's arrival to Forsaken Null, on the real clock against one Kefka: Ultima
// Repeater and Fell Forces, Flood, Maddening Orchestra, Celestriad, Stray Apocalypse and Stray
// Entropy, Orchestra again, then Forsaken. The sim has no boss HP: Kefka dies at the settings' kill
// time, or lives to land Forsaken Null.
public sealed class UmadP5FullScenario : IScenario, IUmadHazardSource
{
    public UmadHazards GuidanceHazards { get; } = new();

    // The Celestriad towers hold their soakers through 53-81, and Fell Forces stacks want the party on its strat spots, so no window sits near a round.
    // Where melee may keep to the boss: the stretches of this scenario's own clock with no cast to dodge, soak or face for 4 s or more.
    internal static readonly (float From, float To)[] UptimeWindows =
    [
        (30.5f, 37.7f), (123.3f, 129.8f), (150f, 158.8f),
    ];
    private const float UptimeReach = 2f;

    public string Name => "Full phase";
    public IPhase Phase => UmadZone.P5;
    public bool SupportsMitigationPractice => true;
    public float BgmSecondsAtStart => flood.BgmSecondsAtStart - FloodAt;
    public IReadOnlyList<IScenarioAi> AiStrats { get; }
    public object SettingsOverrides => settingsWindow.Overrides;
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => celestriad.DrawPerPlayerSettings();
    public IReadOnlyList<string> SettingsConflicts => celestriad.SettingsConflicts;

    internal static readonly float[] RepeaterAt = [3.12f, 85.04f];
    internal static readonly float[][] RepeaterHitsAt = [[8.21f, 8.92f, 9.59f, 10.3f], [90.12f, 90.83f, 91.5f, 92.21f]];
    internal static readonly (float Telegraph, float Stacks)[] FellForcesAt =
    [
        (12.26f, 13.07f), (15.43f, 16.23f), (18.55f, 19.36f),
        (49.2f, 50f), (52.32f, 53.12f),
        (94.18f, 94.98f), (97.35f, 98.15f),
        (141.27f, 142.07f), (144.39f, 145.19f), (147.52f, 148.32f),
    ];
    // Each drill's own casts land on the timeline's times this far into the run.
    internal const float FloodAt = 18.66f;
    internal static readonly float[] OrchestraAt = [32.84f, 124.91f];
    internal const float CelestriadAt = 53.55f;
    internal const float ExaflaresAt = 96.58f;

    private static readonly Placement Centre = new(Vector3.Zero, MathF.PI);

    private readonly UmadP5FloodScenario flood = new();
    private readonly UmadP5CelestriadScenario celestriad = new();
    private readonly UmadP5ExaflaresScenario exaflares = new();
    private readonly UmadP5Forsaken forsaken = new();
    private readonly UmadP5FullSettingsWindow settingsWindow;
    // Only the drill playing hears the sim's events; Forsaken hears its own.
    private IScenario? current;
    private float currentAt;
    private SimWorld? world;
    private UmadP5Enmity? enmity;

    internal UmadP5Enmity? Enmity => enmity;

    public UmadP5FullScenario()
    {
        settingsWindow = new UmadP5FullSettingsWindow(flood, celestriad, exaflares);
        AiStrats = [new UmadP5FullStrat(0, 0, 0, flood.AiStrats[0].Name, celestriad.AiStrats[0].Name, exaflares.AiStrats[0].Name)];
    }

    public void DrawSettings() => settingsWindow.Draw();

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        GuidanceHazards.Clear();
        foreach (var (from, to) in UptimeWindows) worldParam.Uptime.Allow(from, to, UptimeReach);
        world = worldParam;
        current = null;
        var strat = selectedAi is { } idx && idx < AiStrats.Count ? (UmadP5FullStrat)AiStrats[idx] : null;
        var state = new UmadP5FullState(flood.Roll(world), celestriad.Roll(world), exaflares.Roll(world), world.Rng, settingsWindow.Overrides);
        var cast = new UmadP5Cast { Chained = true };
        enmity = new UmadP5Enmity(world, cast);
        Natives.UserActions.Acted -= OnPlayerActed;
        Natives.UserActions.Acted += OnPlayerActed;
        if (strat is not null) new UmadP5FullAi(state, enmity).Run(world);
        world.MitPractice.Begin(MitigationPlan(UmadP5TankPair.FirstInvuln(world.Party)),
            new Dictionary<MitSource, Func<SimCharacter?>> { [UmadMitigation.Kefka] = () => cast.Kefka });
        var w = world;
        var holder = enmity;

        w.Events.Add(0f, () => cast.Kefka = UmadP5Cast.SpawnKefka(w));
        w.Events.Add(1f, () => holder.Take(holder.Holder));
        w.Events.Add(RepeaterAt[0], () => cast.Kefka?.Cast(ActionId.UltimaRepeater, animationLock: 3.1f));
        foreach (var hit in RepeaterHitsAt[0]) w.Events.Add(hit, () => cast.Kefka?.CastFromHelper(UmadActions.UltimaRepeaterHit, at: Centre));
        for (var round = 0; round < 3; round++) FellForces(w, cast, state, round);
        w.Events.Add(FloodAt, () => Start(flood, FloodAt, () => flood.Play(w, strat?.Flood, state.Flood, cast)));
        w.Events.Add(OrchestraAt[0], () => UmadP5Orchestra.Play(w, cast, holder, state.OrchestraHoly[0]));
        for (var round = 3; round < 5; round++) FellForces(w, cast, state, round);
        w.Events.Add(CelestriadAt, () => Start(celestriad, CelestriadAt, () => celestriad.Play(w, strat?.Celestriad, state.Celestriad, cast)));
        w.Events.Add(RepeaterAt[1], () => cast.Kefka?.Cast(ActionId.UltimaRepeater, animationLock: 3.1f));
        foreach (var hit in RepeaterHitsAt[1]) w.Events.Add(hit, () => cast.Kefka?.CastFromHelper(UmadActions.UltimaRepeaterHit, at: Centre));
        for (var round = 5; round < 7; round++) FellForces(w, cast, state, round);
        w.Events.Add(ExaflaresAt, () => Start(exaflares, ExaflaresAt, () => exaflares.Play(w, strat?.Exaflares, state.Exaflares, cast)));
        w.Events.Add(OrchestraAt[1], () => UmadP5Orchestra.Play(w, cast, holder, state.OrchestraHoly[1]));
        for (var round = 7; round < 10; round++) FellForces(w, cast, state, round);
        forsaken.Schedule(w, cast, state.Forsaken);
        if (state.BossSurvives) w.Events.Add(UmadP5Forsaken.NullLandsAt, () => ForsakenNullLands(w, cast));
        else w.Events.Add(state.KillAt, () => KillKefka(cast));
    }

    // The slices in the order they play, each drill between the segments of the phase around it.
    internal static MitPlanData MitigationPlan(PartyRole firstInvuln) => MitPlanChain.Of(
        (UmadP5FullMitigation.Opening(firstInvuln), 0f),
        (UmadP5FloodMitigation.Plan(), FloodAt),
        (UmadP5FullMitigation.FirstOrchestra(firstInvuln), 0f),
        (UmadP5CelestriadMitigation.Plan(firstInvuln), CelestriadAt),
        (UmadP5FullMitigation.SecondRepeater(firstInvuln), 0f),
        (UmadP5ExaflaresMitigation.Plan(firstInvuln), ExaflaresAt),
        (UmadP5FullMitigation.SecondOrchestra(firstInvuln), 0f),
        (UmadP5FullMitigation.Forsaken(firstInvuln), 0f));

    // Kefka's tell, then the three role stacks: the tank one on his highest enmity.
    private void FellForces(SimWorld w, UmadP5Cast cast, UmadP5FullState state, int round)
    {
        var (telegraph, stacks) = FellForcesAt[round];
        var roll = state.FellForces[round];
        w.Events.Add(telegraph, () => cast.Kefka?.Cast(ActionId.FellForces, animationLock: 1.1f));
        w.Events.Add(stacks, () =>
        {
            Stack(w, cast, UmadActions.FellForcesTank, [enmity!.Holder, enmity.Second]);
            Stack(w, cast, UmadActions.FellForcesHealer, [roll.Healer, PartyRole.RegenHealer, PartyRole.ShieldHealer]);
            Stack(w, cast, UmadActions.FellForcesDps,
                [roll.Dps, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps]);
        });
    }

    // On the first alive of `targets`: a stack the roll put on someone down goes to another of their role.
    private static void Stack(SimWorld w, UmadP5Cast cast, Core.EnemyActions.EnemyAction action, PartyRole[] targets)
    {
        if (targets.Select(w.Party.Get).FirstOrDefault(member => member.IsAlive()) is { } target)
            cast.Kefka?.CastFromHelper(action, target, at: Centre);
    }

    private static void KillKefka(UmadP5Cast cast)
    {
        cast.Kefka?.Defeat();
        cast.Kefka?.SetTargetable(false);
    }

    private static void ForsakenNullLands(SimWorld w, UmadP5Cast cast)
    {
        if (cast.Kefka.IsAlive()) w.Party.WipeAllPlayers(ActionId.ForsakenNull, "enrage: Kefka still alive when it landed");
    }

    private void OnPlayerActed(PlayerAction action)
    {
        if (action is not { Step: PlayerActionStep.Resolved, ActionId: UmadP5Enmity.Provoke } || world is null || enmity is null) return;
        if (world.Party.PlayerRole.IsTank()) enmity.Take(world.Party.PlayerRole);
    }

    private void Start(IScenario slice, float at, Action play)
    {
        current = slice;
        currentAt = at;
        play();
    }

    public void RunInstanceEvents(SimWorld instanceWorld) => flood.RunInstanceEvents(instanceWorld);

    public void Tick(float delta, float elapsed)
    {
        current?.Tick(delta, elapsed - currentAt);
        forsaken.Tick();
    }

    public void OnActionResolved(ActionResolved resolved)
    {
        current?.OnActionResolved(resolved);
        forsaken.OnActionResolved(resolved);
    }

    public void OnStatusAdded(StatusAdded added) => current?.OnStatusAdded(added);

    public void OnStatusLost(StatusLost lost) => current?.OnStatusLost(lost);

    public void OnTetherEnded(TetherEnded ended) => current?.OnTetherEnded(ended);
}
