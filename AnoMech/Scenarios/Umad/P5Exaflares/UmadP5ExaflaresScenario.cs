using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Map;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P5Exaflares;

// UMAD P5 "exaflares" (ground-fire): two diagonal walls of fire roll across the arena, then a
// final spread. Runs solo or with bots (UmadP5ExaflaresAi).
//
// Rolling hits are animation-based (no per-tile marker, just the ExaflareOmen lane arrow): each
// eruption snapshots position as it goes off. Lingering fire is safe - only the snapshot instant kills.
public sealed class UmadP5ExaflaresScenario : IScenario, IUmadHazardSource
{
    public UmadHazards GuidanceHazards { get; } = new();

    // Where melee may keep to the boss: the stretches of this scenario's own clock with no cast to dodge, soak or face for 4 s or more.
    internal static readonly (float From, float To)[] UptimeWindows =
    [
        (0f, 6.6f),
    ];
    private const float UptimeReach = 2f;

    public string Name => "Exaflares";
    public IPhase Phase => UmadZone.P5;
    public bool SupportsSolo => true;
    public bool SupportsMitigationPractice => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new UmadP5ExaflaresAi()];

    public void DrawSettings() => settingsWindow.Draw();
    public object SettingsOverrides => settingsWindow.Overrides;
    private readonly UmadP5ExaflaresSettingsWindow settingsWindow = new();

    private UmadP5ExaflaresState state = null!;
    private SimWorld world = null!;
    private SimParty party = null!;
    private SimEnemy? kefka;

    private const float ExaflareFirstHitDelay = 4.582f; // first rolling hit, after the line launch
    private const float ExaflareHitInterval   = 0.513f; // between rolling hits
    private const int   ExaflareHitCount      = 6;
    private const float SpreadT = 25.09f;
    private const float KefkaAnimationLock = 3.1f;
    private const float HelperAnimationLock = 1.1f;

    // The current run's randomized per-run assignments, exposed so
    // MultiplayerManager can read them after a host Start and broadcast them --
    // lets a peer's local "debug: bot controls my character" mode replay the
    // same choreography a host-side bot in that role would produce. Mirrors
    // UmadP3BlackHoleScenario.LastState.
    public UmadP5ExaflaresState? LastState { get; private set; }

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        GuidanceHazards.Clear();
        foreach (var (from, to) in UptimeWindows) worldParam.Uptime.Allow(from, to, UptimeReach);
        Play(worldParam, selectedAi, Roll(worldParam), new UmadP5Cast());
    }

    internal UmadP5ExaflaresState Roll(SimWorld worldParam) => new(worldParam.Rng, settingsWindow.Overrides);

    // A party arriving from an earlier mechanic is already spread, and still stacked for it when the
    // walls are cast: its bots skip the opening fan-out.
    internal void Play(SimWorld worldParam, int? selectedAi, UmadP5ExaflaresState stateParam, UmadP5Cast cast)
    {
        world = worldParam;
        party = worldParam.Party;
        state = stateParam;
        LastState = state;

        if (selectedAi is { } idx && idx < AiStrats.Count)
            new UmadP5ExaflaresAi { FansOut = !cast.Chained }.Run(state, world);
        if (!cast.Chained)
            world.MitPractice.Begin(UmadP5ExaflaresMitigation.Plan(P5Full.UmadP5TankPair.FirstInvuln(party)),
                new Dictionary<MitSource, Func<SimCharacter?>> { [UmadMitigation.Kefka] = () => kefka });

        world.Events.Add(0f, () => kefka = cast.Kefka ??= UmadP5Cast.SpawnKefka(world));
        world.Events.Add(2.89f, () => { if (kefka is { } boss) world.Map.DirectorUpdate(0x80000027U, 0x25U, 0x2U, 0x1BDBU, boss.EntityId); });
        world.Events.Add(3.0f, () => kefka?.Cast(ActionId.ChaosEnd1, animationLock: KefkaAnimationLock));

        // Rolling exaflares: left/right pairs every 2.5s, columns from the chosen order.
        var rollingHits = new List<(float T, Vector3 Position)>();
        rollingHits.AddRange(LaunchExaflareLine(3.0f,  state.LeftOrder[0],  isLeft: true));
        rollingHits.AddRange(LaunchExaflareLine(3.0f,  state.LeftOrder[1],  isLeft: true));
        rollingHits.AddRange(LaunchExaflareLine(5.5f,  state.RightOrder[0], isLeft: false));
        rollingHits.AddRange(LaunchExaflareLine(5.5f,  state.RightOrder[1], isLeft: false));
        rollingHits.AddRange(LaunchExaflareLine(8.0f,  state.LeftOrder[2],  isLeft: true));
        rollingHits.AddRange(LaunchExaflareLine(8.0f,  state.LeftOrder[3],  isLeft: true));
        rollingHits.AddRange(LaunchExaflareLine(10.5f, state.RightOrder[2], isLeft: false));
        rollingHits.AddRange(LaunchExaflareLine(10.5f, state.RightOrder[3], isLeft: false));
        rollingHits.AddRange(LaunchExaflareLine(13.0f, state.LeftOrder[4],  isLeft: true));
        rollingHits.AddRange(LaunchExaflareLine(13.0f, state.LeftOrder[5],  isLeft: true));
        rollingHits.AddRange(LaunchExaflareLine(15.5f, state.RightOrder[4], isLeft: false));
        rollingHits.AddRange(LaunchExaflareLine(15.5f, state.RightOrder[5], isLeft: false));

        world.Events.Add(19.10f, () => kefka?.VoiceLine(8206500));
        world.Events.Add(19.2f, () => kefka?.Cast(ActionId.ChaosEnd2, animationLock: KefkaAnimationLock));
        var spreadSpots = LastRollingHitSpots(rollingHits, 8);
        world.Events.Add(SpreadT, () => ResolveSpread(spreadSpots));
    }

    public void Tick(float delta, float elapsed)
    {
        state?.SpreadTick?.Invoke(delta); // bot spread relaxation (no-op in solo)
    }

    // One rolling line of fire. `lineIdx` 1-6 faces the source; `isLeft` = top-left wall. The arrow
    // telegraph appears at launch and its release is the origin eruption; the rolling hits follow.
    private IEnumerable<(float T, Vector3 Position)> LaunchExaflareLine(float startT, int lineIdx, bool isLeft)
    {
        var initPos = isLeft
            ? new Vector3(-35f + 5f * lineIdx, 0f, -5f * lineIdx)
            : new Vector3(5f * lineIdx, 0f, -35f + 5f * lineIdx);
        var dPos = isLeft ? new Vector3(5f, 0f, 5f) : new Vector3(-5f, 0f, 5f);
        var heading = MathF.PI * (isLeft ? 1f : -1f) / 4f;

        world.Events.Add(startT, () =>
            kefka?.CastFromHelper(ActionId.ExaflareOmen, at: new Placement(initPos, heading), animationLock: HelperAnimationLock));

        var hits = new List<(float T, Vector3 Position)>();
        for (var i = 0; i < ExaflareHitCount; i++)
        {
            var pos = initPos + dPos * (i + 1);
            var tErupt = startT + ExaflareFirstHitDelay + ExaflareHitInterval * i;
            hits.Add((tErupt, pos));
            world.Events.Add(tErupt, () => kefka?.CastFromHelper(UmadActions.ExaflareHit, at: new Placement(pos, 0f)));
        }
        return hits;
    }

    // Final spread: every alive member is the target of their own spread, so a member is caught by
    // their own plus any overlapping one. All spreads resolve in this one event, so the vuln-up the
    // first covering leaves makes the second lethal.
    private void ResolveSpread(Vector3[] spreadSpots)
    {
        for (int i = 0; i < 8; i++)
        {
            var member = party.Get(i);
            if (member is null || !member.IsAlive()) continue;
            kefka?.CastFromHelper(UmadActions.ExaflareSpread, member, at: new Placement(spreadSpots[i], 0f));
        }
    }

    // Each spread fires from one of the last exaflare tiles.
    private static Vector3[] LastRollingHitSpots(IEnumerable<(float T, Vector3 Position)> rollingHits, int count) =>
        rollingHits.OrderByDescending(h => h.T).Select(h => h.Position).Distinct().Take(count).ToArray();





    // A peer never runs this scenario's Tick, so the spread relaxation is driven from here.

}
