using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Scenarios.Fru;

// Mitigation practice's model of Futures Rewritten: max HP at the fight's item level and the share
// of a raw hit each class takes, measured as the ratio of tank and healer to DPS damage in public
// clears. Bot tanks press real cooldowns from the plan, so TankCooldowns is 0.
public static class FruMitigation
{
    public static readonly MitProfile Profile = new(
        TankMaxHp: 233000f, PhysicalMaxHp: 162000f, MagicalMaxHp: 146500f,
        TankTaken: 0.62f, PhysicalTaken: 1f, MagicalTaken: 0.95f,
        TankCooldowns: 0f, TopUpGap: 4f);

    // The bosses the party's Reprisal, Feint and Addle go on.
    public static readonly MitSource Oracle = new("Oracle of Darkness");
    public static readonly MitSource Usurper = new("Usurper of Frost");
    public static readonly MitSource Pandora = new("Pandora");
    // Not a boss: Edge of Oblivion comes from the Fragment of Fate (93 of 93 hits in public clears), so no boss debuff reduces it.
    // It is never registered as an actor, so its hits take only the victims' own mitigation.
    public static readonly MitSource FragmentOfFate = new("Fragment of Fate");

    private static MitHitDef Hit(string key, string name, float raw, MitSource source, uint[] ids, DamageKind kind = DamageKind.Magic,
        bool tankBuster = false, float? tankRaw = null, bool targetOnly = false, float? soloRaw = null)
        => new(key, name, raw, source, kind, tankBuster, ActionIds: ids, TankRaw: tankRaw, TargetOnly: targetOnly, SoloRaw: soloRaw);

    // Raw is the median unmitigated damage on a DPS in public clears (a tank buster's, on a tank).
    // UNVERIFIED where noted: no clear reports the hit, so it is the HP share the sim already uses
    // for it (or the default for its kind) as HP over 0.75, what the median mitigation leaves.
    public static class P3
    {
        public static readonly MitHitDef HellsJudgment = new("hells_judgment", "Hell's Judgment", 0f, Oracle,
            ActionIds: [ActionId.HellsJudgment], ToOneHp: true);
        public static readonly MitHitDef UltimateRelativity = Hit("ultimate_relativity", "Ultimate Relativity", 186000f, Oracle, [ActionId.UltimateRelativity]);
        public static readonly MitHitDef DarkFire = Hit("dark_fire", "Dark Fire III", 145000f, Oracle, [ActionId.DarkFire]);
        public static readonly MitHitDef UnholyDarkness = Hit("unholy_darkness", "Unholy Darkness", 104000f, Oracle, [ActionId.UnholyDarkness]);
        public static readonly MitHitDef SinboundMeltdown = Hit("sinbound_meltdown", "Sinbound Meltdown", 120000f, Oracle, [ActionId.SinboundMeltdownFirst]);
        public static readonly MitHitDef DarkEruption = Hit("dark_eruption", "Dark Eruption", 73000f, Oracle, [ActionId.DarkEruption]);
        public static readonly MitHitDef DarkWater = Hit("dark_water", "Dark Water III", 75000f, Oracle, [ActionId.DarkWater]);
        public static readonly MitHitDef ShellCrusher = Hit("shell_crusher", "Shell Crusher", 118000f, Oracle, [ActionId.ShellCrusher]);
        public static readonly MitHitDef ShockwavePulsar = Hit("shockwave_pulsar", "Shockwave Pulsar", 156000f, Oracle, [ActionId.ShockwavePulsar]);
        public static readonly MitHitDef BlackHalo = Hit("black_halo", "Black Halo", 676000f, Oracle, [ActionId.BlackHalo], DamageKind.Physical, tankBuster: true);
        public static readonly MitHitDef SpiritTaker = Hit("spirit_taker", "Spirit Taker", 68000f, Oracle, [ActionId.SpiritTaker], DamageKind.Physical);
        public static readonly MitHitDef DarkestDance = Hit("darkest_dance", "Darkest Dance", 696000f, Oracle, [ActionId.DarkestDance], DamageKind.Physical, tankBuster: true);
        public static readonly MitHitDef DarkestDanceKnockback = Hit("darkest_dance_knockback", "Darkest Dance", 16000f, Oracle, [ActionId.DarkestDanceKnockback]);
    }

