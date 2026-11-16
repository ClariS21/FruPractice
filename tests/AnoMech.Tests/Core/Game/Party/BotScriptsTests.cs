using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class BotScriptsTests
{
    private static readonly DataminingGameData Data = new(new FakeRsvFunctions());

    // Jobs whose actions the Action sheet files under the class they grew from.
    private static readonly Dictionary<JobId, JobId> BaseClass = new()
    {
        [JobId.Warrior] = JobId.Marauder,
        [JobId.Paladin] = JobId.Gladiator,
        [JobId.Monk] = JobId.Pugilist,
        [JobId.Dragoon] = JobId.Lancer,
        [JobId.Bard] = JobId.Archer,
        [JobId.WhiteMage] = JobId.Conjurer,
        [JobId.BlackMage] = JobId.Thaumaturge,
        [JobId.Scholar] = JobId.Arcanist,
    };

    [Test]
    public void EveryPresetJobAndEveryTankHasAScript()
    {
        foreach (var job in PartyPresets.Standard.Select(p => (JobId)p.ClassJob).Concat([JobId.Warrior, JobId.Paladin, JobId.DarkKnight, JobId.Gunbreaker]))
            Assert.That(BotScripts.For((byte)job), Is.Not.Null, job.ToString());
    }

    // The sheet files these under no job, and not as player actions: they replace another action once a
    // trait is learned (Paradox for Fire/Blizzard, Drakesbane for Wheeling/Fang and Claw's finisher,
    // Eukrasian Dosis III for Dosis III under Eukrasia).
    private static readonly uint[] TraitReplacements = [25797, 36952, 24314];

    [Test]
    public void EveryActionIsAPlayerActionOfItsJob()
    {
        foreach (var (job, script) in BotScripts.All)
        {
            var owners = new List<int> { (int)job };
            if (BaseClass.TryGetValue(job, out var parent)) owners.Add((int)parent);
            foreach (var id in script.Ids)
            {
                var row = Data.Action(id);
                Assert.That(row, Is.Not.Null, $"{job} action {id}");
                var label = $"{job} {row!.Name} ({id})";
                Assert.That(row.ActionCategory, Is.AnyOf(2u, 3u, 4u), label);
                if (TraitReplacements.Contains(id))
                {
                    Assert.That(row.CastSeconds, Is.EqualTo(0f), label);
                    continue;
                }
                Assert.That(row.IsPlayerAction, label);
                Assert.That(owners, Does.Contain(Data.ActionClassJob(id)), $"{label} belongs to the job");
            }
            foreach (var id in script.Burst) Assert.That(script.Ids, Does.Contain(id), $"{job} burst {id}");
        }
    }

    [Test]
    public void EveryGapCloserIsAnOgcdOfItsJobWithTheClosersRange()
    {
        foreach (var (job, id) in BotGapClosers.All)
        {
            var row = Data.Action(id);
            Assert.That(row, Is.Not.Null, $"{job} {id}");
            Assert.That(row!.IsPlayerAction && row.ActionCategory == 4 && row.CastSeconds == 0f, $"{job} {row.Name}");
            Assert.That(Data.ActionClassJob(id), Is.EqualTo((int)job), $"{job} {row.Name}");
        }
    }

    // The script alone, driven the way BotCombat drives it: a GCD each 2.5 s and the oGCDs in the weave.
    private static (List<(float Time, uint Id)> Gcds, List<(float Time, uint Id)> Ogcds) Simulate(JobId job, int seed, float seconds, bool canCast = true)
    {
        var script = BotScripts.For((byte)job)!;
        var rot = new Rot { Rng = new Rng(seed) };
        var gcds = new List<(float, uint)>();
        var ogcds = new List<(float, uint)>();
        uint? Pick(IEnumerable<uint> candidates, bool allowCast)
        {
            foreach (var id in candidates)
            {
                var row = Data.Action(id)!;
                if ((row.CastSeconds > 0f && !allowCast) || !rot.Ready(id)) continue;
                return id;
            }
            return null;
        }
        void Use(uint id)
        {
            var recast = Data.Action(id)!.RecastSeconds;
            rot.Record(id, recast > 2.5f ? recast : 0f, Array.IndexOf(script.Burst, id) >= 0);
            script.Used(rot, id);
        }
        for (var t = 0f; t < seconds; t += 2.5f)
        {
            rot.Now = t;
            if (Pick(script.Gcd(rot), canCast) is { } gcd)
            {
                Use(gcd);
                gcds.Add((t, gcd));
            }
            rot.Now = t + 0.7f;
            for (var weave = 0; weave < 2; weave++)
            {
                if (Pick(script.Burst.Concat(script.Ogcd(rot)), false) is not { } ogcd) break;
                Use(ogcd);
                ogcds.Add((rot.Now, ogcd));
                rot.Now += 0.7f;
            }
        }
        return (gcds, ogcds);
    }

    [Test]
    public void EveryScriptPlaysAVariedRotationDeterministically()
    {
        foreach (var (job, _) in BotScripts.All)
        {
            var first = Simulate(job, 5, 120f);
            var again = Simulate(job, 5, 120f);
            Assert.That(first.Gcds, Is.EqualTo(again.Gcds), job.ToString());
            Assert.That(first.Gcds.Select(g => g.Id).Distinct().Count(), Is.GreaterThanOrEqualTo(2), job.ToString());
            Assert.That(first.Gcds.Count, Is.GreaterThan(40), job.ToString());
        }
    }

    [Test]
    public void ComboRunsInOrder()
    {
        var war = Simulate(JobId.Warrior, 1, 30f).Gcds.Select(g => g.Id).Where(id => id is 31 or 37 or 42 or 45).ToList();
        Assert.That(war.Take(6), Is.EqualTo(new uint[] { 31, 37, 45, 31, 37, 42 }), "Storm's Eye first, then Storm's Path while it holds");
        var pldIds = Simulate(JobId.Paladin, 1, 60f).Gcds.Select(g => g.Id).ToList();
        Assert.That(pldIds.Where(id => id is 9 or 15 or 3539).Take(6), Is.EqualTo(new uint[] { 9, 15, 3539, 9, 15, 3539 }));
        Assert.That(pldIds.IndexOf(16459), Is.LessThan(pldIds.IndexOf(7384)), "Confiteor opens the Requiescat window, Holy Spirit follows");
        Assert.That(pldIds, Does.Contain(16460), "Atonement follows Royal Authority");
        var drg = Simulate(JobId.Dragoon, 1, 30f).Gcds.Select(g => g.Id).Take(5);
        Assert.That(drg, Is.EqualTo(new uint[] { 75, 36955, 25772, 3556, 36952 }));
    }

    [TestCase(JobId.WhiteMage, 16532u)]
    [TestCase(JobId.Scholar, 16540u)]
    [TestCase(JobId.Astrologian, 16554u)]
    [TestCase(JobId.BlackMage, 36986u)]
    public void DotsComeBackBeforeTheyDrop(JobId job, uint dot)
    {
        var times = Simulate(job, 1, 150f).Gcds.Where(g => g.Id == dot).Select(g => g.Time).ToList();
        Assert.That(times.Count, Is.GreaterThanOrEqualTo(4));
        for (var i = 1; i < times.Count; i++) Assert.That(times[i] - times[i - 1], Is.InRange(25f, 30f), $"{job} DoT gap");
    }

    [Test]
    public void SageKeepsEukrasianDosisUpThroughEukrasia()
    {
        var gcds = Simulate(JobId.Sage, 1, 150f).Gcds;
        var dots = gcds.Where(g => g.Id == 24314).Select(g => g.Time).ToList();
        Assert.That(dots.Count, Is.GreaterThanOrEqualTo(4));
        for (var i = 1; i < dots.Count; i++) Assert.That(dots[i] - dots[i - 1], Is.InRange(27f, 33f));
        foreach (var dot in dots) Assert.That(gcds.Any(g => g.Id == 24290 && Math.Abs(g.Time - (dot - 2.5f)) < 0.01f), "Eukrasia comes right before it");
    }

    [Test]
    public void BurstComesAtTheStartAndEveryTwoMinutes()
    {
        var starts = Simulate(JobId.Bard, 1, 250f).Ogcds.Where(o => o.Id == 101).Select(o => o.Time).ToList();
        Assert.That(starts.Count, Is.EqualTo(3));
        Assert.That(starts[0], Is.LessThan(3f));
        Assert.That(starts[1] - starts[0], Is.InRange(120f, 125f));
        Assert.That(starts[2] - starts[1], Is.InRange(120f, 125f));
    }

    [Test]
    public void CastersFallBackToInstantsWhenTheyCannotCast()
    {
        foreach (var job in new[] { JobId.BlackMage, JobId.Scholar, JobId.Sage })
        {
            var ids = Simulate(job, 1, 60f, canCast: false).Gcds.Select(g => g.Id).ToList();
            Assert.That(ids, Is.Not.Empty, job.ToString());
            Assert.That(ids.Select(id => Data.Action(id)!.CastSeconds), Is.All.EqualTo(0f), job.ToString());
        }
    }

    [Test]
    public void BlmWalksIceIntoFire()
    {
        var ids = Simulate(JobId.BlackMage, 1, 60f).Gcds.Select(g => g.Id).Where(id => id != 36986).Take(10);
        Assert.That(ids, Is.EqualTo(new uint[] { 154, 3576, 25797, 152, 3577, 3577, 3577, 3577, 16505, 36989 }));
    }
}
