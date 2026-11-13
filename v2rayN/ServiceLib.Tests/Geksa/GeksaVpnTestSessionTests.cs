using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaVpnTestSessionTests
{
    private static GeksaVpnProfileOption Profile(string id) => new(id, id, "VLESS", "", "test.invalid", 443);

    [Fact]
    public void Complete_OnlyRanksKnownPositiveResults()
    {
        var first = Profile("first");
        var second = Profile("second");
        var failed = Profile("failed");
        var session = new GeksaVpnTestSession([first, second, failed]);
        session.Start();
        session.Apply("foreign-id", "1").Should().BeNull();
        session.Apply("first", "70");
        session.Apply("second", "20");
        session.Apply("failed", "-1");

        session.Complete().Select(c => c.Profile.IndexId).Should().Equal("second", "first");
        failed.IsTesting.Should().BeFalse();
    }

    [Fact]
    public void Complete_ClearsPendingTextEvenWhenCoreReturnsNoNumericResult()
    {
        var skipped = Profile("skipped");
        var answered = Profile("answered");
        var session = new GeksaVpnTestSession([skipped, answered]);
        session.Start();
        session.Apply("skipped", "Skipped");
        session.Apply("answered", "15");
        session.Complete();

        skipped.DelayText.Should().Be("Нет ответа");
        skipped.IsTesting.Should().BeFalse();
        answered.DelayText.Should().Be("15 мс");
    }

    [Fact]
    public void CompletedSession_IgnoresLateCallbacksAndDoesNotCorruptNextTest()
    {
        var target = Profile("server");
        var old = new GeksaVpnTestSession([target]);
        old.Start();
        old.Complete();
        var next = new GeksaVpnTestSession([target]);
        next.Start();
        old.Apply("server", "2").Should().BeNull();
        target.IsTesting.Should().BeTrue();
        next.Apply("server", "40");
        next.Complete().Single().DelayMilliseconds.Should().Be(40);
    }

    [Fact]
    public void NewSession_DoesNotReusePreviousSuccessfulDelay()
    {
        var target = Profile("server");
        target.ApplyDelay(1);
        var session = new GeksaVpnTestSession([target, GeksaVpnProfileOption.CreateAutomatic()]);
        session.Start();
        session.Complete().Should().BeEmpty();
        target.DelayText.Should().Be("Нет ответа");
    }
}
