using AnoMech.Core.Game.PartyMit;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad;

// Mitigation practice's model of Dancing Mad: max HP at the fight's item level and the share of a
// raw hit each class takes, measured as the ratio of tank and healer to DPS damage in 27 public
// clears (0.61 and 0.94 on every big raidwide). Bot tanks press real cooldowns from the plan, so
// TankCooldowns is 0.
public static class UmadMitigation
{
    public static readonly MitProfile Profile = new(
        TankMaxHp: 325200f, PhysicalMaxHp: 226600f, MagicalMaxHp: 205200f,
        TankTaken: 0.61f, PhysicalTaken: 1f, MagicalTaken: 0.94f,
        TankCooldowns: 0f, TopUpGap: 4f);

    // The bosses the party's Reprisal, Feint and Addle go on. A hit counts them only when the sheet
    // aims them at it: its P3 notes call them filler on everything but Bowels of Agony and Knock Down
    // (Chaos) and the busters (Exdeath), and its P4 notes say only Ultima Upsurge is Kefka's own.
    public static readonly MitSource Kefka = new("Kefka");
    public static readonly MitSource Chaos = new("Chaos");
    public static readonly MitSource Exdeath = new("Exdeath");

    // Fresh: the sheet has the healers top the party up before it, which the sim's bots don't heal fast enough to.
    private static MitHitDef Hit(string key, string name, float raw, MitSource? source, uint[] ids, bool tankBuster = false, float? soloRaw = null,
        bool fresh = false)
        => new(key, name, raw, source, TankBuster: tankBuster, Fresh: fresh, ActionIds: ids, SoloRaw: soloRaw);

    // Raw is the median unmitigated damage on a DPS in 27 public clears (a tank buster's, on a tank);
    // every hit here is magic. A stack's is what each member takes when it is shared as planned.
    public static class P3
    {
        public static readonly MitHitDef BowelsOfAgony = Hit("bowels_of_agony", "Bowels of Agony", 166200f, Chaos, [ActionId.BowelsOfAgony]);
        // UNVERIFIED for Stray Spray and Inferno: the clears report one of each pair, so the other takes its value.
        public static readonly MitHitDef Stray = Hit("stray", "Stray Flames", 266000f, null, [ActionId.StrayFlames, ActionId.StraySpray]);
        public static readonly MitHitDef Crystal = Hit("crystal", "Tsunami", 264300f, null, [ActionId.Inferno_Crystal, ActionId.Tsunami_Crystal]);
        public static readonly MitHitDef ThunderIII = Hit("thunder_iii", "Thunder III", 555000f, Exdeath, [ActionId.ThunderIII_Resolve], tankBuster: true);
        public static readonly MitHitDef UltimaBlaster = Hit("ultima_blaster", "Ultima Blaster", 42200f, null, [ActionId.UltimaBlaster]);
        // Retail's eight Cyclones are pair stacks, each member caught by two at 223k; the sim's
        // party takes all eight in one stack, so each counts a quarter of that.
        public static readonly MitHitDef Cyclone = Hit("cyclone", "Cyclone", 55900f, null, [ActionId.Cyclone]);
        public static readonly MitHitDef UltimaBlasterCharge = Hit("ultima_blaster_charge", "Ultima Blaster", 124000f, null, [ActionId.UltimaBlasterCharge]);
        public static readonly MitHitDef Earthquake = Hit("earthquake", "Earthquake", 82800f, null, [ActionId.Earthquake_Cleanse]);
        public static readonly MitHitDef ShockingImpact = Hit("shocking_impact", "Shocking Impact", 218800f, null, [ActionId.ShockingImpact]);
        public static readonly MitHitDef Shockwave = Hit("shockwave", "Shockwave", 215600f, null, [ActionId.ShockwaveCone]);
        // The beam's 22k as the clears report it; the crust absorbs the rest of the beam that spends one,
        // so that one counts the same.
        public static readonly MitHitDef Nothingness = Hit("nothingness", "Nothingness", 22000f, null, [ActionId.Nothingness]);
        // Each player takes one of each within a few seconds; the sheet's healers top the party up
        // between the stomp sets (its footnote 10). Chaos deals Knock Down, which the sheet's
        // Reprisals and Feint on him are for.
        public static readonly MitHitDef KnockDown = Hit("knock_down", "Knock Down", 202100f, Chaos, [ActionId.KnockDown], fresh: true);
        public static readonly MitHitDef StompAMole = Hit("stomp_a_mole", "Stomp-a-Mole", 218600f, null, [ActionId.StompAMole], fresh: true);
    }