    public static class P4
    {
        public static readonly MitHitDef EdgeOfOblivion = Hit("edge_of_oblivion", "Edge of Oblivion", 60000f, FragmentOfFate, [ActionId.EdgeOfOblivion]);
        public static readonly MitHitDef DarklitDragonsong = Hit("darklit_dragonsong", "Darklit Dragonsong", 188000f, Usurper, [ActionId.DarklitDragonsong]);
        public static readonly MitHitDef BrightHunger = Hit("bright_hunger", "Bright Hunger", 45000f, Usurper, [ActionId.BrightHunger]);
        public static readonly MitHitDef PathOfLight = Hit("path_of_light", "The Path of Light", 107000f, Usurper, [ActionId.PathOfLight]);
        public static readonly MitHitDef SpiritTaker = P3.SpiritTaker;
        public static readonly MitHitDef DarkWater = P3.DarkWater;
        public static readonly MitHitDef SomberDanceFar = Hit("somber_dance_far", "Somber Dance", 275000f, Oracle, [ActionId.SomberDanceFar], DamageKind.Physical, tankBuster: true);
        // UNVERIFIED: the reported figure (8.8M) can't be a real hit, so this is 0.48 of a tank's HP over 0.75.
        public static readonly MitHitDef SomberDanceNear = Hit("somber_dance_near", "Somber Dance", 149000f, Oracle, [ActionId.SomberDanceNear], DamageKind.Physical, tankBuster: true);
        // UNVERIFIED: a lone tank's share of the Usurper's Akh Morn; clears stack it with three others.
        public static readonly MitHitDef AkhMornUsurper = Hit("akh_morn_usurper", "Akh Morn", 45000f, Usurper, [ActionId.AkhMornUsurperHit], tankRaw: 42000f, soloRaw: 90000f);
        public static readonly MitHitDef AkhMornOracle = Hit("akh_morn_oracle", "Akh Morn", 45000f, Oracle, [ActionId.AkhMornOracleHit], tankRaw: 42000f);
        // The Usurper of Frost in 46 of 46 public hits, though the sim has the Oracle cast it.
        public static readonly MitHitDef MornAfah = Hit("morn_afah", "Morn Afah", 176000f, Usurper, [ActionId.MornAfah]);
        // UNVERIFIED: the Oracle's party-wide hit; no clear reports it.
        public static readonly MitHitDef CrystallizeTime = Hit("crystallize_time", "Crystallize Time", 108000f, Oracle, [ActionId.CrystallizeTimeOracle]);
        public static readonly MitHitDef DarkAero = Hit("dark_aero", "Dark Aero III", 26000f, Oracle, [ActionId.DarkAero]);
        public static readonly MitHitDef Quietus = Hit("quietus", "Quietus", 20000f, Oracle, [ActionId.Quietus]);
        public static readonly MitHitDef HallowedWings = Hit("hallowed_wings", "Hallowed Wings", 62000f, Usurper, [ActionId.HallowedWingsKnockback], tankRaw: 130000f);
    }

    public static class P5
    {
        public static readonly MitHitDef FulgentBlade = Hit("fulgent_blade", "Fulgent Blade", 179000f, Pandora, [ActionId.FulgentBlade], DamageKind.Physical);
        public static readonly MitHitDef AkhMornLeft = Hit("akh_morn_left", "Akh Morn", 188000f, Pandora, [ActionId.AkhMornPandoraLeft], tankRaw: 124000f);
        public static readonly MitHitDef AkhMornRight = Hit("akh_morn_right", "Akh Morn", 192000f, Pandora, [ActionId.AkhMornPandoraRight], tankRaw: 121000f);
        // UNVERIFIED: no clear reports the wings' tank hits, so they take the default tank-buster share.
        public static readonly MitHitDef WingsCleave = Hit("wings_cleave", "Wings Dark and Light", 139800f, Pandora,
            [ActionId.WingsCleaveLight, ActionId.WingsCleaveDark], DamageKind.Physical, tankBuster: true);
        public static readonly MitHitDef WingsBuster = Hit("wings_buster", "Wings Dark and Light", 139800f, Pandora,
            [ActionId.WingsBusterLight, ActionId.WingsBusterDark], tankBuster: true);
        public static readonly MitHitDef TowerExplosion = Hit("tower_explosion", "Explosion", 45000f, Pandora, [ActionId.ParadiseTowerExplosion]);
        public static readonly MitHitDef CruelPathOfLight = Hit("cruel_path_of_light", "Cruel Path of Light", 113000f, Pandora, [ActionId.CruelPathOfLight]);
        public static readonly MitHitDef CruelPathOfDarkness = Hit("cruel_path_of_darkness", "Cruel Path of Darkness", 112000f, Pandora, [ActionId.CruelPathOfDarkness]);
        public static readonly MitHitDef PandorasBox = new("pandoras_box", "Pandora's Box", 777000f, Pandora);
    }
}
