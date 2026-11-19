using System.Globalization;
using System.Numerics;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using EObjId = AnoMech.Scenarios.Umad.UmadConstants.EObjId;

namespace AnoMech.Tests;

// Bowels of Agony's three crystals appear together 0.39 s after it lands, on the spots the elements
// fire from. Fire and water each fade 0.69 s (the first) or 0.85 s (the second) after their attack and
// leave a second later; wind stays until the Limit Cut's Cyclones are done. With the pinned rolls the
// wind crystal is NE, fire SE and water NW.
public class UmadP3CrystalTests
{
    private const string Appears = "eobj_spawn";
    private const string Leaves = "despawn";
    private const string Fades = "0x25F";

    private sealed record Row(float Time, string Kind, string Actor, string Id, string Detail);

    [TestCase(UmadP3Element.Fire)]
    [TestCase(UmadP3Element.Water)]
    public void TheOpeningsCrystalsAppearTogetherAndLeaveAfterTheirAttacks(UmadP3Element first)
    {
        var rows = Rows(typeof(UmadP3FullScenario), 98f, o =>
        {
            var settings = (UmadP3FullStateOverrides)o;
            UmadP3FullScenarioTests.Pin(settings);
            settings.OpeningRolls.ShortElement = first;
        });
        var (shortCrystal, longCrystal) = first == UmadP3Element.Fire
            ? (EObjId.FireCrystal, EObjId.WaterCrystal)
            : (EObjId.WaterCrystal, EObjId.FireCrystal);
        Assert.Multiple(() =>
        {
            Assert.That(Spawns(rows).Select(s => s.Id), Is.EquivalentTo(new[] { EObjId.FireCrystal, EObjId.WaterCrystal, EObjId.WindCrystal }.Select(Id)));
            foreach (var crystal in new[] { EObjId.FireCrystal, EObjId.WaterCrystal, EObjId.WindCrystal })
                Assert.That(Spawn(rows, crystal).Time, Is.EqualTo(22.95f).Within(0.05f), $"{crystal} appears");
            Assert.That(Times(rows, shortCrystal, Fades), Is.EqualTo(new[] { 43.42f }).Within(0.05f), "the first crystal fades");
            Assert.That(Times(rows, shortCrystal, Leaves), Is.EqualTo(new[] { 44.42f }).Within(0.05f), "the first crystal leaves");
            Assert.That(Times(rows, longCrystal, Fades), Is.EqualTo(new[] { 70.55f }).Within(0.05f), "the second crystal fades");
            Assert.That(Times(rows, longCrystal, Leaves), Is.EqualTo(new[] { 71.55f }).Within(0.05f), "the second crystal leaves");
            Assert.That(Times(rows, EObjId.WindCrystal, Leaves), Is.EqualTo(new[] { 71.1f + 20.934f }).Within(0.05f), "the wind crystal leaves");
            Assert.That(Vector2.Distance(Position(Spawn(rows, EObjId.FireCrystal)), new Vector2(9.9f, 9.9f)), Is.LessThan(0.1f), "fire SE");
            Assert.That(Vector2.Distance(Position(Spawn(rows, EObjId.WaterCrystal)), new Vector2(-9.9f, -9.9f)), Is.LessThan(0.1f), "water NW");
            Assert.That(Vector2.Distance(Position(Spawn(rows, EObjId.WindCrystal)), new Vector2(9.9f, -9.9f)), Is.LessThan(0.1f), "wind NE");
            Assert.That(Spawns(rows).Select(s => s.Detail), Has.All.Contains("targetable=5"));
            Assert.That(Spawn(rows, EObjId.WindCrystal).Detail, Does.Contain($"layout=0x{EObjId.WindCrystalNeLayoutId:X}").And.Contain($"arg2=0x{EObjId.WindCrystalArg2:X}"));
        });
    }

