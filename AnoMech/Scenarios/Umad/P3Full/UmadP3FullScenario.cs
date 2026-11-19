using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P3Full;

// P3 from the bosses' arrival to their fall: the opening, the Limit Cut and the Black Hole drills on
// one clock, with one Chaos, Exdeath and Kefka, the buster and Max between the drills, and the end.
public sealed class UmadP3FullScenario : IScenario, IUmadHazardSource, IBotTargetPicker
{
    public UmadHazards GuidanceHazards { get; } = new();

    // Where melee may keep to the boss: the stretches of this scenario's own clock with no cast to dodge, soak or face for 4 s or more.
    internal static readonly (float From, float To)[] UptimeWindows =
    [
        (0f, 33.8f), (44.4f, 50f), (55.3f, 63.5f), (71.3f, 78.2f), (92.7f, 101.1f), (113.9f, 125.8f), (131.1f, 138.1f), (152.2f, 157f), (159.5f, 164.1f), (182.6f, 187.6f), (213.9f, 221.9f), (236.3f, 243.8f), (250.6f, 255.3f), (266.6f, 271.4f),
    ];
    private const float UptimeReach = 2f;

    public SimEnemy? TargetFor(PartyRole role) => tanks.BossFor(role, cast?.Chaos, cast?.Exdeath);

    private UmadP3Tanks tanks = UmadP3Tanks.MainOnChaos;
    private UmadP3Cast? cast;

    public string Name => "Full phase";
    public IPhase Phase => UmadZone.P3;
    public bool SupportsMitigationPractice => true;
    // The Limit Cut's track position, 71.1 s earlier.
    public float BgmSecondsAtStart => 37.57f;
    public IReadOnlyList<IScenarioAi> AiStrats { get; }
    public object SettingsOverrides => settingsWindow.Overrides;
    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public IReadOnlyList<string> SettingsConflicts =>
    [
        .. settingsWindow.Overrides.OpeningRolls.Validate().Problems,
        .. limitCut.SettingsConflicts,
        .. blackHole.SettingsConflicts,
    ];

    // Each drill's own casts land on the timeline's times this far into the run.
    internal const float LimitCutAt = 71.1f;
    internal const float BlackHoleAt = 125.72f;

    // The Limit Cut casts the third Thunder III as it ends; its hits come after the Black Hole has begun.
    internal const float BridgeThunderCastAt = LimitCutAt + 50.746f;
    internal static readonly float[] BridgeThunderHitsAt = [126.80f, 129.83f];
    internal const float MaxAt = 123.63f;
    internal const float MaxResolvesAt = 128.61f;
    internal const float BlizzardCastAt = 282.74f;
    internal const float BigBangCastAt = 282.94f;
    internal const float BlizzardAt = 286.72f;
    internal const float BigBangAt = 288.04f;
    internal const float ChaosFallsAt = 291.2f;
    internal const float ExdeathFallsAt = 292.0f;
    internal const float PhaseEndsAt = 297.7f;

    private readonly UmadP3Opening opening = new();
    private readonly UmadP3LimitCutScenario limitCut = new();
    private readonly UmadP3BlackHoleScenario blackHole = new();
    private readonly UmadP3FullSettingsWindow settingsWindow;
    // Only the slice playing hears the sim's events: each one's checks are about its own mechanics.
    private IWorldEventListener? current;
    private float currentAt;

    public UmadP3FullScenario()
    {
        settingsWindow = new UmadP3FullSettingsWindow(limitCut, blackHole);
        AiStrats = [.. blackHole.AiStrats.Select((ai, i) => new UmadP3FullStrat(0, i, limitCut.AiStrats[0].Name, ai.Name))];
    }