    public static class P4
    {
        public static readonly MitHitDef GrandCross = Hit("grand_cross", "Grand Cross", 289300f, null, [ActionId.GrandCross]);
        // Five seconds after a Grand Cross: the healers top the party up in between.
        public static readonly MitHitDef Inferno = Hit("inferno", "Inferno", 226700f, null, [ActionId.Inferno_Visual], fresh: true);
        public static readonly MitHitDef Tsunami = Hit("tsunami", "Tsunami", 230900f, null, [ActionId.Tsunami_Visual], fresh: true);
        // The stacked element is shared by three; the other goes on one alone.
        public static readonly MitHitDef DeathBoltStack = Hit("death_bolt_stack", "Death Bolt", 164800f, null, [ActionId.DeathBolt_Lie]);
        public static readonly MitHitDef DeathBoltSpread = Hit("death_bolt_spread", "Death Bolt", 225800f, null, [ActionId.DeathBolt]);
        public static readonly MitHitDef DeathWaveStack = Hit("death_wave_stack", "Death Wave", 168900f, null, [ActionId.DeathWave]);
        public static readonly MitHitDef DeathWaveSpread = Hit("death_wave_spread", "Death Wave", 214700f, null, [ActionId.DeathWave_Lie]);
        public static readonly MitHitDef UltimaUpsurge = Hit("ultima_upsurge", "Ultima Upsurge", 218200f, Kefka, [ActionId.UltimaUpsurge]);
    }

    public static class P5
    {
        public static readonly MitHitDef UltimaRepeater = Hit("ultima_repeater", "Ultima Repeater", 125600f, Kefka, [ActionId.UltimaRepeaterHit]);
        // The tank stack, on each of the two tanks sharing it, or on the invulnerable one alone.
        public static readonly MitHitDef FellForcesTank = Hit("fell_forces_tank", "Fell Forces", 415300f, Kefka, [ActionId.FellForcesTank],
            tankBuster: true, soloRaw: 820400f);
        // Only healers take it: their 79.6k over the healers' share.
        public static readonly MitHitDef FellForcesHealer = Hit("fell_forces_healer", "Fell Forces", 84600f, Kefka, [ActionId.FellForcesHealer]);
        public static readonly MitHitDef FellForcesDps = Hit("fell_forces_dps", "Fell Forces", 85600f, Kefka, [ActionId.FellForcesDps]);
        public static readonly MitHitDef ChaoticFlood = Hit("chaotic_flood", "Chaotic Flood", 72900f, null, [ActionId.ChaoticFlood]);
        public static readonly MitHitDef Flare = Hit("flare", "Flare", 344300f, Kefka, [ActionId.Flare], tankBuster: true);
        // On three players a few seconds after Stray Entropy or Chaotic Flood hit them.
        public static readonly MitHitDef Holy = Hit("holy", "Holy", 162700f, Kefka, [ActionId.Holy], fresh: true);
        public static readonly MitHitDef ChaoticFlare = Hit("chaotic_flare", "Chaotic Flare", 345100f, Kefka, [ActionId.ChaoticFlare], tankBuster: true);
        public static readonly MitHitDef FlareDiffusion = Hit("flare_diffusion", "Flare Diffusion", 350400f, Kefka, [ActionId.FlareDiffusion], tankBuster: true);
        public static readonly MitHitDef ChaoticHoly = Hit("chaotic_holy", "Chaotic Holy", 1051400f, Kefka, [ActionId.ChaoticHoly], tankBuster: true);
        // Every player soaks one of each set's four towers, 6 s apart: the healers top the soakers up between sets.
        public static readonly MitHitDef CelestriadTower = Hit("celestriad_tower", "Fire III", 165200f, null,
            [ActionId.CelestriadFireIII, ActionId.CelestriadBlizzardIII, ActionId.CelestriadThunderIII], fresh: true);
        public static readonly MitHitDef StrayEntropy = Hit("stray_entropy", "Stray Entropy", 166000f, null, [ActionId.ExaflareSpread]);
        // Eight raidwides in 30 s: the healers top the party up between them.
        public static readonly MitHitDef Forsaken = Hit("forsaken", "Forsaken", 414000f, Kefka, [ActionId.ForsakenP5]);
        public static readonly MitHitDef ForsakenAgain = Hit("forsaken_again", "Forsaken", 307300f, Kefka, [ActionId.ForsakenP5Raidwide], fresh: true);
        public static readonly MitHitDef ForsakenBonds = Hit("forsaken_bonds", "Forsaken Bonds", 284700f, Kefka, [ActionId.ForsakenBonds], fresh: true);
    }
}
