using System.Globalization;
using AnoMech.Core.ActBridge;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P4Full;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Umad.P5Full;

namespace AnoMech.Tests;

// What cactbot's DMU P3 to P5 triggers read from the bridge, one case per trigger (or per group
// sharing one line), from the audit in docs/superpowers/specs/2026-11-17-dmu-cactbot.md. Lines are
// as the emitter writes them, without ACT's timestamp: field n here is cactbot's field n + 1.
public class UmadCactbotLineTests
{
    private const int Seed = 4242;

    // A line with the scenario time it went out at.
    public sealed record Line(float Time, string[] F);

    private sealed class Sink : IActLineSink
    {
        public float Now { get; set; }
        public bool Connected => true;
        public List<Line> Lines { get; } = [];
        public void Send(string line) => Lines.Add(new Line(Now, line.Split('|')));
    }

    private sealed record Run(IReadOnlyList<Line> Lines, ScenarioRun Result, HashSet<uint> EObjs, HashSet<(uint BNpcBase, uint NameId)> Enemies);

    private static readonly Dictionary<Type, Run> Runs = [];

    // One run per scenario, shared by its cases; also notes what was spawned, for what ACT reads from memory.
    private static Run RunOf(Type scenario)
    {
        if (Runs.TryGetValue(scenario, out var cached)) return cached;
        var sink = new Sink();
        var eobjs = new HashSet<uint>();
        var enemies = new HashSet<(uint, uint)>();
        var result = ScenarioRun.Execute(scenario, 0, Seed, new ScenarioRunOptions
        {
            ActSink = sink,
            Probe = probe =>
            {
                sink.Now = probe.Time;
                foreach (var child in probe.World.Children)
                    if (child is SimEventObject eobj) eobjs.Add(eobj.EObjRowId);
                    else if (child is SimEnemy enemy) enemies.Add((enemy.BNpcBaseId, enemy.SpawnConfig.NameId));
            },
        });
        return Runs[scenario] = new Run(sink.Lines, result, eobjs, enemies);
    }

    // ── Line shapes ──────────────────────────────────────────────────────────

    private static bool IsPlayer(string id) => id.StartsWith('1');

    private static bool StartsUsing(string[] f, string id, string source)
        => f[0] == "20" && f[3] == id && f[2] == source && float.Parse(f[7], CultureInfo.InvariantCulture) > 0f;

    private static bool Ability(string[] f, string id, string source) => f[0] is "21" or "22" && f[3] == id && f[2] == source;

    // In game the client capitalizes the sheet's lowercase "black hole", as retail's logs show; the
    // fake keeps the sheet's case.
    private static bool AbilityFromBlackHole(string[] f, string id)
        => f[0] is "21" or "22" && f[3] == id && string.Equals(f[2], "Black Hole", StringComparison.OrdinalIgnoreCase);

    private static bool GainsOnPlayer(string[] f, string id) => f[0] == "26" && f[1] == id && IsPlayer(f[6]);

    private static bool LosesOnPlayer(string[] f, string id) => f[0] == "30" && f[1] == id && IsPlayer(f[6]);

    private static bool HeadMarker(string[] f, string id) => f[0] == "27" && f[5] == id;

    private static float Duration(string[] f) => float.Parse(f[3], CultureInfo.InvariantCulture);

    public sealed record Needed(string Trigger, Type[] Scenarios, string What, Func<IReadOnlyList<Line>, bool> Present)
    {
        public override string ToString() => Trigger;
    }

    private static Needed Any(string trigger, Type[] scenarios, string what, Func<string[], bool> line)
        => new(trigger, scenarios, what, lines => lines.Any(l => line(l.F)));

    private static readonly Type[] P3Full = [typeof(UmadP3FullScenario)];
    private static readonly Type[] P3LimitCut = [typeof(UmadP3FullScenario), typeof(UmadP3LimitCutScenario)];
    private static readonly Type[] P3BlackHole = [typeof(UmadP3FullScenario), typeof(UmadP3BlackHoleScenario)];
    private static readonly Type[] P3All = [typeof(UmadP3FullScenario), typeof(UmadP3LimitCutScenario), typeof(UmadP3BlackHoleScenario)];
    private static readonly Type[] P4All = [typeof(UmadP4FullScenario), typeof(UmadP4KefkaSaysScenario)];