    public void Run(SimWorld world, int? selectedAi)
    {
        GuidanceHazards.Clear();
        foreach (var (from, to) in UptimeWindows) world.Uptime.Allow(from, to, UptimeReach);
        current = null;
        var strat = selectedAi is { } idx && idx < AiStrats.Count ? (UmadP3FullStrat)AiStrats[idx] : null;
        var state = Roll(world);
        var cast = this.cast = new UmadP3Cast { Chained = true };
        tanks = state.Tanks;
        if (strat is not null) new UmadP3FullAi().Run(world, state, cast);
        world.MitPractice.Begin(MitigationPlan(state.Tanks, Busters(state)), new Dictionary<MitSource, Func<SimCharacter?>>
        {
            [UmadMitigation.Chaos] = () => cast.Chaos,
            [UmadMitigation.Exdeath] = () => cast.Exdeath,
        });

        world.Events.Add(0f, () => Start(opening, 0f, () => opening.Play(world, strat is not null, state.Opening, cast)));
        world.Events.Add(LimitCutAt, () => Start(limitCut, LimitCutAt, () => limitCut.Play(world, strat?.LimitCut, state.LimitCut, cast)));
        world.Events.Add(MaxAt, () => cast.Kefka?.Cast(ActionId.Max, animationLock: 1.1f));
        world.Events.Add(BlackHoleAt, () => Start(blackHole, BlackHoleAt, () => blackHole.Play(world, strat?.BlackHole, state.BlackHole, cast)));
        world.Events.Add(BridgeThunderHitsAt[0], () => ResolveThunder(world, cast));
        world.Events.Add(MaxResolvesAt, () => cast.Kefka?.AddStatus(StatusId.Max, stacks: 506, overrideStacks: true));
        world.Events.Add(BridgeThunderHitsAt[1], () => ResolveThunder(world, cast));
        world.Events.Add(BridgeThunderHitsAt[1] + 0.5f, () => cast.Exdeath?.Follow(world.Party.Get(state.Tanks.Exdeath)));
        world.Events.Add(BlizzardAt, () => FreezeTheStill(world));
        world.Events.Add(BigBangAt, () => cast.Chaos?.CastFromHelper(UmadActions.BigBang, at: new Placement(Vector3.Zero, 0f)));
        world.Events.Add(ChaosFallsAt, () => cast.Chaos?.SetTargetable(false));
        world.Events.Add(ExdeathFallsAt, () => cast.Exdeath?.SetTargetable(false));
        world.Events.Add(PhaseEndsAt, () =>
        {
            cast.Chaos?.FadeOut();
            cast.Exdeath?.FadeOut();
        });
    }

    // The tank seats from the pair's jobs, each Thunder III plan onto them, and the rolls in the order
    // the drills read the run's dice.
    internal UmadP3FullState Roll(SimWorld world)
    {
        var overrides = settingsWindow.Overrides;
        var tanks = UmadP3Tanks.For(world.Party);
        var limitCutState = limitCut.Roll(world, tanks, tanks.Seats(overrides.Thunder2));
        var openingState = new UmadP3OpeningState(world.Rng, world.Party.PlayerRole, overrides.OpeningRolls, limitCutState, tanks.Seats(overrides.Thunder1));
        var blackHoleState = blackHole.Roll(world, tanks, tanks.Seats(overrides.Thunder4), tanks.Seats(overrides.Thunder5));
        return new UmadP3FullState(tanks, openingState, limitCutState, tanks.Seats(overrides.Thunder3), blackHoleState);
    }

    // The slices in the order they play; the busters' tank rows, planned over the whole phase, last.
    // `busters` is the five Thunder III sets' plans, in order.
    internal static MitPlanData MitigationPlan(UmadP3Tanks tanks, IReadOnlyList<ThunderIIIAssignment> busters) => MitPlanChain.Of(
        (UmadP3OpeningMitigation.Plan(tanks), 0f),
        (UmadP3LimitCutMitigation.Plan(tanks, thunder: null), LimitCutAt),
        (UmadP3BlackHoleMitigation.Plan(tanks, busters: null), BlackHoleAt),
        (UmadP3FullMitigation.Plan(busters), 0f));

    internal static IReadOnlyList<ThunderIIIAssignment> Busters(UmadP3FullState state)
        => [state.Opening.Thunder, state.LimitCut.ThunderPlan, state.BridgeThunder, state.BlackHole.ThunderSet1, state.BlackHole.ThunderSet2];

    public void RunInstanceEvents(SimWorld world)
    {
        Natives.TimelinePreload.Preload(UmadP3LimitCutScenario.CloneTimelines, "UmadP3Full");
        world.Party.LimitBreak.Set(3f);
    }

    private void Start(IWorldEventListener slice, float at, Action play)
    {
        current = slice;
        currentAt = at;
        play();
    }

    public void Tick(float delta, float elapsed)
    {
        if (current is IScenario scenario) scenario.Tick(delta, elapsed - currentAt);
    }

    public void OnActionResolved(ActionResolved resolved) => current?.OnActionResolved(resolved);

    public void OnStatusAdded(StatusAdded added) => current?.OnStatusAdded(added);

    public void OnStatusLost(StatusLost lost) => current?.OnStatusLost(lost);

    public void OnTetherEnded(TetherEnded ended) => current?.OnTetherEnded(ended);

    private static void ResolveThunder(SimWorld world, UmadP3Cast cast)
    {
        if (cast.Exdeath is not { } exdeath) return;
        exdeath.CastFromHelper(UmadActions.ThunderIII, world.Party.Find.Closest(exdeath.Position));
    }

    // Blizzard III freezes whoever stands still as it lands. Only the human is judged: a bot never stops.
    private static void FreezeTheStill(SimWorld world)
    {
        if (world.Party.Player is not { } player || !player.IsAlive() || player.IsMoving) return;
        player.AddStatus(StatusId.DeepFreeze, 10f);
        player.DieOfStatus(StatusId.DeepFreeze, "stood still through Exdeath's Blizzard III");
    }
}
