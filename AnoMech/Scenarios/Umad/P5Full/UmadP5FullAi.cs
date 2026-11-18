using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P5Full;

public sealed class UmadP5FullAi(UmadP5FullState state, UmadP5Enmity enmity)
{
    private const string Guide = "https://materiaraiding.com/ultimate/dmu";
    private const string TankSheet = "Ikuya Mitty (DMU), Omnitank P5";

    private const float Lead = 0.5f;
    private const float AfterSnapshot = 0.05f;
    private const float StackRoom = 0.5f;
    private const float TightRoom = 0.3f;
    private const float StackJitter = 0.3f;
    private const float TightJitter = 0.2f;
    // Chosen: the Holy tank provokes this long after its debuff lands, and invulns this long before it
    // runs out.
    private const float ProvokeReaction = 0.6f;
    private const float InvulnLead = 0.7f;
    private const float StartingProvokeAt = 1.5f;
    private const float CelestriadDoneAt = 81f;

    // Where clears stack for Fell Forces; the spots inside a group are chosen.
    private static readonly Vector3 TankStack = new(0f, 0f, -7f);
    private static readonly Vector3 HealerStack = new(-4.7f, 0f, 5.3f);
    private static readonly Vector3 DpsStack = new(5.8f, 0f, 6f);
    // Chosen: the tank out of the solo rounds' tank stack waits here.
    private static readonly Vector3 SoloRoundsWait = new(-4.5f, 0f, -10f);
    // Chosen: the first notes' clock spots, 11 out and 3.5 either side.
    private const float SpreadReach = 11f;
    private const float SpreadSide = 3.5f;
    // Chosen: the second notes' Holy baits, 5 out on E, S, W.
    private const float BaitReach = 5f;
    // Chosen: the tanks' Chaotic Flare stack, then the Flare tank's spot on the north edge.
    private const float ChaoticFlareZ = -9.5f;
    private static readonly Vector3 FlareEdge = new(0f, 0f, -18.5f);
    // Chosen: where the rest wait for the finale.
    private static readonly Vector3[] FinaleSpots = [new(12f, 0f, 6f), new(0f, 0f, 12.5f), new(-12f, 0f, 6f)];
    // Chosen: Forsaken's start on waymark C, where clears stand, and the clump around each spot.
    private static readonly Vector3 ForsakenStart = new(0f, 0f, 10.5f);
    private const float ClumpReach = 0.45f;