    // ── P3 ───────────────────────────────────────────────────────────────────

    private static IEnumerable<TestCaseData> P3Triggers() => Cases(
    [
        Any("DMU P3 Epic Hero/Fated Hero Collect, Debuffs", P3All, "26 1060 on a player",
            f => GainsOnPlayer(f, "1060")),
        Any("DMU P3 Epic Hero/Fated Hero Collect, Debuffs (Exdeath side)", P3All, "26 1062 on a player",
            f => GainsOnPlayer(f, "1062")),
        Any("DMU P3 Bowels of Agony", P3Full, "20 BAF2 from Chaos", f => StartsUsing(f, "BAF2", "Chaos")),
        new("DMU P3 Entropy and Dynamic Fluid Debuff Collector, Entropy and Fire Crystal, Dynamic Fluid and Water Crystal", P3Full,
            "26 640 and 641 on players, one element short (19s), the other long (46s)",
            lines =>
            {
                var elements = Fields(lines).Where(f => GainsOnPlayer(f, "640") || GainsOnPlayer(f, "641")).ToList();
                return elements.Any(f => Duration(f) < 20f) && elements.Any(f => Duration(f) > 40f)
                       && elements.Any(f => f[1] == "640") && elements.Any(f => f[1] == "641");
            }),
        Any("DMU P3 Headwind/Tailwind Debuff Collector, Bowels of Agony Debuffs and Short Element", P3LimitCut, "26 642 or 643 on a player",
            f => GainsOnPlayer(f, "642") || GainsOnPlayer(f, "643")),
        Any("DMU P3 Headwind/Tailwind Cleanup", P3LimitCut, "30 642 or 643 on a player", f => LosesOnPlayer(f, "642") || LosesOnPlayer(f, "643")),
        Any("DMU P3 Long Crystal and Wind Crystal Locations, Wind Crystal Location", P3Full, "21 BAF3 or BAF6 from Chaos",
            f => Ability(f, "BAF3", "Chaos") || Ability(f, "BAF6", "Chaos")),
        Any("DMU P3 Wind Crystal Next Flag", P3BlackHole, "21 BAFF from Chaos", f => Ability(f, "BAFF", "Chaos")),
        Any("DMU P3 Thunder III AOE", P3Full, "20 BB12 from Exdeath", f => StartsUsing(f, "BB12", "Exdeath")),
        Any("DMU P3 Thunder III Tankbuster", P3All, "20 BB09 from Exdeath", f => StartsUsing(f, "BB09", "Exdeath")),
        Any("DMU P3 Thunder III Tank Swap", P3All, "21 BB09 from Exdeath naming the player it hits",
            f => Ability(f, "BB09", "Exdeath") && IsPlayer(f[5]) && f[6].Length > 0),
        Any("DMU P3 Longitudinal/Latitudinal Implosion", P3BlackHole, "20 BAFD or BAFE from Chaos",
            f => StartsUsing(f, "BAFD", "Chaos") || StartsUsing(f, "BAFE", "Chaos")),
        Any("DMU P3 Umbra Smash", P3BlackHole, "21 BAFD or BAFE from Chaos", f => Ability(f, "BAFD", "Chaos") || Ability(f, "BAFE", "Chaos")),
        new("DMU P3 Ultima Blaster Collect, Rotation", P3LimitCut, "264 BAE3, the first two with different headings",
            lines =>
            {
                var headings = Fields(lines).Where(f => f[0] == "264" && f[2] == "BAE3")
                    .Select(f => float.Parse(f[8], CultureInfo.InvariantCulture)).ToList();
                return headings.Count >= 2 && MathF.Abs(headings[1] - headings[0]) > 0.1f;
            }),
        Any("DMU P3 Vacuum Wave, Vacuum Wave Tank LB3", P3LimitCut, "20 BB13 from Exdeath", f => StartsUsing(f, "BB13", "Exdeath")),
        new("DMU P1 Ultima Blaster Location (the P3 Limit Cut numbers)", P3LimitCut, "27 0150-0153 and 01B5-01B8 on players",
            lines => new[] { "0150", "0151", "0152", "0153", "01B5", "01B6", "01B7", "01B8" }
                .All(id => Fields(lines).Any(f => HeadMarker(f, id) && IsPlayer(f[1])))),
        Any("DMU P3 Max Collect ID", P3BlackHole, "20 BAE5 from Kefka", f => StartsUsing(f, "BAE5", "Kefka")),
        new("DMU P3 In Line Debuff Collector, In Line Debuff + Accretion 1", P3BlackHole, "26 BBC, BBD and BBE on players",
            lines => new[] { "BBC", "BBD", "BBE" }.All(id => Fields(lines).Any(f => GainsOnPlayer(f, id)))),
        Any("DMU P3 Accretion Collector", P3BlackHole, "26 644 on a player", f => GainsOnPlayer(f, "644")),
        Any("DMU P3 Accretion Cleanup", P3BlackHole, "30 644 on a player", f => LosesOnPlayer(f, "644")),
        Any("DMU P3 Accretion 2", P3BlackHole, "26 D2C on a player", f => GainsOnPlayer(f, "D2C")),
        new("DMU P3 Boss Teleport Collect, Boss Teleport Location, White Hole + Boss Teleport Location", P3BlackHole,
            "273 0197 1E44 on the Kefka that cast Max, each within a second after a 271 for him",
            lines =>
            {
                var kefka = Fields(lines).FirstOrDefault(f => StartsUsing(f, "BAE5", "Kefka"))?[1];
                var teleports = lines.Where(l => l.F[0] == "273" && l.F[1] == kefka && l.F[2] == "0197" && l.F[3] == "1E44").ToList();
                return kefka is not null && teleports.Count == 5
                       && teleports.All(t => lines.Any(l => l.F[0] == "271" && l.F[1] == kefka && t.Time - l.Time is >= 0f and <= 1f));
            }),
        Any("DMU P3 Slap Happy", P3BlackHole, "20 BAE6 or BAE7 from Kefka", f => StartsUsing(f, "BAE6", "Kefka") || StartsUsing(f, "BAE7", "Kefka")),
        new("DMU P3 Nothingness Counter, Black Hole 3 Nothingness 4/5, Black Hole 4 Nothingness 7/8, Blizzard III Puddles", P3BlackHole,
            "21 BAFC from Black Hole at ten moments (the counter's suppress is 1s)",
            lines => lines.Where(l => AbilityFromBlackHole(l.F, "BAFC")).Select(l => MathF.Round(l.Time)).Distinct().Count() == 10),
        new("DMU P3 Black Hole Tether Collect (SpawnNpcExtra), Black Hole 1/3/4/5", P3BlackHole,
            "272 with tether 0054 for sets 1, 3, 4 and 5 (1 + 3 + 3 + 2 holes)",
            lines => lines.Count(l => l.F[0] == "272" && l.F[3] == "0054") == 9),
        Any("DMU P3 Black Hole Tether Collect (NetworkTether), Black Hole 2, Black Hole 6", P3BlackHole, "35 0054 from a black hole",
            f => f[0] == "35" && f[7] == "0054" && string.Equals(f[2], "Black Hole", StringComparison.OrdinalIgnoreCase)),
        Any("DMU P3 Damning Edict", P3BlackHole, "20 BB01 from Chaos", f => StartsUsing(f, "BB01", "Chaos")),
        Any("DMU P3 Look upon Me and Despair 1", P3BlackHole, "20 BAEC from Kefka", f => StartsUsing(f, "BAEC", "Kefka")),
        Any("DMU P3 Look upon Me and Despair 2", P3BlackHole, "20 BAED from Kefka", f => StartsUsing(f, "BAED", "Kefka")),
        Any("DMU P3 Knock Down Collect", P3BlackHole, "27 00A1 on a player", f => HeadMarker(f, "00A1") && IsPlayer(f[1])),
        Any("DMU P3 Knock Down 1 State", P3BlackHole, "21 BB02 from Chaos", f => Ability(f, "BB02", "Chaos")),
        Any("DMU P3 Knock Down 1", P3BlackHole, "20 BB0D from Exdeath", f => StartsUsing(f, "BB0D", "Exdeath")),
        Any("DMU P3 Blizzard III State, Keep Moving (Tower Role)", P3BlackHole, "20 BB11 from Exdeath", f => StartsUsing(f, "BB11", "Exdeath")),
        Any("DMU P3 Blizzard III Keep Moving (Stack Role)", P3BlackHole, "21 BB03 from Chaos", f => Ability(f, "BB03", "Chaos")),
    ]);

