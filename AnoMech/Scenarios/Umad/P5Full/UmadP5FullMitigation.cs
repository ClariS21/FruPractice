using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Umad.UmadMitigation.P5;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P5Full;

// What P5's drills don't hold, on the full run's clock, in the segments they play between: the
// Ultima Repeaters, Fell Forces, both Maddening Orchestras and Forsaken. The tank rows are the
// Omnitank sheet's, by role rather than seat: A is the tank that invulns the first Surprise Holy
// (Warrior > Dark Knight > Gunbreaker > Paladin), B the one that starts with Kefka and invulns the
// second; the main sheet's MT column is A's and its OT column B's. A tank's invuln covers its Chaotic
// Holy and the two Fell Forces it then takes alone.
internal static class UmadP5FullMitigation
{
    public static readonly float[][] RepeaterAt = [[8.22f, 8.93f, 9.60f, 10.30f], [90.12f, 90.84f, 91.50f, 92.22f]];
    public static readonly float[] FellForcesAt = [13.08f, 16.23f, 19.37f, 50.02f, 53.13f, 94.99f, 98.16f, 142.08f, 145.19f, 148.32f];
    // Each Orchestra's first notes, second notes and finale.
    public static readonly float[][] OrchestraAt = [[38.73f, 41.92f, 45.40f], [130.80f, 133.98f, 137.46f]];
    public static readonly float[] ForsakenAt = [159.69f, 164.75f, 167.84f, 172.90f, 175.98f, 181.03f, 184.12f, 189.20f];

    // Kefka's opening line, halfway through: the sheet's first party mitigations, pressed off cooldown
    // after, which puts the second just before the second Ultima Repeater's first hit.
    private const float HalfwayThroughTheLine = 0f;
    // After the third towers, for the second Ultima Repeater.
    private const float AfterTheThirdTowers = 81f;
    // As a bot's invuln goes out: 0.7 s before the Surprise Holy it was given runs out.
    private const float InvulnBeforeTheFinale = 0.8f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    private static readonly MitHitDef[] FellForces = [FellForcesTank, FellForcesHealer, FellForcesDps];

    // The seats the plan's tank columns stand for: A invulns the first Surprise Holy.
    private static (SheetColumn A, SheetColumn B) Tanks(PartyRole firstInvuln) => (Column(firstInvuln), Column(Other(firstInvuln)));

    private static PartyRole Other(PartyRole tank) => tank == PartyRole.MainTank ? PartyRole.OffTank : PartyRole.MainTank;

    private static IEnumerable<MitScheduledHit> FellForcesRounds(params int[] rounds)
        => rounds.Select(r => new MitScheduledHit(FellForcesAt[r], [.. FellForces.Select(h => h.Key)]));

    // The first Ultima Repeater and the three Fell Forces after it.
    public static MitPlanData Opening(PartyRole firstInvuln)
    {
        var (a, b) = Tanks(firstInvuln);
        var repeater = RepeaterAt[0];
        float[] forces = [FellForcesAt[0], FellForcesAt[1], FellForcesAt[2]];
        float[] through = [repeater[0], .. forces];
        var press = repeater[0] - 3.2f;
        return new(UmadMitigation.Profile,
            [UltimaRepeater, .. FellForces],
            [.. repeater.Select(at => new MitScheduledHit(at, [UltimaRepeater.Key])), .. FellForcesRounds(0, 1, 2)],
            [
                new(b, PartyMit, HalfwayThroughTheLine, "Ultima Repeater 1 (halfway through Kefka's line)", [repeater[0], forces[0]]),
                new(D3, PartyMit, HalfwayThroughTheLine, "Ultima Repeater 1 (halfway through Kefka's line)", [repeater[0], forces[0]]),
                new(a, MitActionId.Reprisal, press, "Ultima Repeater 1 (through Fell Forces)", through),
                new(SCH, SacredSoil, press, "Ultima Repeater 1 (through Fell Forces)", through),
                new(SGE, Kerachole, press, "Ultima Repeater 1 (through Fell Forces)", through),
                new(SGE, Zoe, repeater[0] - Short - 0.3f, "Ultima Repeater 1 (Zoe shields)", []),
                new(SGE, EukrasianPrognosisII, repeater[0] - Short, "Ultima Repeater 1 (Zoe shields)", [repeater[0]]),
                new(SCH, DeploymentTactics, repeater[0] - Short, "Ultima Repeater 1 (Spreadlo)", [repeater[0]]),
                new(SGE, Holos, repeater[0] - 1.7f, "Ultima Repeater 1 (through Chaotic Flood)", [.. through, 25.70f]),
                new(WHM, PlenaryIndulgence, repeater[0] - Short, "Ultima Repeater 1", [repeater[0], forces[0]]),
                new(AST, CollectiveUnconscious, repeater[0] - Short, "Ultima Repeater 1", [repeater[0], forces[0]]),
                .. Extra(repeater[0] - Short, "Ultima Repeater 1", [repeater[0], repeater[^1]]),

                .. PerJob(a, FortyPercent, forces[0], "Fell Forces (3x)", forces),
                .. PerJob(a, ShortMit, forces[0], "Fell Forces (3x)", [forces[0], forces[1]]),
                .. PerJob(b, FortyPercent, forces[0], "Fell Forces (3x)", forces),
                .. PerJob(b, ShortMit, forces[0], "Fell Forces (3x)", [forces[0], forces[1]]),
            ],
            UmadMitigation.Kefka);
    }