    private readonly PartyRole?[] holyTanks = new PartyRole?[UmadP5FullScenario.OrchestraAt.Length];

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0.2f, At(FellForcesSpot), jitter: StackJitter, knownAt: 0.2f, room: StackRoom,
            arrivalTime: UmadP5FullScenario.FellForcesAt[0].Stacks - Lead, cue: FellForcesCue);
        world.Events.Add(StartingProvokeAt, () => TakeKefkaForTheFirstFlare(world));
        for (var round = 0; round < holyTanks.Length; round++) Orchestra(ai, world, round);
        ai.Move(CelestriadDoneAt, At(FellForcesSpot), jitter: StackJitter, knownAt: CelestriadDoneAt, knownBy: CueKind.Resolve,
            room: StackRoom, arrivalTime: UmadP5FullScenario.FellForcesAt[5].Stacks - Lead, cue: FellForcesCue);
        Forsaken(ai, world);
    }

    private void TakeKefkaForTheFirstFlare(SimWorld world)
    {
        if (TankJobs(world) is not var (main, off)) return;
        var starter = UmadP5TankPair.StartsWithBoss(main, off);
        if (starter != enmity.Holder) enmity.ProvokeBy(starter);
    }

    private static (JobId Main, JobId Off)? TankJobs(SimWorld world)
        => world.Party.Get(PartyRole.MainTank) is ISimPartyMember main && world.Party.Get(PartyRole.OffTank) is ISimPartyMember off
            ? ((JobId)main.ClassJob, (JobId)off.ClassJob)
            : null;

    private void Orchestra(AiManager ai, SimWorld world, int round)
    {
        var at = UmadP5FullScenario.OrchestraAt[round];
        var firstNotes = at + UmadP5Orchestra.FirstNotesAt;
        var surprise = at + UmadP5Orchestra.SurpriseAt;
        var secondNotes = at + UmadP5Orchestra.SecondNotesAt;
        var finale = at + UmadP5Orchestra.FinaleAt;
        var next = Array.FindIndex(UmadP5FullScenario.FellForcesAt, f => f.Stacks > finale);
        var soloRounds = UmadP5FullScenario.FellForcesAt[next..(next + 2)];

        ai.Move(at, At(FirstNotesSpot), jitter: StackJitter, knownAt: at, room: StackRoom, arrivalTime: firstNotes - Lead,
            cue: new StratCue("Maddening Orchestra", _ => "spread on your clock spot: tanks N, healers W, ranged E, melee S", Guide));
        ai.Move(firstNotes + AfterSnapshot, At(role => SecondNotesSpot(round, role)), jitter: StackJitter, knownAt: firstNotes + AfterSnapshot,
            knownBy: CueKind.Resolve, room: StackRoom, arrivalTime: secondNotes - Lead,
            cue: new StratCue("Maddening Orchestra", role => SecondNotesWhy(round, role), Guide));
        world.Events.Add(surprise + AfterSnapshot, () => holyTanks[round] = HolderOf(world, StatusId.SurpriseHoly));
        world.Events.Add(surprise + ProvokeReaction, () => { if (holyTanks[round] is { } tank) enmity.ProvokeBy(tank); });
        world.Events.Add(surprise + UmadP5Orchestra.SurpriseDuration - InvulnLead, () => Invuln(world, holyTanks[round]));
        ai.Move(secondNotes + AfterSnapshot, At(role => FinaleSpot(round, role)), jitter: StackJitter, knownAt: secondNotes + AfterSnapshot,
            knownBy: CueKind.Resolve, room: StackRoom, arrivalTime: finale - Lead,
            cue: new StratCue("Maddening Orchestra", role => FinaleWhy(round, role), Guide));
        ai.Move(finale + AfterSnapshot, At(role => SoloRoundsSpot(round, role)), jitter: StackJitter, knownAt: finale + AfterSnapshot,
            knownBy: CueKind.Resolve, room: StackRoom, arrivalTime: soloRounds[0].Stacks - Lead,
            cue: new StratCue("Fell Forces", role => SoloRoundsWhy(round, role), TankSheet));
        if (round == 1)
            ai.Move(soloRounds[1].Stacks + AfterSnapshot, At(role => role.IsTank() && role != holyTanks[round] ? FellForcesSpot(role) : null),
                jitter: StackJitter, knownAt: soloRounds[1].Stacks + AfterSnapshot, knownBy: CueKind.Resolve, room: StackRoom,
                arrivalTime: UmadP5FullScenario.FellForcesAt[next + 2].Stacks - Lead,
                cue: new StratCue("Fell Forces", _ => "invulnerability over: share the third tank stack", TankSheet));
    }

    private static PartyRole? HolderOf(SimWorld world, ushort statusId)
        => ((PartyRole[])[PartyRole.MainTank, PartyRole.OffTank]).Cast<PartyRole?>()
            .FirstOrDefault(tank => world.Party.Get(tank!.Value) is { } member && member.HasStatus(statusId));

    private static void Invuln(SimWorld world, PartyRole? tank)
    {
        if (tank is not { } role || world.Party.Get(role) is not SimPartyNpc bot || !bot.IsAlive()) return;
        bot.UseAction((JobId)bot.ClassJob switch
        {
            JobId.Warrior => MitActionId.Holmgang,
            JobId.Paladin => MitActionId.HallowedGround,
            JobId.Gunbreaker => MitActionId.Superbolide,
            _ => MitActionId.LivingDead,
        });
    }

    private static Vector3 FellForcesSpot(PartyRole role) => role switch
    {
        PartyRole.MainTank => TankStack + new Vector3(-0.6f, 0f, 0f),
        PartyRole.OffTank => TankStack + new Vector3(0.6f, 0f, 0f),
        PartyRole.RegenHealer => HealerStack + new Vector3(-0.6f, 0f, 0f),
        PartyRole.ShieldHealer => HealerStack + new Vector3(0.6f, 0f, 0f),
        PartyRole.MeleeDpsA => DpsStack + new Vector3(-0.6f, 0f, -0.6f),
        PartyRole.MeleeDpsB => DpsStack + new Vector3(0.6f, 0f, -0.6f),
        PartyRole.PhysRangedDps => DpsStack + new Vector3(-0.6f, 0f, 0.6f),
        _ => DpsStack + new Vector3(0.6f, 0f, 0.6f),
    };

    private static readonly StratCue FellForcesCue = new("Fell Forces", role => role switch
    {
        PartyRole.MainTank or PartyRole.OffTank => "tank stack north of Kefka",
        PartyRole.RegenHealer or PartyRole.ShieldHealer => "healer stack south-west",
        _ => "DPS stack south-east",
    }, Guide);

    private static Vector3 FirstNotesSpot(PartyRole role) => role switch
    {
        PartyRole.MainTank => new(-SpreadSide, 0f, -SpreadReach),
        PartyRole.OffTank => new(SpreadSide, 0f, -SpreadReach),
        PartyRole.RegenHealer => new(-SpreadReach, 0f, SpreadSide),
        PartyRole.ShieldHealer => new(-SpreadReach, 0f, -SpreadSide),
        PartyRole.PhysRangedDps => new(SpreadReach, 0f, -SpreadSide),
        PartyRole.CasterDps => new(SpreadReach, 0f, SpreadSide),
        PartyRole.MeleeDpsA => new(SpreadSide, 0f, SpreadReach),
        _ => new(-SpreadSide, 0f, SpreadReach),
    };

    private static readonly (string Name, Vector3 Spot)[] BaitSpots =
        [("E", new(BaitReach, 0f, 0f)), ("S", new(0f, 0f, BaitReach)), ("W", new(-BaitReach, 0f, 0f))];

    private IReadOnlyDictionary<PartyRole, int> BaitsSharedForTheShortestLongestWalk(int round)
    {
        PartyRole[] baiters = [.. Enum.GetValues<PartyRole>().Where(role => !role.IsTank() && !state.OrchestraHoly[round].Contains(role))];
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        var best = orders.MinBy(order => baiters.Select((role, i) => Vector3.Distance(FirstNotesSpot(role), BaitSpots[order[i]].Spot)).DefaultIfEmpty().Max())!;
        return baiters.Select((role, i) => (role, i)).ToDictionary(b => b.role, b => best[b.i]);
    }

    private Vector3? SecondNotesSpot(int round, PartyRole role)
    {
        if (role.IsTank()) return new(role == PartyRole.MainTank ? -0.5f : 0.5f, 0f, ChaoticFlareZ);
        return BaitsSharedForTheShortestLongestWalk(round).TryGetValue(role, out var bait) ? BaitSpots[bait].Spot : null;
    }

    private string SecondNotesWhy(int round, PartyRole role)
    {
        if (role.IsTank()) return "stack with the other tank north for Chaotic Flare";
        return BaitsSharedForTheShortestLongestWalk(round).TryGetValue(role, out var bait)
            ? $"Holy missed you: bait the next one in Kefka's hitbox, {BaitSpots[bait].Name}"
            : "Holy hit you: hold your clock spot";
    }

    private Vector3? FinaleSpot(int round, PartyRole role)
    {
        if (role.IsTank()) return role == holyTanks[round] ? null : FlareEdge;
        var from = SecondNotesSpot(round, role) ?? FirstNotesSpot(role);
        return Clump(FinaleSpots.MinBy(spot => Vector3.Distance(spot, from)), role, StackRoom + 0.1f);
    }

    private string FinaleWhy(int round, PartyRole role)
    {
        if (!role.IsTank()) return "away from the north edge, out of Flare Diffusion";
        return role == holyTanks[round] ? "Surprise Holy: hold Kefka, invulnerable before it runs out" : "Surprise Flare: north edge, away from everyone";
    }

    private Vector3 SoloRoundsSpot(int round, PartyRole role)
        => role.IsTank() && role != holyTanks[round] ? SoloRoundsWait : FellForcesSpot(role);

    private string SoloRoundsWhy(int round, PartyRole role)
    {
        if (!role.IsTank()) return FellForcesCue.Why(role);
        return role == holyTanks[round] ? "invulnerable: take the tank stacks alone" : "stay out of the invulnerable tank's stacks";
    }

    private void Forsaken(AiManager ai, SimWorld world)
    {
        var start = UmadP5FullScenario.FellForcesAt[^1].Stacks + AfterSnapshot;
        var cue = new StratCue("Forsaken", _ => "stack on the marker; then the waymark clockwise past the orange (C, 4, 1, 2, 3)", Guide);
        ai.Move(start, At(role => Clump(ForsakenStart, role)), jitter: TightJitter, knownAt: start, knownBy: CueKind.Resolve, room: TightRoom,
            arrivalTime: UmadP5Forsaken.MarkerAt[0] - Lead, cue: cue);
        var exit = UmadP5Forsaken.PuddleRadius + 0.5f;
        foreach (var drop in UmadP5Forsaken.PuddlesAt)
        {
            var lands = drop + UmadP5Forsaken.PuddlesLandAfter;
            ai.Move(drop + AfterSnapshot, At(role => Clump(WaymarkClockwisePastTheOrange(OrangeOmenOrTheStackSpot(world)), role)), jitter: TightJitter,
                knownAt: drop, knownBy: CueKind.Cast, room: TightRoom, arrivalTime: lands - Lead, outBy: lands, exitDistance: exit, cue: cue);
        }
    }

    internal static Vector3 WaymarkClockwisePastTheOrange(Vector3 orange)
    {
        var step = UmadP5Forsaken.BearingStep(orange);
        return UmadP5Forsaken.InnerPoint(step % 2 == 1 ? step + 2 : step + 1);
    }

    private static Vector3 OrangeOmenOrTheStackSpot(SimWorld world)
        => world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.IsCasting && e.CastActionId == ActionId.ForsakenOrange)?.Position
           ?? ForsakenStart;

    private static Vector3 Clump(Vector3 spot, PartyRole role, float reach = ClumpReach)
    {
        var angle = (int)role * MathF.PI / 4f;
        return spot + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * reach;
    }

    private static Func<IAiMove> At(Func<PartyRole, Vector3?> spot) => () => ByRole(spot);

    private static Func<IAiMove> At(Func<PartyRole, Vector3> spot) => () => ByRole(role => spot(role));

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