    private static IEnumerable<string[]> Fields(IReadOnlyList<Line> lines) => lines.Select(l => l.F);

    // ── P4 ───────────────────────────────────────────────────────────────────

    private static IEnumerable<TestCaseData> P4Triggers() => Cases(
    [
        Any("DMU Phase 1-4 Tracker (p4)", P4All, "20 C2DC from Kefka", f => StartsUsing(f, "C2DC", "Kefka")),
        new("DMU P4 Chaos and Neo Exdeath Debuff Collect", P4All, "26 808 with count 45F/460 on Chaos and 461/462 on Neo Exdeath",
            lines => Fields(lines).Any(f => f[0] == "26" && f[1] == "808" && f[7] == "Chaos" && f[8] is "45F" or "460")
                     && Fields(lines).Any(f => f[0] == "26" && f[1] == "808" && f[7] == "Neo Exdeath" && f[8] is "461" or "462")),
        Any("DMU P4 Second and Fourth Debuffs (Early)", P4All, "20 BB20 or BB21", f => f[0] == "20" && f[3] is "BB20" or "BB21"),
        new("DMU P4 Grand Cross Counter, Grand Cross", P4All, "20 BB14 from Neo Exdeath, three times",
            lines => Fields(lines).Count(f => StartsUsing(f, "BB14", "Neo Exdeath")) == 3),
        new("DMU P4 Debuff Collect, Tsunami/Inferno and First/Third Debuffs (Early), Fifth Debuffs", P4All,
            "26 15A5-15AA, 566 and 1C6 on players",
            lines => new[] { "15A7", "15A8", "15A9", "15AA", "566", "1C6" }.All(id => Fields(lines).Any(f => GainsOnPlayer(f, id)))
                     && Fields(lines).Any(f => GainsOnPlayer(f, "15A5") || GainsOnPlayer(f, "15A6"))),
        Any("DMU P4 Entropy/Dynamic Fluid Collect, Stray Flames and Long Debuffs, Fake Stray Spray", P4All, "26 15AB or 15AC on a player",
            f => GainsOnPlayer(f, "15AB") || GainsOnPlayer(f, "15AC")),
        new("DMU P4 Cursed Shriek, First/Second Cursed Shriek, Entropy, Dynamic Fluid", P4All, "26 15A7 at 60s and at 69s",
            lines => Fields(lines).Any(f => GainsOnPlayer(f, "15A7") && Duration(f) < 61f) && Fields(lines).Any(f => GainsOnPlayer(f, "15A7") && Duration(f) > 68f)),
        Any("DMU P4 Acceleration Bomb Reminder", P4All, "26 15AA on a player", f => GainsOnPlayer(f, "15AA")),
        Any("DMU P4 Flood of Naught", P4All, "20 C392/C393/C3A1/C3A2 from Neo Exdeath",
            f => f[0] == "20" && f[3] is "C392" or "C393" or "C3A1" or "C3A2" && f[2] == "Neo Exdeath"),
        Any("DMU P4 Short Debuffs", P4All, "21 C394 or C395 from Neo Exdeath", f => Ability(f, "C394", "Neo Exdeath") || Ability(f, "C395", "Neo Exdeath")),
        new("DMU P4 Mana Charge Collect", P4All, "26 5CC and 5CD on Kefka",
            lines => new[] { "5CC", "5CD" }.All(id => Fields(lines).Any(f => f[0] == "26" && f[1] == id && f[7] == "Kefka"))),
        new("DMU P1 and P4 Mystery Magic Collect", P4All, "27 for true or fake ice (02A3/02A4) and thunder (02A5/02A6) on Kefka",
            lines => Fields(lines).Any(f => (HeadMarker(f, "02A3") || HeadMarker(f, "02A4")) && f[2] == "Kefka")
                     && Fields(lines).Any(f => (HeadMarker(f, "02A5") || HeadMarker(f, "02A6")) && f[2] == "Kefka")),
        Any("DMU P1 and P4 Mystery Magic Ice and Thunder, Mystery Magic Cleanup", P4All, "20 BA94 from Kefka", f => StartsUsing(f, "BA94", "Kefka")),
        Any("DMU P4 Thrumming Thunder III", P4All, "20 C5DE from Kefka", f => StartsUsing(f, "C5DE", "Kefka")),
        Any("DMU P4 Ultima Upsurge", P4All, "20 C24A from Kefka", f => StartsUsing(f, "C24A", "Kefka")),
        Any("DMU P4 Blizzard III Blowout", P4All, "20 BA95 from Kefka", f => StartsUsing(f, "BA95", "Kefka")),
        Any("DMU P4 Mana Release", P4All, "20 BAA5 from Kefka", f => StartsUsing(f, "BAA5", "Kefka")),
    ]);

