using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Party;

// What a script reads and writes about its bot's rotation: the combo step, resources, DoT timers
// and the 2-minute burst window. Times are BotCombat's clock.
internal sealed class Rot
{
    private const float BurstSeconds = 20f;

    private readonly Dictionary<uint, float> readyAt = new();
    private readonly Dictionary<uint, float> expiresAt = new();
    private readonly Dictionary<uint, float> lastUsed = new();
    private readonly HashSet<uint> usedInBurst = new();

    public float Now { get; set; }
    public Rng Rng { get; set; } = Rng.Detached;
    public int Combo { get; set; }
    public int Gauge { get; set; }
    public int Stack { get; set; }
    public int Phase { get; set; }
    public float Clock { get; set; }
    public bool Proc { get; set; }
    public float BurstAt { get; private set; } = float.NegativeInfinity;

    public bool Burst => Now - BurstAt < BurstSeconds;
    public bool Ready(uint id) => !readyAt.TryGetValue(id, out var at) || Now >= at;
    public float Left(uint id) => expiresAt.TryGetValue(id, out var at) ? MathF.Max(0f, at - Now) : 0f;
    public void Dot(uint id, float seconds) => expiresAt[id] = Now + seconds;
    public bool Every(uint id, float seconds) => !lastUsed.TryGetValue(id, out var at) || Now - at >= seconds;

    // True once per burst window, for actions that come back faster than the burst does.
    public bool Fresh(uint id) => Burst && !usedInBurst.Contains(id);

    public void Record(uint id, float recastSeconds, bool startsBurst)
    {
        if (startsBurst && !Burst)
        {
            BurstAt = Now;
            usedInBurst.Clear();
        }
        usedInBurst.Add(id);
        lastUsed[id] = Now;
        readyAt[id] = Now + recastSeconds;
    }
}

// A job's rotation as priority lists: the first usable action wins. A GCD with a cast time is only
// usable while the bot can stand and cast. Burst oGCDs are tried first and open the burst window.
internal sealed record JobScript(
    bool Melee,
    Func<Rot, IEnumerable<uint>> Gcd,
    Func<Rot, IEnumerable<uint>> Ogcd,
    Action<Rot, uint> Used,
    uint[] Burst,
    uint[] Ids);

internal static class BotScripts
{
    // How long a refreshed DoT lasts; the statuses BotBuffs shows use the same numbers.
    public const float DotSeconds = 30f;
    public const float BardDotSeconds = 45f;

    public static JobScript? For(byte classJob) => Scripts.GetValueOrDefault((JobId)classJob);

    public static IEnumerable<KeyValuePair<JobId, JobScript>> All => Scripts;

