using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public sealed class WindowsGeksaSmbDriveServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TryRunUserSessionHelper_OrdinaryArguments_ShouldNotInterceptApplicationStart()
    {
        var handled = WindowsGeksaSmbDriveService.TryRunUserSessionHelper(
            ["--ordinary-argument"],
            out var exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
    }

    [Fact]
    public void TryRunUserSessionHelper_StatusRequest_ShouldWriteAtomicResponse()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(Path.GetTempPath(), $"geksa-smb-test-{requestId}.request.json");
        var responsePath = Path.Combine(Path.GetTempPath(), $"geksa-smb-test-{requestId}.response.json");
        try
        {
            var resource = new GeksaResourceProfile
            {
                Id = "smb-helper-test",
                Type = "smb",
                DisplayName = "SMB helper test",
                Host = "smb-helper.invalid",
                Port = 445,
                Path = "share",
                DriveLetter = "Y:"
            };
            var request = new
            {
                operation = "status",
                resource,
                responsePath
            };
            File.WriteAllText(
                requestPath,
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8);

            var handled = WindowsGeksaSmbDriveService.TryRunUserSessionHelper(
                ["--geksa-smb-user-session", requestPath],
                out var exitCode);

            handled.Should().BeTrue();
            exitCode.Should().Be(0);
            File.Exists(responsePath).Should().BeTrue();
            File.Exists(responsePath + ".tmp").Should().BeFalse();

            using var response = JsonDocument.Parse(File.ReadAllText(responsePath, Encoding.UTF8));
            response.RootElement.GetProperty("status").ValueKind.Should().Be(JsonValueKind.Object);
            response.RootElement.GetProperty("errorMessage").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            File.Delete(requestPath);
            File.Delete(responsePath);
            File.Delete(responsePath + ".tmp");
        }
    }
}