    // ── P5 ───────────────────────────────────────────────────────────────────

    // P5's one trigger answers the Ultima Upsurge that opens P5, which P4's full phase plays at its end
    // when the HP check passes (the Kefka Says drill ends on the recorded wipe; the P5 drills start at
    // Ultima Repeater).
    private static IEnumerable<TestCaseData> P5Triggers() => Cases(
    [
        new("DMU P5 Ultima Upsurge", [typeof(UmadP4FullScenario)], "20 C24A from Kefka twice: P4's own and P5's opener",
            lines => Fields(lines).Count(f => StartsUsing(f, "C24A", "Kefka")) == 2),
        Any("DMU Phase 5 Tracker", [typeof(UmadP4FullScenario)], "21 C3FD from Kefka", f => Ability(f, "C3FD", "Kefka")),
        Any("Ultima Repeater (timeline)", [typeof(UmadP5FullScenario)], "20 BB40 from Kefka", f => StartsUsing(f, "BB40", "Kefka")),
    ]);

    private static IEnumerable<TestCaseData> Cases(IEnumerable<Needed> needed)
        => from need in needed
           from scenario in need.Scenarios
           select new TestCaseData(need, scenario).SetName($"{need.Trigger} [{scenario.Name}]");

    [TestCaseSource(nameof(P3Triggers))]
    public void P3TriggerLines(Needed need, Type scenario) => AssertPresent(need, scenario);