    // The first Maddening Orchestra and the two Fell Forces A takes alone behind its invuln.
    public static MitPlanData FirstOrchestra(PartyRole firstInvuln)
    {
        var (a, b) = Tanks(firstInvuln);
        var (flare, chaoticFlare, finale) = (OrchestraAt[0][0], OrchestraAt[0][1], OrchestraAt[0][2]);
        float[] notes = [flare, chaoticFlare];
        float[] alone = [FellForcesAt[3], FellForcesAt[4]];
        float[] through = [flare, chaoticFlare, finale, alone[0]];
        var press = flare - 2.73f;
        return new(UmadMitigation.Profile,
            [Flare, Holy, ChaoticFlare, FlareDiffusion, ChaoticHoly, .. FellForces],
            [
                new(flare, [Flare.Key, Holy.Key]), new(chaoticFlare, [ChaoticFlare.Key, Holy.Key]), new(finale, [FlareDiffusion.Key, ChaoticHoly.Key]),
                .. FellForcesRounds(3, 4),
            ],
            [
                new(b, MitActionId.Reprisal, press, "Maddening Orchestra 1 (through Fell Forces)", through),
                new(SCH, SacredSoil, press, "Maddening Orchestra 1 (through Fell Forces)", through),
                new(SCH, FeyIllumination, press, "Maddening Orchestra 1 (through Fell Forces)", [.. through, alone[1]]),
                new(SGE, Kerachole, press, "Maddening Orchestra 1 (through Fell Forces)", through),
                new(D2, Feint, press, "Maddening Orchestra 1 (through Fell Forces)", through),
                new(WHM, DivineCaress, flare - Short, "Maddening Orchestra 1", [flare]),
                new(AST, SunSign, flare - Short, "Maddening Orchestra 1 (through Fell Forces)", through),

                Rampart(a, flare, "Maddening Orchestra 1", notes),
                .. PerJob(a, NinetySeconds, flare, "Maddening Orchestra 1", notes),
                .. PerJob(a, ShortMit, flare, "Maddening Orchestra 1", notes),
                .. PerJob(a, Invulnerability, finale, "Surprise Holy 1 (invulnerable for Chaotic Holy and the two Fell Forces alone)",
                    [finale, .. alone], at: finale - InvulnBeforeTheFinale),
                Rampart(b, flare, "Maddening Orchestra 1 (through Flare Diffusion)", [.. notes, finale]),
                .. PerJob(b, NinetySeconds, flare, "Maddening Orchestra 1 (through Flare Diffusion)", [.. notes, finale]),
                .. PerJob(b, ShortMit, flare, "Maddening Orchestra 1", notes),
            ],
            UmadMitigation.Kefka);
    }

    // The second Ultima Repeater and the two Fell Forces after it.
    public static MitPlanData SecondRepeater(PartyRole firstInvuln)
    {
        var (a, b) = Tanks(firstInvuln);
        var repeater = RepeaterAt[1];
        float[] forces = [FellForcesAt[5], FellForcesAt[6]];
        float[] through = [repeater[0], repeater[^1], .. forces];
        float[] offCooldown = [repeater[1], repeater[^1], .. forces];
        var press = repeater[0] - 3.12f;
        return new(UmadMitigation.Profile,
            [UltimaRepeater, .. FellForces],
            [.. repeater.Select(at => new MitScheduledHit(at, [UltimaRepeater.Key])), .. FellForcesRounds(5, 6)],
            [
                new(D1, Feint, AfterTheThirdTowers, "Ultima Repeater 2 (after the third towers)", [repeater[0], repeater[^1], forces[0]]),
                new(D4, Addle, AfterTheThirdTowers, "Ultima Repeater 2 (after the third towers)", [repeater[0], repeater[^1], forces[0]]),
                new(a, MitActionId.Reprisal, press, "Ultima Repeater 2 (through Fell Forces)", through),
                new(SCH, SacredSoil, press, "Ultima Repeater 2 (through Fell Forces)", through),
                new(SGE, Kerachole, press, "Ultima Repeater 2 (through Fell Forces)", through),
                new(WHM, PlenaryIndulgence, repeater[0] - Short, "Ultima Repeater 2", [repeater[0], repeater[^1], forces[0]]),
                new(AST, CollectiveUnconscious, repeater[0] - Short, "Ultima Repeater 2", [repeater[0], repeater[^1], forces[0]]),
                // The sheet's "All mechanics requires shields!": the shield healers' own, its Spreadlo still on recast.
                new(SCH, Concitation, repeater[0] - Short, "Ultima Repeater 2 (shields)", [repeater[0]]),
                new(SGE, EukrasianPrognosisII, repeater[0] - Short, "Ultima Repeater 2 (shields)", [repeater[0]]),
                new(b, PartyMit, HalfwayThroughTheLine + 90f, "Ultima Repeater 2 (as soon as it is back)", offCooldown),
                new(D3, PartyMit, HalfwayThroughTheLine + 90f, "Ultima Repeater 2 (as soon as it is back)", offCooldown),

                .. PerJob(a, ShortMit, forces[0], "Fell Forces (2x)", forces),
                .. PerJob(b, ShortMit, forces[0], "Fell Forces (2x)", forces),
            ],
            UmadMitigation.Kefka);
    }

