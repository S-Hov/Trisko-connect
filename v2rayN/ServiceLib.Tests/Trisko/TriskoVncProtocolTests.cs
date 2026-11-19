using System.Net;
using System.Net.Sockets;
using MarcusW.VncClient;
using MarcusW.VncClient.Protocol.Implementation.Services.Transports;
using MarcusW.VncClient.Protocol.SecurityTypes;
using MarcusW.VncClient.Security;
using MarcusW.VncClient.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoVncProtocolTests
{
    [Fact]
    public async Task Closing_an_idle_server_unblocks_the_reader_and_can_be_repeated()
    {
        await using var server = new IdleRfbServer();
        using var connection = await ConnectAsync(server.Port);
        Assert.Equal(ConnectionState.Connected, connection.ConnectionState);
        await connection.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await connection.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Closed, connection.ConnectionState);
    }

    [Fact]
    public async Task A_remote_disconnect_closes_the_connection_without_crashing_the_host()
    {
        await using var server = new IdleRfbServer();
        using var connection = await ConnectAsync(server.Port);
        server.Disconnect();
        for (int i = 0; i < 50 && connection.ConnectionState != ConnectionState.Closed; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Closed, connection.ConnectionState);
        Assert.NotNull(connection.InterruptionCause);
        await connection.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_empty_first_update_retries_the_full_frame_then_keeps_requesting_updates()
    {
        var requests = new TaskCompletionSource<(byte[] Initial, byte[] Retry, byte[] Next)>();
        await using var server = new IdleRfbServer(async (stream, token) =>
        {
            var encodings = new byte[4];
            await stream.ReadExactlyAsync(encodings, token);
            Assert.Equal(2, encodings[0]); // SetEncodings
            var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(encodings.AsSpan(2));
            await stream.ReadExactlyAsync(new byte[count * 4], token);

            var initial = new byte[10];
            var retry = new byte[10];
            var next = new byte[10];
            await stream.ReadExactlyAsync(initial, token);
            await stream.WriteAsync(new byte[4], token); // FramebufferUpdate with zero rectangles
            await stream.ReadExactlyAsync(retry, token);
            await stream.WriteAsync(new byte[4], token);
            await stream.ReadExactlyAsync(next, token);
            requests.SetResult((initial, retry, next));
        });
        using var connection = await ConnectAsync(server.Port);
        var (initial, retry, next) = await requests.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 3, 0, 0, 0, 0, 0, 0, 2, 0, 2 }, initial);
        Assert.Equal(initial, retry);
        Assert.Equal(new byte[] { 3, 1, 0, 0, 0, 0, 0, 2, 0, 2 }, next);
        await connection.CloseAsync();
    }

    [Fact]
    public async Task Canceling_a_worker_is_normal_shutdown()
    {
        using var worker = new CancelableWorker();
        var failed = false;
        worker.Failed += (_, _) => failed = true;
        worker.Begin();
        await worker.StopAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.False(failed);
    }

    private static Task<RfbConnection> ConnectAsync(int port) => new VncClient(NullLoggerFactory.Instance).ConnectAsync(new ConnectParameters
    {
        TransportParameters = new TcpTransportParameters { Host = "127.0.0.1", Port = port },
        AuthenticationHandler = new TestAuthentication(),
        ConnectTimeout = TimeSpan.FromSeconds(3),
        MaxReconnectAttempts = 0
    }, TestContext.Current.CancellationToken);

    private sealed class TestAuthentication : IAuthenticationHandler
    {
        public Task<T> ProvideAuthenticationInputAsync<T>(RfbConnection connection, ISecurityType securityType,
            IAuthenticationInputRequest<T> request) where T : class, IAuthenticationInput =>
            Task.FromResult((T)(object)new PasswordAuthenticationInput("Test123!"));
    }

    private sealed class CancelableWorker() : BackgroundThread("VNC cancellation regression")
    {
        public void Begin() => Start();
        public Task StopAsync() => StopAndWaitAsync();
        protected override void ThreadWorker(CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Minimal authenticated RFB 3.8 peer that leaves the framebuffer idle.</summary>
    private sealed class IdleRfbServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
        private readonly Task _worker;
        private readonly Func<NetworkStream, CancellationToken, Task>? _afterHandshake;
        private TcpClient? _client;
        public int Port { get; }
        public IdleRfbServer(Func<NetworkStream, CancellationToken, Task>? afterHandshake = null)
        {
            _afterHandshake = afterHandshake;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _worker = ServeAsync();
        }
        private async Task ServeAsync()
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(_timeout.Token);
                var stream = _client.GetStream();
                await stream.WriteAsync("RFB 003.008\n"u8.ToArray(), _timeout.Token);
                await ReadAsync(stream, 12);
                await stream.WriteAsync(new byte[] { 1, 2 }, _timeout.Token);
                await ReadAsync(stream, 1);
                await stream.WriteAsync(new byte[16], _timeout.Token);
                await ReadAsync(stream, 16);
                await stream.WriteAsync(new byte[4], _timeout.Token);
                await ReadAsync(stream, 1);
                // 2x2 framebuffer, standard 32-bit true color, four-byte desktop name.
                await stream.WriteAsync(new byte[] { 0, 2, 0, 2, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0,
                    0, 0, 0, 4, 116, 101, 115, 116 }, _timeout.Token);
                if (_afterHandshake is not null) await _afterHandshake(stream, _timeout.Token);
                var buffer = new byte[1024];
                while (await stream.ReadAsync(buffer, _timeout.Token) > 0) { }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        }
        private async Task ReadAsync(NetworkStream stream, int count) =>
            await stream.ReadExactlyAsync(new byte[count], _timeout.Token);
        public void Disconnect() => _client?.Dispose();
        public async ValueTask DisposeAsync()
        {
            _timeout.Cancel(); _client?.Dispose(); _listener.Stop();
            await _worker;
            _timeout.Dispose();
        }
    }
}
