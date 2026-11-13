using AwesomeAssertions;
using ServiceLib.Trisko;
using ServiceLib.Enums;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoVpnControllerTests
{
    [Fact]
    public async Task ConnectAsync_ShouldPublishConnectingThenConnected()
    {
        var runtime = new FakeVpnRuntime();
        var controller = new TriskoVpnController(runtime);
        var states = new List<TriskoVpnState>();
        using var subscription = controller.SnapshotChanged.AsObservable()
            .Subscribe(new SnapshotObserver(snapshot => states.Add(snapshot.State)));

        var snapshot = await controller.ConnectAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Connected);
        snapshot.IsCoreRunning.Should().BeTrue();
        snapshot.ProfileName.Should().Be("Corporate VPN");
        runtime.ConnectCallCount.Should().Be(1);
        states.Should().ContainInOrder(TriskoVpnState.Connecting, TriskoVpnState.Connected);
    }

    [Fact]
    public async Task ConcurrentConnectAsync_ShouldStartRuntimeOnlyOnce()
    {
        var runtime = new FakeVpnRuntime { OperationDelay = TimeSpan.FromMilliseconds(50) };
        var controller = new TriskoVpnController(runtime);

        var first = controller.ConnectAsync(TestContext.Current.CancellationToken);
        var second = controller.ConnectAsync(TestContext.Current.CancellationToken);
        var snapshots = await Task.WhenAll(first, second);

        runtime.ConnectCallCount.Should().Be(1);
        snapshots.Should().AllSatisfy(snapshot =>
            snapshot.State.Should().Be(TriskoVpnState.Connected));
    }

    [Fact]
    public async Task RefreshDuringConnect_ShouldWaitForCommandAndKeepFinalState()
    {
        var runtime = new FakeVpnRuntime { OperationDelay = TimeSpan.FromMilliseconds(50) };
        var controller = new TriskoVpnController(runtime);

        var connect = controller.ConnectAsync(TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        var refresh = controller.RefreshAsync(TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(connect, refresh);

        results.Should().AllSatisfy(snapshot => snapshot.State.Should().Be(TriskoVpnState.Connected));
        controller.Snapshot.State.Should().Be(TriskoVpnState.Connected);
    }

    [Fact]
    public async Task ConnectAsync_WhenCoreDidNotStart_ShouldReturnFailedSnapshot()
    {
        var runtime = new FakeVpnRuntime { StartCoreOnConnect = false };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.ConnectAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Failed);
        snapshot.IsCoreRunning.Should().BeFalse();
        snapshot.Failure.Should().NotBeNull();
        snapshot.Failure!.Code.Should().Be("coreNotRunning");
        snapshot.Failure.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_WhenRuntimeThrows_ShouldReturnSafeFailure()
    {
        var runtime = new FakeVpnRuntime { ConnectException = new InvalidOperationException("secret detail") };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.ConnectAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Failed);
        snapshot.Failure!.Code.Should().Be("connectFailed");
        snapshot.Failure.Message.Should().NotContain("secret detail");
        snapshot.Failure.Details.Should().Contain("secret detail");
        snapshot.Failure.Details.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task ConnectAsync_WhenRuntimeReportsExpectedFailure_ShouldShowSafeMessage()
    {
        var runtime = new FakeVpnRuntime
        {
            ConnectException = new TriskoVpnOperationException(
                "administratorRequired",
                "Запустите программу от имени администратора.")
        };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.ConnectAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Failed);
        snapshot.Failure!.Code.Should().Be("administratorRequired");
        snapshot.Failure.Message.Should().Be("Запустите программу от имени администратора.");
    }

    [Fact]
    public async Task DisconnectAsync_ShouldStopRuntimeAndPublishDisconnected()
    {
        var runtime = new FakeVpnRuntime { IsCoreRunning = true };
        var controller = new TriskoVpnController(runtime);
        var states = new List<TriskoVpnState>();
        using var subscription = controller.SnapshotChanged.AsObservable()
            .Subscribe(new SnapshotObserver(snapshot => states.Add(snapshot.State)));

        var snapshot = await controller.DisconnectAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Disconnected);
        snapshot.IsCoreRunning.Should().BeFalse();
        runtime.DisconnectCallCount.Should().Be(1);
        states.Should().ContainInOrder(TriskoVpnState.Disconnecting, TriskoVpnState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhenCoreAlreadyStopped_ShouldStillRunCleanup()
    {
        var runtime = new FakeVpnRuntime { IsCoreRunning = false };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.DisconnectAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Disconnected);
        runtime.DisconnectCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_ShouldReplaceStaleStateWithRuntimeState()
    {
        var runtime = new FakeVpnRuntime { IsCoreRunning = true, IsTunEnabled = true };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.RefreshAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Connected);
        snapshot.IsTunEnabled.Should().BeTrue();
        snapshot.CoreType.Should().Be(ECoreType.Xray);
    }

    [Fact]
    public async Task RefreshAsync_CoreWithoutTun_ShouldNotReportVpnConnected()
    {
        var runtime = new FakeVpnRuntime { IsCoreRunning = true, IsTunEnabled = false };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.RefreshAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Disconnected);
        snapshot.IsCoreRunning.Should().BeTrue();
        snapshot.IsTunEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReload_ShouldReportTransitionAndNotStartAnotherConnection(bool coreRunning)
    {
        var runtime = new FakeVpnRuntime { IsCoreRunning = coreRunning, IsTransitioning = true };
        var controller = new TriskoVpnController(runtime);

        var refreshed = await controller.RefreshAsync(TestContext.Current.CancellationToken);
        var connected = await controller.ConnectAsync(TestContext.Current.CancellationToken);

        refreshed.State.Should().Be(TriskoVpnState.Connecting);
        connected.State.Should().Be(TriskoVpnState.Connecting);
        runtime.ConnectCallCount.Should().Be(0);

        runtime.IsTransitioning = false;
        runtime.IsCoreRunning = true;
        var completed = await controller.RefreshAsync(TestContext.Current.CancellationToken);
        completed.State.Should().Be(TriskoVpnState.Connected);
        runtime.ConnectCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_ShouldPreserveRunningProfileDetails()
    {
        var running = new RunningProfileInfo("old-id", "Trisko Trojan TLS", EConfigType.Trojan,
            "vpn.example.test", 443, ECoreType.Xray);
        var runtime = new FakeVpnRuntime { IsCoreRunning = true, RunningProfile = running };
        var controller = new TriskoVpnController(runtime);

        var snapshot = await controller.RefreshAsync(TestContext.Current.CancellationToken);

        snapshot.State.Should().Be(TriskoVpnState.Connected);
        snapshot.RunningProfile.Should().Be(running);
        var option = TriskoVpnProfileOption.From(snapshot.RunningProfile!);
        option.DisplayName.Should().Be("Trisko Trojan TLS");
        option.ProtocolName.Should().Be("Trojan");
        option.EndpointText.Should().Be("vpn.example.test:443");
    }

    private sealed class FakeVpnRuntime : ITriskoVpnRuntime
    {
        private int _connectCallCount;
        private int _disconnectCallCount;

        public bool IsCoreRunning { get; set; }
        public bool IsTransitioning { get; set; }
        public RunningProfileInfo? RunningProfile { get; set; }
        public bool IsTunEnabled { get; set; } = true;
        public bool StartCoreOnConnect { get; set; } = true;
        public Exception? ConnectException { get; set; }
        public TimeSpan OperationDelay { get; set; }
        public int ConnectCallCount => Volatile.Read(ref _connectCallCount);
        public int DisconnectCallCount => Volatile.Read(ref _disconnectCallCount);

        public Task<TriskoVpnRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TriskoVpnRuntimeSnapshot(
                IsCoreRunning,
                IsTunEnabled,
                "corporate-vpn",
                "Corporate VPN",
                ECoreType.Xray)
            {
                IsTransitioning = IsTransitioning,
                RunningProfile = RunningProfile
            });
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCallCount);
            if (OperationDelay > TimeSpan.Zero)
            {
                await Task.Delay(OperationDelay, cancellationToken);
            }

            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            IsCoreRunning = StartCoreOnConnect;
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _disconnectCallCount);
            if (OperationDelay > TimeSpan.Zero)
            {
                await Task.Delay(OperationDelay, cancellationToken);
            }

            IsCoreRunning = false;
        }
    }

    private sealed class SnapshotObserver(Action<TriskoVpnSnapshot> onNext)
        : IObserver<TriskoVpnSnapshot>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnNext(TriskoVpnSnapshot value)
        {
            onNext(value);
        }
    }
}
