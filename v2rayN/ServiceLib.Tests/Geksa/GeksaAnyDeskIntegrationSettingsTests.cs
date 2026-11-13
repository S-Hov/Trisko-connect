using AwesomeAssertions;
using ServiceLib.Geksa;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaAnyDeskIntegrationSettingsTests
{
    [Fact]
    public void Inspect_WhenExecutableDoesNotExist_ReturnsNotReady()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-anydesk-{Guid.NewGuid():N}.exe");

        var result = GeksaAnyDeskClientLocator.Inspect(path);

        result.Path.Should().Be(path);
        result.Exists.Should().BeFalse();
        result.IsSupportedVersion.Should().BeFalse();
        result.StatusText.Should().Contain("не найден");
    }

    [Theory]
    [InlineData("6.1.0", true)]
    [InlineData("6.1.0.0", true)]
    [InlineData("ad 6.1.0", true)]
    [InlineData("ad 7.0.0", false)]
    [InlineData("16.1.0", false)]
    [InlineData("6.1.01-invalid", false)]
    [InlineData("6.0.8", false)]
    [InlineData("7.0.0", false)]
    [InlineData(null, false)]
    public void IsSupportedVersion_AcceptsOnly610(string? version, bool expected)
    {
        GeksaAnyDeskClientLocator.IsSupportedVersion(version).Should().Be(expected);
    }

    [Fact]
    public void UiSettings_DefaultToDirectStrategy()
    {
        var settings = new UIItem();

        settings.GeksaAnyDeskStrategy.Should().Be(GeksaAnyDeskStrategy.Direct);
        settings.GeksaAnyDeskViewMode.Should().Be(GeksaAnyDeskViewMode.Integration);
        settings.GeksaAnyDeskExecutablePath.Should().BeNull();
        settings.GeksaAnyDeskIntegrationEnabled.Should().BeTrue();
        settings.GeksaAutoStartAnyDeskOnLaunch.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void SupportedInstalledOrRunningClientDoesNotRequirePortableChecksum(bool installed, bool running, bool ready)
    {
        var info = new GeksaAnyDeskClientInfo("AnyDesk.exe", true, "6.1.0.0", true, null, false, installed, running);
        Assert.Equal(ready, info.IsReady);
        Assert.False((info with { IsSupportedVersion = false }).IsReady);
    }

    [Fact]
    public void EnvironmentInspection_CombinesInstalledVersionAndExternalProcessWarnings()
    {
        var inspection = new GeksaAnyDeskEnvironmentInspection(
            ["8.0.0 (C:\\Program Files\\AnyDesk\\AnyDesk.exe)"],
            ["PID 42 (C:\\Tools\\AnyDesk.exe)"]);

        inspection.HasWarnings.Should().BeTrue();
        inspection.WarningText.Should().Contain("отличная от 6.1.0");
        inspection.WarningText.Should().Contain("не был запущен Trisko connect");
    }

}
