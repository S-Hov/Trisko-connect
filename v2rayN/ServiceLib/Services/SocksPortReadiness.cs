namespace ServiceLib.Services;

internal enum SocksPortReadinessResult
{
    Ready,
    ProcessExited,
    TimedOut,
    InvalidResponse,
    ListenerNotOwned,
}

internal static class SocksPortReadiness
{
    internal static async Task<SocksPortReadinessResult> WaitAsync(
        int port,
        Func<bool> isProcessRunning,
        Func<bool> isListenerOwned,
        TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        var lastResult = SocksPortReadinessResult.TimedOut;
        ReadOnlyMemory<byte> greeting = new byte[] { 0x05, 0x01, 0x00 };
        var response = new byte[2];

        while (!deadline.IsCancellationRequested)
        {
            if (!isProcessRunning())
            {
                return SocksPortReadinessResult.ProcessExited;
            }

            using var tcp = new TcpClient();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            attempt.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await tcp.ConnectAsync(Global.Loopback, port, attempt.Token);
                var stream = tcp.GetStream();
                await stream.WriteAsync(greeting, attempt.Token);
                // TCP may split even this two-byte reply across multiple reads.
                await stream.ReadExactlyAsync(response, attempt.Token);
                if (response[0] != 0x05 || response[1] != 0x00)
                {
                    lastResult = SocksPortReadinessResult.InvalidResponse;
                }
                else
                {
                    // Allow bind failures to surface before accepting another proxy's reply.
                    await Task.Delay(100, deadline.Token);
                    if (!isProcessRunning())
                    {
                        return SocksPortReadinessResult.ProcessExited;
                    }
                    if (isListenerOwned())
                    {
                        return SocksPortReadinessResult.Ready;
                    }
                    // The OS listener inventory can lag behind process startup.
                    lastResult = SocksPortReadinessResult.ListenerNotOwned;
                }
            }
            catch (OperationCanceledException)
            {
                // Retry a slow attempt while the overall startup deadline remains.
            }
            catch (SocketException)
            {
                // The core may still be loading its configuration.
            }
            catch (IOException)
            {
                // A connection closed before a complete SOCKS reply is not ready.
            }

            try
            {
                await Task.Delay(100, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return isProcessRunning() ? lastResult : SocksPortReadinessResult.ProcessExited;
    }
}
