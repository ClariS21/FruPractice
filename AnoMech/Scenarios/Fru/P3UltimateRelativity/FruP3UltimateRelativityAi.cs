using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.P3UltimateRelativity.FruP3UltimateRelativityState;

namespace AnoMech.Scenarios.Fru.P3UltimateRelativity;

public sealed class FruP3UltimateRelativityAi : IScenarioAi<FruP3UltimateRelativityState>
{
    public string Name => "NA";
    public string Group => "NA";

    private const float S = 1f / 2.358f;

    private static readonly PartyRole[] DpsPriority = [PartyRole.CasterDps, PartyRole.PhysRangedDps, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB];
    private static readonly PartyRole[] SupportPriority = [PartyRole.ShieldHealer, PartyRole.RegenHealer, PartyRole.MainTank, PartyRole.OffTank];

    private FruP3UltimateRelativityState state = null!;

    public void Run(FruP3UltimateRelativityState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        DeclareUptime(world);
        // Every move is scheduled when its spot becomes known (knownAt), with the hit it must beat declared from
        // the scenario's timeline. The human-like layer picks each bot's departure, path and settle spot.
        // (jitter is kept where there was one: it only places the human seat of a test and Last moment bots.)
        ai.Move(1f, () => ByRole(CenterSpot), knownAt: 1f, knownBy: CueKind.Cast, room: 1f);
        // Everyone knows their fire from the debuffs. The fire holders spread, the rest stack in the middle.
        ai.Move(AssignAt, () => ByRole(role => FireSpot(role, 0)), knownAt: AssignAt, knownBy: CueKind.Debuff, room: 0.5f, arrivalTime: FireAt(0));
        // The return is saved at the first laser, so the spot must be reached by then: the baiters' spot beside the
        // hourglass is tight, the eruption spots less so.
        ai.Move(FireAt(0) + Hit, () => ByRole(role => BaitSpot(role, 0)), knownAt: FireAt(0) + Hit, knownBy: CueKind.Resolve,
            room: BaitRoom, jitter: 0.3f, arrivalTime: ReturnAt(earlyReturn: true));
        LeaveLaserNowOthersArriveBy(ai, 28.6f, 0, role => FireSpot(role, 1, intermediate: true), arrivalTime: 30.6f);
        ai.Move(30.6f, () => ByRole(role => FireSpot(role, 1)), knownAt: 30.6f, knownBy: CueKind.Resolve, room: IceRoom, jitter: 0f, arrivalTime: FireAt(1));
        ai.Move(FireAt(1) + Hit, () => ByRole(role => BaitSpot(role, 1)), knownAt: FireAt(1) + Hit, knownBy: CueKind.Resolve,
            room: BaitRoom, jitter: 0.3f, arrivalTime: ReturnAt(earlyReturn: false));
        LeaveLaserNowOthersArriveBy(ai, 38.7f, 1, role => FireSpot(role, 2), arrivalTime: FireAt(2));
        ai.Move(FireAt(2) + Hit, () => ByRole(role => BaitSpot(role, 2)), knownAt: FireAt(2) + Hit, knownBy: CueKind.Resolve,
            room: BaitRoom, jitter: 0.3f, arrivalTime: LaserHitAt(2, 0));
        // The sim stops everyone and turns them outward at 51 s, so the middle must be reached by then.
        LeaveLaserNowOthersArriveBy(ai, 49.1f, 2, CenterSpot, arrivalTime: FacesOutwardAt);
        world.Events.Add(FacesOutwardAt, () => FaceOutwardForShadoweye(world));
        // The stun ends and Shell Crusher's stack goes off: close to the middle, from wherever the rewind left each.
        ai.Move(StunEndAt + 0.1f, () => ByRole(role => Direction(role) * 0.5f), knownAt: StunEndAt + 0.1f, knownBy: CueKind.Resolve,
            room: 1.5f, arrivalTime: 59.2f);
    }

    // Where the party holds still with nothing positional live (Dark Fire starts at 22.8 s, the lasers at 27.95 s and
    // sweep until 59.64 s): before the debuffs, and once the lasers and Shell Crusher are over. Bots hold the spots
    // for 4 s or more there.
    private static void DeclareUptime(SimWorld world)
    {
        var start = world.Events.Elapsed;
        world.Uptime.Allow(start + 3f, start + 10f, 2f);
        world.Uptime.Allow(start + 61f, start + 75f, 2f);
    }

