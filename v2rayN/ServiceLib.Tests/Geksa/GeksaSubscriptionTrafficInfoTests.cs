using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaSubscriptionTrafficInfoTests
{
    [Fact]
    public void TryParse_StandardSubscriptionUserinfo_ShouldReadTrafficAndExpiry()
    {
        var parsed = GeksaSubscriptionTrafficInfo.TryParse(
            "upload=1024; download=2048; total=10737418240; expire=1798761600",
            out var info);

        parsed.Should().BeTrue();
        info.Should().NotBeNull();
        info!.Upload.Should().Be(1024);
        info.Download.Should().Be(2048);
        info.Used.Should().Be(3072);
        info.Total.Should().Be(10L * 1024 * 1024 * 1024);
        info.Expire.Should().Be(1798761600);
        info.HasLimit.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("upload=1; download=2")]
    [InlineData("garbage")]
    public void TryParse_InvalidHeader_ShouldReject(string? header)
    {
        GeksaSubscriptionTrafficInfo.TryParse(header, out var info).Should().BeFalse();
        info.Should().BeNull();
    }

    [Fact]
    public void TryParse_ZeroTotal_ShouldRepresentUnlimitedSubscription()
    {
        GeksaSubscriptionTrafficInfo.TryParse(
            "upload=0; download=4096; total=0; expire=0",
            out var info).Should().BeTrue();

        info!.HasLimit.Should().BeFalse();
        GeksaSubscriptionTrafficInfo.FormatBytes(info.Used).Should().Be("4 КБ");
    }

    [Fact]
    public void UsagePresentation_LimitedSubscription_ShouldExposeProgressAndRemainingTraffic()
    {
        SubItem[] subscriptions =
        [
            new()
            {
                GeksaTrafficDownload = 25L * 1024 * 1024 * 1024,
                GeksaTrafficTotal = 100L * 1024 * 1024 * 1024,
                GeksaTrafficUpdatedAt = 1
            }
        ];

        var presentation = GeksaSubscriptionUsagePresentation.Create(subscriptions);

        presentation.HasMetadata.Should().BeTrue();
        presentation.HasLimit.Should().BeTrue();
        presentation.Percentage.Should().Be(25);
        presentation.PrimaryText.Should().Be("Использовано 25 ГБ из 100 ГБ");
        presentation.SecondaryText.Should().Contain("Осталось 75 ГБ");
        presentation.ProgressColor.Should().Be("#3FD69A");
    }

    [Fact]
    public void UsagePresentation_HighUsage_ShouldClampProgressAndUseWarningColor()
    {
        SubItem[] subscriptions =
        [
            new()
            {
                GeksaTrafficDownload = 110,
                GeksaTrafficTotal = 100,
                GeksaTrafficUpdatedAt = 1
            }
        ];

        var presentation = GeksaSubscriptionUsagePresentation.Create(subscriptions);

        presentation.Percentage.Should().Be(100);
        presentation.SecondaryText.Should().Contain("Осталось 0 Б");
        presentation.ProgressColor.Should().Be("#F0717E");
    }
}
