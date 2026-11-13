using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoVpnSubscriptionViewModelTests
{
    [Fact]
    public void TryBuild_ValidHttpsSubscription_ShouldReturnTrimmedRequest()
    {
        var viewModel = new TriskoVpnSubscriptionViewModel(
            "  https://sub.trisko.example:2096/path/client-id  ");

        var valid = viewModel.TryBuild(out var request);

        valid.Should().BeTrue();
        request.Should().NotBeNull();
        request!.Url.Should().Be("https://sub.trisko.example:2096/path/client-id");
        request.Name.Should().BeEmpty();
        viewModel.ValidationMessage.Should().BeNull();
    }

    [Fact]
    public void TryBuild_EditContextPreservesNameAndUrl()
    {
        var viewModel = new TriskoVpnSubscriptionViewModel(new TriskoVpnSubscriptionEditContext(
            "https://sub.example.com/personal", "Personal VPN"));
        viewModel.TryBuild(out var request).Should().BeTrue();
        request!.Name.Should().Be("Personal VPN");
        request.Url.Should().Be("https://sub.example.com/personal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://sub.trisko.example/client")]
    [InlineData("vless://server.example:443")]
    [InlineData("https://user:password@sub.trisko.example/client")]
    public void TryBuild_InvalidOrNonHttpsSubscription_ShouldReject(string url)
    {
        var viewModel = new TriskoVpnSubscriptionViewModel(url);

        var valid = viewModel.TryBuild(out var request);

        valid.Should().BeFalse();
        request.Should().BeNull();
        viewModel.ValidationMessage.Should().NotBeNullOrWhiteSpace();
    }
}
