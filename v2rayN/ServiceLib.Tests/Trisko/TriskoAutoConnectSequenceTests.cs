using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoAutoConnectSequenceTests
{
    private static TriskoVpnTestCandidate Candidate(string id, int delay) =>
        new(new(id, id, "VLESS", "", "test.invalid", 443), delay);

    private static TriskoVpnSnapshot Connected(string id) => TriskoVpnSnapshot.Disconnected with
    {
        State = TriskoVpnState.Connected, ProfileId = id, IsCoreRunning = true, IsTunEnabled = true
    };

    [Fact]
    public async Task Failure_StopsBeforeNextServer_AndStopsOnSuccess()
    {
        var events = new List<string>();
        var current = "";
        var result = await TriskoAutoConnectSequence.RunAsync(
            [Candidate("slow", 90), Candidate("fast", 10), Candidate("middle", 30)],
            (candidate, _) => { current = candidate.Profile.IndexId; events.Add("select:" + current); return Task.FromResult(true); },
            () => { events.Add("connect:" + current); return Task.FromResult(current == "middle" ? Connected(current) : TriskoVpnSnapshot.Disconnected); },
            () => { events.Add("stop"); return Task.FromResult(TriskoVpnSnapshot.Disconnected); },
            (_, _) => { });

        result.Connected!.Profile.IndexId.Should().Be("middle");
        events.Should().Equal("select:fast", "connect:fast", "stop", "select:middle", "connect:middle");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    public async Task AllFailures_StopAfterAtMostThree_AndCleanUpLastAttempt(int count, int expected)
    {
        var attempts = 0;
        var stops = 0;
        var result = await TriskoAutoConnectSequence.RunAsync(
            Enumerable.Range(1, count).Select(i => Candidate(i.ToString(), i)).ToList(),
            (_, _) => Task.FromResult(true),
            () => { attempts++; return Task.FromResult(TriskoVpnSnapshot.Disconnected); },
            () => { stops++; return Task.FromResult(TriskoVpnSnapshot.Disconnected); },
            (_, _) => { });

        result.Connected.Should().BeNull();
        attempts.Should().Be(expected);
        stops.Should().Be(expected);
    }

    [Fact]
    public async Task StopFailure_PreventsSwitchingProfileWithRunningCore()
    {
        var selections = 0;
        var result = await TriskoAutoConnectSequence.RunAsync(
            [Candidate("first", 10), Candidate("second", 20)],
            (_, _) => { selections++; return Task.FromResult(true); },
            () => Task.FromResult(TriskoVpnSnapshot.Disconnected),
            () => Task.FromResult(Connected("first")),
            (_, _) => { });

        result.StopFailed.Should().BeTrue();
        selections.Should().Be(1);
    }

    [Fact]
    public async Task RemovedProfile_IsSkipped_AndWrongConnectedProfileIsNotSuccess()
    {
        var attempts = 0;
        var stops = 0;
        var result = await TriskoAutoConnectSequence.RunAsync(
            [Candidate("removed", 10), Candidate("available", 20)],
            (candidate, _) => Task.FromResult(candidate.Profile.IndexId != "removed"),
            () => { attempts++; return Task.FromResult(Connected("unrelated")); },
            () => { stops++; return Task.FromResult(TriskoVpnSnapshot.Disconnected); },
            (_, _) => { });

        result.Connected.Should().BeNull();
        attempts.Should().Be(1);
        stops.Should().Be(1);
    }
}
