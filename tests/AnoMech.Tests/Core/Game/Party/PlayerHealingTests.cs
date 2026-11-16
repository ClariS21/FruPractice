using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Tests;

public partial class BotHealingTests
{
    [Test]
    public void APlayersHealerButtonHealsThePartyThroughTheSamePath()
    {
        world.BotCombatEnabled = false;
        Raidwides(3);
        var before = Members.ToDictionary(m => m, Hp);
        new HealActionHandler().OnAction(ActionType.Action, 24299u, (ulong)fake.BattleCharas.Player.GameObjectId.ObjectId);
        Advance(1f);
        var healed = Members.Count(m => Hp(m) > before[m] + m.Proxy!.MaxHealth / 20);
        Assert.That(healed, Is.GreaterThan(3), "Ixochole reached the party");
        var gains = Members.Where(m => Hp(m) < m.Proxy!.MaxHealth).Select(m => (float)((long)Hp(m) - before[m]) / m.Proxy!.MaxHealth).ToList();
        Assert.That(gains.Max(), Is.InRange(0.146f - 0.002f, 0.189f + 0.002f), "within Ixochole's p25..p75");
        Assert.That(gains.Max(), Is.LessThanOrEqualTo(0.191f));
    }
}
