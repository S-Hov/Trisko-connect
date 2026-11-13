using AwesomeAssertions;
using ServiceLib.Services;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoTestCoreCleanupTests
{
    [Fact]
    public async Task StopAndWaitForExit_DoesNotCompleteWithOwnedProcessStillRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = new ProcessService(powershell,
            "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"",
            Path.GetTempPath(), false, false, null, null);
        await process.StartAsync();
        process.HasExited.Should().BeFalse();

        await process.StopAndWaitForExitAsync();
        process.HasExited.Should().BeTrue();
        await process.StopAndWaitForExitAsync();
        process.HasExited.Should().BeTrue();
    }
}