    // A helper's ability resolves a frame or two after the time it is scheduled at.
    private const float Hit = 0.05f;
    // A laser follows its baiter, so a baiter 0.3 y off tilts the beam into a neighbour; and the eruption spots in the
    // same move are 6.65 y apart for a 6 y blast.
    private const float BaitRoom = 0.3f;
    // UNVERIFIED: how far a bot beside an hourglass walks to be out of the next sweep shot's beam.
    private const float BeamClear = 3f;
    private const float FacesOutwardAt = 51f;
    // Dark Blizzard hits whoever stands 3 y or more from its holder, and the middle spots span 2.5 y: each bot gets 0.2.
    private const float IceRoom = 0.2f;

    // The baited players of a wave leave right after the first shot, before the next sweep shot (their outBy);
    // the others reach their own spots by arrivalTime.
    private void LeaveLaserNowOthersArriveBy(AiManager ai, float time, int laserWave, Func<PartyRole, Vector3?> at, float arrivalTime)
    {
        ai.Move(time, () => ByRole(role => BaitedLaser(role, laserWave) ? at(role) : null), knownAt: time, knownBy: CueKind.Resolve,
            room: 0.5f, outBy: LaserHitAt(laserWave, 1), exitDistance: BeamClear);
        ai.Move(time, () => ByRole(role => BaitedLaser(role, laserWave) ? null : at(role)), knownAt: time, knownBy: CueKind.Resolve,
            room: 0.5f, arrivalTime: arrivalTime);
    }

    private bool BaitedLaser(PartyRole role, int wave) => Wave(Clock(role)) == wave;

    private void FaceOutwardForShadoweye(SimWorld world)
    {
        foreach (var role in Enum.GetValues<PartyRole>())
            if (world.Party.Get(role) is { } member && member.IsAlive() && world.Party.IsBotDriven(member))
            {
                // Drops what the habit layer still has planned for the bot (path legs, idle steps), or it would turn from outward.
                world.Movement.Supersede(role);
                member.StopMoving();
                member.Face(member.Position + Direction(role) * 100f);
            }
    }

    private int Clock(PartyRole role)
    {
        var debuff = state.Debuff(role);
        return debuff switch
        {
            RelativityDebuff.ShortDps => IsWestOfPair(role, DpsPriority) ? 5 : 3,
            RelativityDebuff.ShortSupport => 0,
            RelativityDebuff.MediumDps => 2,
            RelativityDebuff.MediumSupport => 6,
            RelativityDebuff.LongSupport => IsWestOfPair(role, SupportPriority) ? 7 : 1,
            _ => 4,
        };
    }

    private bool IsWestOfPair(PartyRole role, PartyRole[] priority)
        => priority.First(r => state.Debuff(r) == state.Debuff(role)) == role;

    private Vector3 Direction(PartyRole role) => state.Direction(Clock(role));
    private Vector3 At(PartyRole role, float radius) => Direction(role) * radius;

    private Vector3 Bait(PartyRole role)
    {
        var direction = Direction(role);
        var tangent = new Vector3(-direction.Z, 0f, direction.X);
        return direction * (23f * S) + tangent * (state.Clockwise(Clock(role)) ? -1f : 1f) * (4.1f * S);
    }

    private Vector3? FireSpot(PartyRole role, int wave, bool intermediate = false)
    {
        var fireWave = FruP3UltimateRelativityState.FireWave(state.Debuff(role));
        if (wave == 0 && state.Debuff(role) == RelativityDebuff.ShortSupport && !state.DpsIce) return At(role, 20.5f * S);
        return At(role, fireWave == wave ? (intermediate ? 20.5f : 32.5f) * S : 3f * S);
    }

    private Vector3? BaitSpot(PartyRole role, int wave)
    {
        if (BaitedLaser(role, wave)) return Bait(role);
        if (wave == 0) return At(role, state.DarkEruption(role) ? 20.5f * S : 3f * S);
        if (wave == 1 && state.Debuff(role) is RelativityDebuff.MediumDps or RelativityDebuff.MediumSupport) return At(role, 10.25f * S);
        return At(role, 3f * S);
    }

    private Vector3? CenterSpot(PartyRole role) => At(role, 3f * S);

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
