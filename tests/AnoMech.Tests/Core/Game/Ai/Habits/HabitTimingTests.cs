using AnoMech.Core.Game.Ai.Habits;

namespace AnoMech.Tests;

// HabitTiming.Departure, with a 1 s walk and a 1 s reaction unless a test says otherwise.
public class HabitTimingTests
{
    private static float Departure(float time = 0f, float knownAt = 0f, float reaction = 1f, float quantile = 0.5f,
        float slackUse = 0.5f, float travel = 1f, float? inBy = null, float? outBy = null, float? exitDistance = null,
        float? notBefore = null)
        => HabitTiming.Departure(time, knownAt, reaction, quantile, slackUse, travel, inBy, outBy, exitDistance, notBefore);

    [Test]
    public void WithSlackTheBotLeavesAfterItsReactionPlusItsShareOfTheSlack()
    {
        // latestSafe = 8 - 0.8 - 1 = 6.2; ready at 1, so 5.2 s of slack.
        Assert.Multiple(() =>
        {
            Assert.That(Departure(inBy: 8f), Is.EqualTo(1f + 0.5f * 5.2f).Within(1e-5f));
            Assert.That(Departure(inBy: 8f, slackUse: 0f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Departure(inBy: 8f, slackUse: 1f), Is.EqualTo(6.2f).Within(1e-5f));
            Assert.That(Departure(knownAt: 2f, inBy: 8f, slackUse: 0f), Is.EqualTo(3f).Within(1e-5f));
        });
    }

    [Test]
    public void ATightWindowSqueezesTheReactionsInOrder()
    {
        // latestSafe = 2 - 0.8 - 1 = 0.2, before the 3 s reaction ends: each bot leaves at its
        // reaction's quantile of what is left.
        var quantiles = new[] { 0.1f, 0.2f, 0.5f, 0.8f, 0.95f };
        var departures = quantiles.Select(u => Departure(reaction: 3f, quantile: u, inBy: 2f)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(departures, Is.Ordered.Ascending.And.Unique);
            for (var i = 0; i < quantiles.Length; i++)
                Assert.That(departures[i], Is.EqualTo(quantiles[i] * 0.2f).Within(1e-5f));
        });
    }

    [Test]
    public void NotBeforeHoldsTheDepartureBackUntilTheSpotIsSafe()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Departure(notBefore: 5f), Is.EqualTo(4f).Within(1e-5f));
            Assert.That(Departure(notBefore: 1.5f), Is.EqualTo(1f).Within(1e-5f), "already arrives after it");
            Assert.That(Departure(slackUse: 0f, inBy: 8f, notBefore: 6f), Is.EqualTo(5f).Within(1e-5f));
        });
    }

    [Test]
    public void OutByCountsOnlyTheWalkOutOfTheDanger()
    {
        Assert.Multiple(() =>
        {
            // A 3 s walk to an inBy of 10 would allow 6.2; leaving the danger takes 6.5 y = 1 s before 4 - 0.8.
            Assert.That(Departure(slackUse: 1f, travel: 3f, inBy: 10f, outBy: 4f, exitDistance: 6.5f), Is.EqualTo(2.2f).Within(1e-5f));
            // Without an exit distance the whole walk has to fit: latestSafe = 0.2, so the reaction is squeezed.
            Assert.That(Departure(slackUse: 1f, travel: 3f, outBy: 4f), Is.EqualTo(0.5f * 0.2f).Within(1e-5f));
        });
    }

    [Test]
    public void PastTheLatestSafeDepartureTheBotLeavesAtOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Departure(time: 2f, inBy: 2f), Is.EqualTo(2f));
            Assert.That(Departure(time: 2f, outBy: 3f), Is.EqualTo(2f));
        });
    }

    [Test]
    public void WithoutADeadlineTheBotLeavesAfterItsReaction()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Departure(time: 2f, knownAt: 3f), Is.EqualTo(4f));
            Assert.That(Departure(time: 2f, knownAt: 0f), Is.EqualTo(2f), "never before the move fires");
        });
    }
}
