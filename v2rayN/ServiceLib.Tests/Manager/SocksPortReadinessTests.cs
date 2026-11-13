using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using ServiceLib.Trisko;
using ServiceLib.Services;
using Xunit;

namespace ServiceLib.Tests.Manager;

public class SocksPortReadinessTests
{
    [Fact]
    public async Task WaitAsync_FragmentedSlowReply_IsReady()
    {
        var result = await ProbeAsync(async (stream, token) =>
        {
            await stream.WriteAsync(new byte[] { 0x05 }, token);
            await Task.Delay(150, token);
            await stream.WriteAsync(new byte[] { 0x00 }, token);
        });

        result.Should().Be(SocksPortReadinessResult.Ready);
    }

    [Fact]
    public async Task WaitAsync_SlowReply_IsReady()
    {
        var result = await ProbeAsync(async (stream, token) =>
        {
            await Task.Delay(200, token);
            await ReplyAsync(stream, token);
        });

        result.Should().Be(SocksPortReadinessResult.Ready);
    }

    [Theory]
    [InlineData(0x05, 0xff)]
    [InlineData(0x05, 0x02)]
    [InlineData(0x48, 0x54)]
    public async Task WaitAsync_RejectedGreeting_IsNotReady(byte version, byte method)
    {
        var result = await ProbeAsync(
            (stream, token) => stream.WriteAsync(new byte[] { version, method }, token).AsTask(),
            timeout: TimeSpan.FromMilliseconds(600));

        result.Should().Be(SocksPortReadinessResult.InvalidResponse);
    }

    [Fact]
    public async Task WaitAsync_ListenerInventoryUpdates_Retries()
    {
        var checks = 0;
        var result = await ProbeAsync(ReplyAsync, isOwner: _ => ++checks >= 2);

        result.Should().Be(SocksPortReadinessResult.Ready);
        checks.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task WaitAsync_AnotherProcessOwnsListener_IsNotReady()
    {
        var result = await ProbeAsync(ReplyAsync,
            isOwner: _ => false,
            timeout: TimeSpan.FromMilliseconds(600));

        result.Should().Be(SocksPortReadinessResult.ListenerNotOwned);
    }

    [Fact]
    public async Task WaitAsync_CoreExitsAfterGreeting_IsNotReady()
    {
        var running = 1;
        var result = await ProbeAsync(async (stream, token) =>
        {
            Interlocked.Exchange(ref running, 0);
            await ReplyAsync(stream, token);
        }, isRunning: () => Volatile.Read(ref running) == 1);

        result.Should().Be(SocksPortReadinessResult.ProcessExited);
    }

    [Fact]
    public async Task WaitAsync_UnresponsiveListener_TimesOut()
    {
        var result = await ProbeAsync(
            (_, token) => Task.Delay(Timeout.Infinite, token),
            timeout: TimeSpan.FromMilliseconds(600));

        result.Should().Be(SocksPortReadinessResult.TimedOut);
    }

    private static Task ReplyAsync(NetworkStream stream, CancellationToken token) =>
        stream.WriteAsync(new byte[] { 0x05, 0x00 }, token).AsTask();

    private static async Task<SocksPortReadinessResult> ProbeAsync(
        Func<NetworkStream, CancellationToken, Task> reply,
        Func<bool>? isRunning = null,
        Func<int, bool>? isOwner = null,
        TimeSpan? timeout = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        serverCancellation.CancelAfter(TimeSpan.FromSeconds(10));
        var token = serverCancellation.Token;
        var server = ServeAsync();
        try
        {
            return await SocksPortReadiness.WaitAsync(
                port,
                isRunning ?? (() => true),
                () => isOwner?.Invoke(port)
                      ?? TriskoStartupDiagnosticsService.IsTcpListenerOwnedByProcess(port, Environment.ProcessId),
                timeout ?? TimeSpan.FromSeconds(5));
        }
        finally
        {
            await serverCancellation.CancelAsync();
            await server;
        }

        async Task ServeAsync()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(token);
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    var greeting = new byte[3];
                    await stream.ReadExactlyAsync(greeting, token);
                    greeting.Should().Equal(0x05, 0x01, 0x00);
                    await reply(stream, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }
    }
}
