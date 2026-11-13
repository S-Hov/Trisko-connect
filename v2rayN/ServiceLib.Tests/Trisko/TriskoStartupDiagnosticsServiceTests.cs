using AwesomeAssertions;
using ServiceLib.Trisko;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoStartupDiagnosticsServiceTests
{
    [Fact]
    public async Task InspectAndRepairAsync_PortConflict_ShouldMoveOnlyTriskoPortRange()
    {
        var config = CreateConfig(10808);
        var saveCount = 0;
        var service = CreateService(
            portOwners:
            [
                Owner(10808, 3608, "incy-desktop"),
                Owner(10809, 3608, "incy-desktop")
            ],
            configSaver: _ =>
            {
                saveCount++;
                return Task.FromResult(0);
            });

        var report = await service.InspectAndRepairAsync(
            config,
            TestContext.Current.CancellationToken);

        report.HasErrors.Should().BeFalse();
        report.HasAutomaticFixes.Should().BeTrue();
        report.EffectivePort.Should().Be(10840);
        report.DisplayMessage.Should().Contain("incy-desktop");
        config.Inbound[0].LocalPort.Should().Be(10840);
        saveCount.Should().Be(1);
    }

    [Fact]
    public async Task InspectAndRepairAsync_SaveFailure_ShouldRestoreConfiguredPort()
    {
        var config = CreateConfig(10808);
        var service = CreateService(
            portOwners: [Owner(10808, 10, "other-proxy")],
            configSaver: _ => Task.FromResult(-1));

        var report = await service.InspectAndRepairAsync(
            config,
            TestContext.Current.CancellationToken);

        report.HasErrors.Should().BeTrue();
        report.EffectivePort.Should().Be(10808);
        config.Inbound[0].LocalPort.Should().Be(10808);
        report.Issues.Should().Contain(issue => issue.Code == "localPortSaveFailed");
    }

    [Fact]
    public async Task InspectAndRepairAsync_NoAdministrator_ShouldReportBlockingError()
    {
        var config = CreateConfig(10808);
        var service = CreateService(administrator: false);

        var report = await service.InspectAndRepairAsync(
            config,
            TestContext.Current.CancellationToken);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(issue => issue.Code == "administratorRequired");
    }

    [Fact]
    public async Task InspectAndRepairAsync_ExternalProcesses_ShouldReportButNotStopThem()
    {
        var config = CreateConfig(10808);
        var externalProcess = new TriskoProcessObservation(
            "xray",
            501,
            IsLikelyService: true,
            "xray (PID 501, системная служба)");
        var service = CreateService(processes: [externalProcess]);

        var report = await service.InspectAndRepairAsync(
            config,
            TestContext.Current.CancellationToken);

        report.HasErrors.Should().BeFalse();
        report.Issues.Should().Contain(issue =>
            issue.Code == "externalVpnCoreDetected"
            && issue.Message.Contains("системная служба", StringComparison.Ordinal));
    }

    [Fact]
    public void FindAvailableBasePort_ShouldCheckTheWholeReservedRange()
    {
        var occupiedPorts = new[]
        {
            10808,
            10840 + 21,
            10872 + 4
        };

        var result = TriskoStartupDiagnosticsService.FindAvailableBasePort(10808, occupiedPorts);

        result.Should().Be(10904);
    }

    private static TriskoStartupDiagnosticsService CreateService(
        IReadOnlyList<TriskoPortOwner>? portOwners = null,
        IReadOnlyList<TriskoProcessObservation>? processes = null,
        bool administrator = true,
        Func<Config, Task<int>>? configSaver = null)
    {
        return new TriskoStartupDiagnosticsService(
            () => portOwners ?? [],
            () => processes ?? [],
            () => administrator,
            configSaver ?? (_ => Task.FromResult(0)));
    }

    private static Config CreateConfig(int localPort)
    {
        return new Config
        {
            Inbound =
            [
                new InItem
                {
                    Protocol = nameof(EInboundProtocol.socks),
                    LocalPort = localPort,
                    UdpEnabled = true,
                    SniffingEnabled = true,
                }
            ]
        };
    }

    private static TriskoPortOwner Owner(int port, int processId, string processName)
    {
        return new TriskoPortOwner(
            "TCP",
            port,
            processId,
            processName,
            IsLikelyService: false,
            $"{processName} (PID {processId})");
    }
}