    [TestCaseSource(nameof(P4Triggers))]
    public void P4TriggerLines(Needed need, Type scenario) => AssertPresent(need, scenario);

    [TestCaseSource(nameof(P5Triggers))]
    public void P5TriggerLines(Needed need, Type scenario) => AssertPresent(need, scenario);

    private static void AssertPresent(Needed need, Type scenario)
    {
        var run = RunOf(scenario);
        Assert.That(run.Result.Failure, Is.Null, run.Result.ToString);
        Assert.That(need.Present(run.Lines), Is.True, $"{need.Trigger}: no {need.What} in {scenario.Name}");
    }

    // Every DMU run opens with DMU's director, not FRU's, and sends no 00 line (the bridge drops them).
    [TestCase(typeof(UmadP3FullScenario))]
    [TestCase(typeof(UmadP4FullScenario))]
    [TestCase(typeof(UmadP5FullScenario))]
    public void RunsOpenWithDmusDirector(Type scenario)
    {
        var lines = Fields(RunOf(scenario).Lines).ToList();
        Assert.That(string.Join('|', lines.First(f => f[0] == "33")), Is.EqualTo("33|800375D2|40000006|1C20|00|00|00"));
        Assert.That(lines, Has.None.Matches<string[]>(f => f[0] is "00" or "0"));
    }

    // ACT and OverlayPlugin read these from the client's memory (03 and 261), not from the bridge.
    [Test]
    public void ActorsCactbotReadsFromMemory()
    {
        var p3 = RunOf(typeof(UmadP3FullScenario));
        Assert.Multiple(() =>
        {
            // DMU P3 Crystal Location Collector: BNpcIDs 1EC03A (fire), 1EC03B (water) and 1EC03C (wind).
            Assert.That(p3.EObjs, Is.SupersetOf(new uint[] { 0x1EC03A, 0x1EC03B, 0x1EC03C }));
            // DMU P3 Black Hole Tracker: AddedCombatant named Black Hole, BNpcID 4C38.
            Assert.That(p3.Enemies, Does.Contain((0x4C38u, UmadConstants.BNpcNameId.BlackHole)));
            Assert.That(Natives.Data.BNpcName(UmadConstants.BNpcNameId.BlackHole), Is.EqualTo("black hole").IgnoreCase);
        });
    }
}