    private static uint[] Ids(params Type[] holders)
        => holders.SelectMany(t => t.GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(uint)).Select(f => (uint)f.GetRawConstantValue()!)).ToArray();

    private static readonly Dictionary<JobId, JobScript> Scripts = new()
    {
        [JobId.Warrior] = new(true, WarGcd, WarOgcd, WarUsed, [War.InnerRelease], Ids(typeof(War))),
        [JobId.Paladin] = new(true, PldGcd, PldOgcd, PldUsed, [Pld.FightOrFlight, Pld.Imperator], Ids(typeof(Pld))),
        [JobId.Gunbreaker] = new(true, GnbGcd, GnbOgcd, GnbUsed, [Gnb.NoMercy, Gnb.Bloodfest], Ids(typeof(Gnb))),
        [JobId.DarkKnight] = new(true, DrkGcd, DrkOgcd, DrkUsed, [Drk.Delirium, Drk.LivingShadow], Ids(typeof(Drk))),
        [JobId.WhiteMage] = new(false, WhmGcd, WhmOgcd, WhmUsed, [Whm.PresenceOfMind], Ids(typeof(Whm))),
        [JobId.Scholar] = new(false, SchGcd, SchOgcd, SchUsed, [Sch.ChainStratagem], Ids(typeof(Sch))),
        [JobId.Astrologian] = new(false, AstGcd, AstOgcd, AstUsed, [Ast.Divination], Ids(typeof(Ast))),
        [JobId.Sage] = new(false, SgeGcd, SgeOgcd, SgeUsed, [Sge.Psyche], Ids(typeof(Sge))),
        [JobId.Dragoon] = new(true, DrgGcd, DrgOgcd, DrgUsed, [Drg.LanceCharge, Drg.BattleLitany], Ids(typeof(Drg))),
        [JobId.Monk] = new(true, MnkGcd, MnkOgcd, MnkUsed, [Mnk.RiddleOfFire, Mnk.Brotherhood], Ids(typeof(Mnk))),
        [JobId.Bard] = new(false, BrdGcd, BrdOgcd, BrdUsed, [Brd.RagingStrikes, Brd.BattleVoice, Brd.RadiantFinale, Brd.Barrage], Ids(typeof(Brd))),
        [JobId.BlackMage] = new(false, BlmGcd, BlmOgcd, BlmUsed, [Blm.LeyLines], Ids(typeof(Blm))),
    };

    // ── Warrior ──────────────────────────────────────────────────────────────

    private static class War
    {
        public const uint HeavySwing = 31, Maim = 37, StormsPath = 42, StormsEye = 45, FellCleave = 3549, PrimalRend = 25753,
            InnerRelease = 7389, Infuriate = 52, Upheaval = 7387;
    }

    private static IEnumerable<uint> WarGcd(Rot s)
    {
        if (s.Fresh(War.PrimalRend)) yield return War.PrimalRend;
        if (s.Gauge >= 50 && (s.Burst || s.Gauge >= 90)) yield return War.FellCleave;
        yield return s.Combo switch
        {
            0 => War.HeavySwing,
            1 => War.Maim,
            _ => s.Left(War.StormsEye) < 10f ? War.StormsEye : War.StormsPath,
        };
    }

    private static IEnumerable<uint> WarOgcd(Rot s)
    {
        if (s.Gauge < 50) yield return War.Infuriate;
        yield return War.Upheaval;
    }

    private static void WarUsed(Rot s, uint id)
    {
        switch (id)
        {
            case War.HeavySwing: s.Combo = 1; s.Gauge += 10; break;
            case War.Maim: s.Combo = 2; s.Gauge += 10; break;
            case War.StormsPath: s.Combo = 0; s.Gauge += 20; break;
            case War.StormsEye: s.Combo = 0; s.Gauge += 20; s.Dot(id, DotSeconds); break;
            case War.FellCleave: s.Gauge -= 50; break;
            case War.Infuriate: s.Gauge += 50; break;
        }
        s.Gauge = Math.Min(s.Gauge, 100);
    }

    // ── Paladin ──────────────────────────────────────────────────────────────

    private static class Pld
    {
        public const uint FastBlade = 9, RiotBlade = 15, RoyalAuthority = 3539, Atonement = 16460, HolySpirit = 7384, Confiteor = 16459,
            Imperator = 36921, Expiacion = 25747, FightOrFlight = 20, CircleOfScorn = 23;
    }

    // Stack counts the Requiescat window's casts: Confiteor, then Holy Spirit.
    private static IEnumerable<uint> PldGcd(Rot s)
    {
        if (s.Stack == 4) yield return Pld.Confiteor;
        if (s.Stack > 0 && s.Stack < 4) yield return Pld.HolySpirit;
        if (s.Gauge > 0) yield return Pld.Atonement;
        yield return s.Combo switch { 0 => Pld.FastBlade, 1 => Pld.RiotBlade, _ => Pld.RoyalAuthority };
    }

    private static IEnumerable<uint> PldOgcd(Rot s)
    {
        yield return Pld.CircleOfScorn;
        yield return Pld.Expiacion;
    }

    private static void PldUsed(Rot s, uint id)
    {
        switch (id)
        {
            case Pld.FastBlade: s.Combo = 1; break;
            case Pld.RiotBlade: s.Combo = 2; break;
            case Pld.RoyalAuthority: s.Combo = 0; s.Gauge = 3; break;
            case Pld.Atonement: s.Gauge--; break;
            case Pld.Imperator: s.Stack = 4; break;
            case Pld.Confiteor: s.Stack = 3; break;
            case Pld.HolySpirit: s.Stack--; break;
        }
    }

    // ── Gunbreaker ───────────────────────────────────────────────────────────

    private static class Gnb
    {
        public const uint KeenEdge = 16137, BrutalShell = 16139, SolidBarrel = 16145, BurstStrike = 16162, GnashingFang = 16146,
            SonicBreak = 16153, DoubleDown = 25760, NoMercy = 16138, Bloodfest = 16164, BlastingZone = 16165, BowShock = 16159;
    }

    private static IEnumerable<uint> GnbGcd(Rot s)
    {
        if (s.Fresh(Gnb.SonicBreak)) yield return Gnb.SonicBreak;
        if (s.Burst && s.Gauge >= 2) yield return Gnb.DoubleDown;
        if (s.Gauge >= 1 && s.Ready(Gnb.GnashingFang)) yield return Gnb.GnashingFang;
        if (s.Gauge >= 3 || (s.Burst && s.Gauge >= 1)) yield return Gnb.BurstStrike;
        yield return s.Combo switch { 0 => Gnb.KeenEdge, 1 => Gnb.BrutalShell, _ => Gnb.SolidBarrel };
    }

    private static IEnumerable<uint> GnbOgcd(Rot s)
    {
        yield return Gnb.BlastingZone;
        yield return Gnb.BowShock;
    }

    private static void GnbUsed(Rot s, uint id)
    {
        switch (id)
        {
            case Gnb.KeenEdge: s.Combo = 1; break;
            case Gnb.BrutalShell: s.Combo = 2; break;
            case Gnb.SolidBarrel: s.Combo = 0; s.Gauge = Math.Min(3, s.Gauge + 1); break;
            case Gnb.BurstStrike: case Gnb.GnashingFang: s.Gauge--; break;
            case Gnb.DoubleDown: s.Gauge = Math.Max(0, s.Gauge - 2); break;
            case Gnb.Bloodfest: s.Gauge = 3; break;
        }
    }

    // ── Dark Knight ──────────────────────────────────────────────────────────

    private static class Drk
    {
        public const uint HardSlash = 3617, SyphonStrike = 3623, Souleater = 3632, Bloodspiller = 7392, Disesteem = 36932,
            Delirium = 7390, LivingShadow = 16472, EdgeOfShadow = 16470, Shadowbringer = 25757, CarveAndSpit = 3643;
    }

    private static IEnumerable<uint> DrkGcd(Rot s)
    {
        if (s.Fresh(Drk.Disesteem)) yield return Drk.Disesteem;
        if (s.Gauge >= 50 && (s.Burst || s.Gauge >= 90)) yield return Drk.Bloodspiller;
        yield return s.Combo switch { 0 => Drk.HardSlash, 1 => Drk.SyphonStrike, _ => Drk.Souleater };
    }

    // Stack is MP in thirds of a Edge of Shadow: one more per GCD.
    private static IEnumerable<uint> DrkOgcd(Rot s)
    {
        if (s.Stack >= 3) yield return Drk.EdgeOfShadow;
        yield return Drk.Shadowbringer;
        yield return Drk.CarveAndSpit;
    }

    private static void DrkUsed(Rot s, uint id)
    {
        if (id is Drk.HardSlash or Drk.SyphonStrike or Drk.Souleater or Drk.Bloodspiller) s.Stack = Math.Min(9, s.Stack + 1);
        switch (id)
        {
            case Drk.HardSlash: s.Combo = 1; break;
            case Drk.SyphonStrike: s.Combo = 2; break;
            case Drk.Souleater: s.Combo = 0; s.Gauge += 20; break;
            case Drk.Bloodspiller: s.Gauge -= 50; break;
            case Drk.EdgeOfShadow: s.Stack -= 3; break;
        }
        s.Gauge = Math.Clamp(s.Gauge, 0, 100);
    }

    // ── White Mage ───────────────────────────────────────────────────────────

    private static class Whm
    {
        public const uint Dia = 16532, GlareIII = 25859, GlareIV = 37009, AfflatusMisery = 16535, PresenceOfMind = 136, Assize = 3571;
    }

    // Gauge is Lilies, one per 20 s up to three; Stack is the Glare IV charges Misery grants.
    private static IEnumerable<uint> WhmGcd(Rot s)
    {
        TopUpLilies(s);
        if (s.Left(Whm.Dia) < 3f) yield return Whm.Dia;
        if (s.Gauge >= 3) yield return Whm.AfflatusMisery;
        if (s.Stack > 0) yield return Whm.GlareIV;
        yield return Whm.GlareIII;
    }

    // One more Lily every 20 s, up to three.
    internal static void TopUpLilies(Rot s)
    {
        if (s.Gauge >= 3) s.Clock = s.Now;
        else if (s.Now - s.Clock >= 20f)
        {
            s.Gauge++;
            s.Clock += 20f;
        }
    }

    private static IEnumerable<uint> WhmOgcd(Rot s)
    {
        yield return Whm.Assize;
    }

    private static void WhmUsed(Rot s, uint id)
    {
        switch (id)
        {
            case Whm.Dia: s.Dot(id, DotSeconds); break;
            case Whm.AfflatusMisery: s.Gauge = 0; s.Stack = 3; break;
            case 16534: s.Gauge = Math.Max(0, s.Gauge - 1); break;
            case Whm.GlareIV: s.Stack--; break;
        }
    }

    // ── Scholar ──────────────────────────────────────────────────────────────

    private static class Sch
    {
        public const uint Biolysis = 16540, BroilIV = 25865, RuinII = 17870, Aetherflow = 166, EnergyDrain = 167,
            ChainStratagem = 7436, BanefulImpaction = 37012;
    }

    // Gauge is the Aetherflow stacks.
    private static IEnumerable<uint> SchGcd(Rot s)
    {
        if (s.Left(Sch.Biolysis) < 3f) yield return Sch.Biolysis;
        yield return Sch.BroilIV;
        yield return Sch.RuinII;
    }

    private static IEnumerable<uint> SchOgcd(Rot s)
    {
        if (s.Gauge == 0) yield return Sch.Aetherflow;
        if (s.Gauge > 0) yield return Sch.EnergyDrain;
        if (s.Fresh(Sch.BanefulImpaction)) yield return Sch.BanefulImpaction;
    }

    private static void SchUsed(Rot s, uint id)
    {
        switch (id)
        {
            case Sch.Biolysis: s.Dot(id, DotSeconds); break;
            case Sch.Aetherflow: s.Gauge = 3; break;
            case Sch.EnergyDrain: s.Gauge--; break;
        }
    }

    // ── Astrologian ──────────────────────────────────────────────────────────

    private static class Ast
    {
        public const uint CombustIII = 16554, FallMalefic = 25871, Divination = 16552, AstralDraw = 37017, MinorArcana = 37022, Oracle = 37029;
    }

    private static IEnumerable<uint> AstGcd(Rot s)
    {
        if (s.Left(Ast.CombustIII) < 3f) yield return Ast.CombustIII;
        yield return Ast.FallMalefic;
    }

    private static IEnumerable<uint> AstOgcd(Rot s)
    {
        if (s.Fresh(Ast.Oracle)) yield return Ast.Oracle;
        yield return Ast.AstralDraw;
        if (s.Every(Ast.MinorArcana, 60f)) yield return Ast.MinorArcana;
    }

    private static void AstUsed(Rot s, uint id)
    {
        if (id == Ast.CombustIII) s.Dot(id, DotSeconds);
    }

    // ── Sage ─────────────────────────────────────────────────────────────────

    private static class Sge
    {
        public const uint DosisIII = 24312, Phlegma = 24313, ToxikonII = 24316, Psyche = 37033, Rhizomata = 24309, Eukrasia = 24290,
            EukrasianDosisIII = 24314, Kardia = 24285;
    }

    // Stack is 1 while Eukrasia is up, which the next GCD spends on the DoT.
    private static IEnumerable<uint> SgeGcd(Rot s)
    {
        if (s.Stack == 1) yield return Sge.EukrasianDosisIII;
        if (s.Left(Sge.EukrasianDosisIII) < 3f) yield return Sge.Eukrasia;
        if (s.Ready(Sge.Phlegma)) yield return Sge.Phlegma;
        yield return Sge.DosisIII;
        yield return Sge.ToxikonII;
    }

    private static IEnumerable<uint> SgeOgcd(Rot s)
    {
        if (s.Every(Sge.Kardia, float.MaxValue)) yield return Sge.Kardia;
        if (s.Every(Sge.Rhizomata, 90f)) yield return Sge.Rhizomata;
        yield break;
    }

    private static void SgeUsed(Rot s, uint id)
    {
        if (id == Sge.Eukrasia) s.Stack = 1;
        else if (id == Sge.EukrasianDosisIII)
        {
            s.Stack = 0;
            s.Dot(id, DotSeconds);
        }
    }

    // ── Dragoon ──────────────────────────────────────────────────────────────

    private static class Drg
    {
        public const uint TrueThrust = 75, SpiralBlow = 36955, ChaoticSpring = 25772, WheelingThrust = 3556,
            Drakesbane = 36952, LanceBarrage = 36954, HeavensThrust = 25771, FangAndClaw = 3554, LanceCharge = 85, BattleLitany = 3557, LifeSurge = 83,
            Geirskogul = 3555, Nastrond = 7400;
    }

    private static readonly uint[][] DrgChains =
    [
        [Drg.TrueThrust, Drg.SpiralBlow, Drg.ChaoticSpring, Drg.WheelingThrust, Drg.Drakesbane],
        [Drg.TrueThrust, Drg.LanceBarrage, Drg.HeavensThrust, Drg.FangAndClaw, Drg.Drakesbane],
    ];

    private static IEnumerable<uint> DrgGcd(Rot s)
    {
        yield return DrgChains[s.Phase][s.Combo];
    }

    // Stack is the Nastrond casts Geirskogul opens.
    private static IEnumerable<uint> DrgOgcd(Rot s)
    {
        if (s.Combo == 3) yield return Drg.LifeSurge;
        if (s.Stack > 0) yield return Drg.Nastrond;
        yield return Drg.Geirskogul;
    }

    private static void DrgUsed(Rot s, uint id)
    {
        if (id == DrgChains[s.Phase][s.Combo])
        {
            s.Combo++;
            if (s.Combo == DrgChains[s.Phase].Length)
            {
                s.Combo = 0;
                s.Phase ^= 1;
            }
        }
        if (id == Drg.Geirskogul) s.Stack = 3;
        else if (id == Drg.Nastrond) s.Stack--;
    }

    // ── Monk ─────────────────────────────────────────────────────────────────

    private static class Mnk
    {
        public const uint LeapingOpo = 36945, RisingRaptor = 36946, PouncingCoeurl = 36947, FiresReply = 36950, WindsReply = 36949,
            RiddleOfFire = 7395, Brotherhood = 7396, PerfectBalance = 69, RiddleOfWind = 25766, ForbiddenChakra = 3547;
    }

    private static readonly uint[] MnkForms = [Mnk.LeapingOpo, Mnk.RisingRaptor, Mnk.PouncingCoeurl];

    private static IEnumerable<uint> MnkGcd(Rot s)
    {
        if (s.Fresh(Mnk.FiresReply)) yield return Mnk.FiresReply;
        if (s.Fresh(Mnk.WindsReply) && !s.Fresh(Mnk.FiresReply)) yield return Mnk.WindsReply;
        yield return MnkForms[s.Combo];
    }

    // Gauge is the Chakra, one per GCD.
    private static IEnumerable<uint> MnkOgcd(Rot s)
    {
        if (s.Gauge >= 5) yield return Mnk.ForbiddenChakra;
        yield return Mnk.PerfectBalance;
        yield return Mnk.RiddleOfWind;
    }

    private static void MnkUsed(Rot s, uint id)
    {
        if (id == MnkForms[s.Combo]) s.Combo = (s.Combo + 1) % MnkForms.Length;
        if (id is Mnk.LeapingOpo or Mnk.RisingRaptor or Mnk.PouncingCoeurl) s.Gauge = Math.Min(5, s.Gauge + 1);
        else if (id == Mnk.ForbiddenChakra) s.Gauge = 0;
    }

    // ── Bard ─────────────────────────────────────────────────────────────────

    private static class Brd
    {
        public const uint BurstShot = 16495, Stormbite = 7407, CausticBite = 7406, IronJaws = 3560, RefulgentArrow = 7409,
            ApexArrow = 16496, RadiantEncore = 36977, RagingStrikes = 101, BattleVoice = 118, RadiantFinale = 25785, Barrage = 107,
            HeartbreakShot = 36975, EmpyrealArrow = 3558, Sidewinder = 3562, WanderersMinuet = 3559;
        public const float DotSeconds = BardDotSeconds;
    }

    private static IEnumerable<uint> BrdGcd(Rot s)
    {
        if (s.Left(Brd.Stormbite) <= 0f) yield return Brd.Stormbite;
        if (s.Left(Brd.CausticBite) <= 0f) yield return Brd.CausticBite;
        if (MathF.Min(s.Left(Brd.Stormbite), s.Left(Brd.CausticBite)) < 4f) yield return Brd.IronJaws;
        if (s.Proc) yield return Brd.RefulgentArrow;
        if (s.Fresh(Brd.RadiantEncore)) yield return Brd.RadiantEncore;
        if (s.Every(Brd.ApexArrow, 45f)) yield return Brd.ApexArrow;
        yield return Brd.BurstShot;
    }

    private static IEnumerable<uint> BrdOgcd(Rot s)
    {
        yield return Brd.WanderersMinuet;
        yield return Brd.HeartbreakShot;
        yield return Brd.EmpyrealArrow;
        yield return Brd.Sidewinder;
    }

    private static void BrdUsed(Rot s, uint id)
    {
        switch (id)
        {
            case Brd.Stormbite: case Brd.CausticBite: s.Dot(id, Brd.DotSeconds); break;
            case Brd.IronJaws: s.Dot(Brd.Stormbite, Brd.DotSeconds); s.Dot(Brd.CausticBite, Brd.DotSeconds); break;
            case Brd.RefulgentArrow: s.Proc = false; break;
            case Brd.BurstShot: s.Proc = s.Proc || s.Rng.NextSingle() < 0.35f; break;
        }
    }

    // ── Black Mage ───────────────────────────────────────────────────────────

    private static class Blm
    {
        public const uint BlizzardIII = 154, BlizzardIV = 3576, FireIII = 152, FireIV = 3577, Despair = 16505, FlareStar = 36989,
            HighThunder = 36986, Xenoglossy = 16507, Paradox = 25797, LeyLines = 3573, Triplecast = 7421, Manafont = 158;
    }

    // Combo walks Ice (Blizzard III, IV, Paradox) into Fire (Fire III, IV x4, Despair, Flare Star). While it
    // can't cast, the instants carry the GCD.
    private static readonly uint[] BlmCycle =
        [Blm.BlizzardIII, Blm.BlizzardIV, Blm.Paradox, Blm.FireIII, Blm.FireIV, Blm.FireIV, Blm.FireIV, Blm.FireIV, Blm.Despair, Blm.FlareStar];

    private static IEnumerable<uint> BlmGcd(Rot s)
    {
        if (s.Left(Blm.HighThunder) < 3f) yield return Blm.HighThunder;
        yield return BlmCycle[s.Combo];
        yield return Blm.Xenoglossy;
    }

    private static IEnumerable<uint> BlmOgcd(Rot s)
    {
        yield return Blm.Triplecast;
        if (s.Combo >= 3) yield return Blm.Manafont;
    }

    private static void BlmUsed(Rot s, uint id)
    {
        if (id == Blm.HighThunder) s.Dot(id, DotSeconds);
        if (id == BlmCycle[s.Combo]) s.Combo = (s.Combo + 1) % BlmCycle.Length;
    }
}