    // Without an opening to hand it over, the drill puts the wind crystal on its own boss spot.
    [TestCase(1, EObjId.WindCrystalSeLayoutId)]
    [TestCase(3, EObjId.WindCrystalNeLayoutId)]
    [TestCase(5, EObjId.WindCrystalNwLayoutId)]
    [TestCase(7, EObjId.WindCrystalSwLayoutId)]
    public void TheLimitCutDrillsWindCrystalStandsThroughItsCyclones(int bossSpot, uint layout)
    {
        var rows = Rows(typeof(UmadP3LimitCutScenario), 22f, o =>
        {
            var settings = (UmadP3LimitCutStateOverrides)o;
            UmadP3LimitCutScenarioTests.Pin(settings);
            settings.BossSpot = bossSpot;
        });
        var spot = UmadP3LimitCutState.OnCircle(UmadP3LimitCutState.SpotHeading(bossSpot), 14f);
        Assert.Multiple(() =>
        {
            Assert.That(Spawns(rows).Select(s => s.Id), Is.EqualTo(new[] { Id(EObjId.WindCrystal) }));
            Assert.That(Spawn(rows, EObjId.WindCrystal).Time, Is.LessThan(0.1f));
            Assert.That(Spawn(rows, EObjId.WindCrystal).Detail, Does.Contain($"layout=0x{layout:X}").And.Contain($"arg2=0x{EObjId.WindCrystalArg2:X}"));
            Assert.That(Vector2.Distance(Position(Spawn(rows, EObjId.WindCrystal)), new Vector2(spot.X, spot.Z)), Is.LessThan(0.1f));
            Assert.That(Times(rows, EObjId.WindCrystal, Leaves), Is.EqualTo(new[] { 20.934f }).Within(0.05f));
        });
    }

    private static string Id(uint eobj) => eobj.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<Row> Spawns(List<Row> rows) => rows.Where(r => r.Kind == Appears && r.Id is var id && IsCrystal(id));

    private static bool IsCrystal(string id) => id == Id(EObjId.FireCrystal) || id == Id(EObjId.WaterCrystal) || id == Id(EObjId.WindCrystal);

    private static Row Spawn(List<Row> rows, uint eobj) => Spawns(rows).Single(r => r.Id == Id(eobj));

    // An event object's fades and despawns, by the entity id its spawn row gave it.
    private static float[] Times(List<Row> rows, uint eobj, string what)
    {
        var actor = Spawn(rows, eobj).Actor;
        return rows.Where(r => r.Actor == actor && (what == Leaves ? r.Kind == Leaves : r.Kind == "ac" && r.Id == what)).Select(r => r.Time).ToArray();
    }

    // The timeline holds world coordinates; the scenario's are from the arena's centre.
    private static Vector2 Position(Row spawn)
    {
        var cells = spawn.Detail.Split('|');
        var origin = new UmadP3FullScenario().Phase.Zone.Origin;
        return new Vector2(float.Parse(cells[0], CultureInfo.InvariantCulture) - origin.X, float.Parse(cells[1], CultureInfo.InvariantCulture) - origin.Z);
    }

    // The sim's own timeline of the run, x and z packed behind the detail for the spawn rows.
    private static List<Row> Rows(Type scenario, float stopAt, Action<object> overrides)
    {
        var path = Path.Combine(Path.GetTempPath(), $"umadp3crystals-{Guid.NewGuid():N}.csv");
        try
        {
            var run = ScenarioRun.Execute(scenario, 0, 7, new ScenarioRunOptions { StopAt = stopAt, Overrides = overrides, TimelinePath = path });
            Assert.That(run.Passed, run.ToString);
            var lines = File.ReadLines(path).ToList();
            var columns = Cells(lines[0]);
            int Column(string name) => columns.IndexOf(name);
            return lines.Skip(1).Select(Cells).Select(c => new Row(
                float.Parse(c[Column("t")], CultureInfo.InvariantCulture), c[Column("kind")], c[Column("actor")], c[Column("id")],
                c[Column("kind")] == Appears ? $"{c[Column("x")]}|{c[Column("z")]}|{c[Column("detail")]}" : c[Column("detail")])).ToList();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }
}