    // The second Maddening Orchestra and the three Fell Forces after it, the first two on B alone.
    public static MitPlanData SecondOrchestra(PartyRole firstInvuln)
    {
        var (a, b) = Tanks(firstInvuln);
        var (flare, chaoticFlare, finale) = (OrchestraAt[1][0], OrchestraAt[1][1], OrchestraAt[1][2]);
        float[] notes = [flare, chaoticFlare];
        float[] alone = [FellForcesAt[7], FellForcesAt[8]];
        var third = FellForcesAt[9];
        // The sheet's footnote: just before the cast ends, so it carries to the third Fell Forces.
        var rampart = flare - 1.3f;
        // As soon as the first Fell Forces' 40 % is back.
        var forty = flare - 2.6f;
        return new(UmadMitigation.Profile,
            [Flare, Holy, ChaoticFlare, FlareDiffusion, ChaoticHoly, .. FellForces],
            [
                new(flare, [Flare.Key, Holy.Key]), new(chaoticFlare, [ChaoticFlare.Key, Holy.Key]), new(finale, [FlareDiffusion.Key, ChaoticHoly.Key]),
                .. FellForcesRounds(7, 8, 9),
            ],
            [
                new(SCH, SacredSoil, flare - 3.4f, "Maddening Orchestra 2 (early enough to be back for Forsaken)", [.. notes, finale, alone[0]]),
                new(SGE, Kerachole, flare - 3.4f, "Maddening Orchestra 2 (early enough to be back for Forsaken)", [.. notes, finale, alone[0]]),

                Rampart(a, flare, "Maddening Orchestra 2 (through the third Fell Forces)", [.. notes, finale, third], at: rampart),
                .. PerJob(a, FortyPercent, flare, "Maddening Orchestra 2 (through Flare Diffusion)", [.. notes, finale], at: forty),
                .. PerJob(a, ShortMit, flare, "Maddening Orchestra 2", notes),
                Rampart(b, flare, "Maddening Orchestra 2 (through the third Fell Forces)", [.. notes, third], at: rampart),
                .. PerJob(b, FortyPercent, flare, "Maddening Orchestra 2", notes, at: forty),
                .. PerJob(b, ShortMit, flare, "Maddening Orchestra 2", notes),
                .. PerJob(b, Invulnerability, finale, "Surprise Holy 2 (invulnerable for Chaotic Holy and the two Fell Forces alone)",
                    [finale, .. alone], at: finale - InvulnBeforeTheFinale),

                .. PerJob(a, NinetySeconds, third, "Fell Forces III", [third], jobs: [Paladin, DarkKnight]),
                .. PerJob(a, ShortMit, third, "Fell Forces III", [third], jobs: [Paladin, DarkKnight]),
                .. PerJob(b, NinetySeconds, third, "Fell Forces III", [third], jobs: [Paladin, DarkKnight]),
                .. PerJob(b, ShortMit, third, "Fell Forces III", [third], jobs: [Paladin, DarkKnight]),
                // The sheet's "Assist Tanks": a Warrior or Gunbreaker has only its Rampart left for it.
                new(WHM, Aquaveil, third - Short, "Fell Forces III (assist the tanks)", [third], Target: firstInvuln),
                new(WHM, DivineBenison, third - Short, "Fell Forces III (assist the tanks)", [third], Target: Other(firstInvuln)),
            ],
            UmadMitigation.Kefka);
    }

    public static MitPlanData Forsaken(PartyRole firstInvuln)
    {
        var (a, b) = Tanks(firstInvuln);
        return new(UmadMitigation.Profile,
            [UmadMitigation.P5.Forsaken, ForsakenAgain, ForsakenBonds],
            [.. ForsakenAt.Select((at, i) => new MitScheduledHit(at, [(i == 0 ? UmadMitigation.P5.Forsaken : i % 2 == 1 ? ForsakenBonds : ForsakenAgain).Key]))],
            [.. UmadP5ForsakenMitigation.Rows(ForsakenAt, a, b)],
            UmadMitigation.Kefka);
    }
}
