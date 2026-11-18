using System.Numerics;
using AnoMech.Scenarios.Umad.P5Full;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

public class UmadP5ForsakenTests
{
    private const float Puddle = 8f;
    private const float Body = 0.5f;
    // Inner corners stand this far from every outer point and from the centre.
    private const float CornerClearance = 9.55f;

    private static readonly (int Side, int Pick)[] Rolls = [.. from side in new[] { -1, 1 } from pick in Enumerable.Range(0, 4) select (side, pick)];

    // Every roll of every set, along the guide's path from waymark C: the orange lands under the
    // party; the next waymark is clear of it, of the set's black holes and of every void; the eight
    // black holes take eight different points; and the walk round Kefka to the next waymark touches no
    // void already down.
    [Test]
    public void TheGuidePathStaysClearForEveryRoll()
    {
        foreach (var r0 in Rolls)
        foreach (var r1 in Rolls)
        foreach (var r2 in Rolls)
        foreach (var r3 in Rolls)
        {
            (int Side, int Pick)[] sets = [r0, r1, r2, r3];
            var party = new Vector3(0f, 0f, 10.5f);
            var voids = new List<Vector3>();
            for (var set = 0; set < sets.Length; set++)
            {
                var roll = new UmadP5ForsakenRoll(RegenHealer, sets[set].Side, sets[set].Pick);
                var holes = UmadP5Forsaken.Holes(set, UmadP5Forsaken.BearingStep(party), roll, voids);
                var context = $"rolls {string.Join(" ", sets)} set {set + 1}: holes {string.Join(" ", holes)}, voids {string.Join(" ", voids)}";
                Assert.That(holes, Has.Count.EqualTo(2), context);
                Assert.That(holes.Concat(voids).Distinct().Count(), Is.EqualTo(voids.Count + 2), context);

                var orange = UmadP5Forsaken.Orange(party, holes);
                Assert.That(Flat(orange, party), Is.LessThan(4f), context);
                var next = UmadP5FullAi.WaymarkClockwisePastTheOrange(orange);
                foreach (var danger in holes.Concat(voids).Append(orange))
                    Assert.That(Flat(next, danger), Is.GreaterThanOrEqualTo(CornerClearance - 0.01f), $"{context}: next {next} by {danger}");
                foreach (var point in Walk(party, next))
                    foreach (var hole in voids)
                        Assert.That(Flat(point, hole), Is.GreaterThan(Puddle + Body), $"{context}: walk at {point} into the void at {hole}");

                voids.AddRange(holes);
                party = next;
            }
        }
    }

    [Test]
    public void TheOrangeAvoidsTheSetsOwnBlackHoles()
    {
        var marker = new Vector3(1.7f, 0f, 10f);
        var holes = new[] { new Vector3(0f, 0f, 13.5f), new Vector3(13.5f, 0f, 0f) };
        Assert.That(UmadP5Forsaken.Orange(marker, holes), Is.EqualTo(new Vector3(6.75f, 0f, 6.75f)));
    }

    // Round Kefka at the walk's own radius, clockwise from where the party is to the next waymark.
    private static IEnumerable<Vector3> Walk(Vector3 from, Vector3 to)
    {
        var start = MathF.Atan2(from.X, -from.Z);
        var sweep = (MathF.Atan2(to.X, -to.Z) - start + MathF.Tau) % MathF.Tau;
        var radius = MathF.Max(MathF.Min(from.Length(), to.Length()), CornerClearance);
        for (var i = 0; i <= 20; i++)
        {
            var angle = start + sweep * i / 20f;
            var r = i == 0 ? from.Length() : i == 20 ? to.Length() : radius;
            yield return new Vector3(MathF.Sin(angle) * r, 0f, -MathF.Cos(angle) * r);
        }
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
