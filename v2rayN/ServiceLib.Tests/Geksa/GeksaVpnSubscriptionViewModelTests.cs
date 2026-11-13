using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaVpnSubscriptionViewModelTests
{
    [Fact]
    public void TryBuild_ValidHttpsSubscription_ShouldReturnTrimmedRequest()
    {
        var viewModel = new GeksaVpnSubscriptionViewModel(
            "  https://sub.geksa.example:2096/path/client-id  ");

        var valid = viewModel.TryBuild(out var request);

        valid.Should().BeTrue();
        request.Should().NotBeNull();
        request!.Url.Should().Be("https://sub.geksa.example:2096/path/client-id");
        request.Name.Should().BeEmpty();
        viewModel.ValidationMessage.Should().BeNull();
    }

    [Fact]
    public void TryBuild_EditContextPreservesNameAndUrl()
    {
        var viewModel = new GeksaVpnSubscriptionViewModel(new GeksaVpnSubscriptionEditContext(
            "https://sub.example.com/personal", "Personal VPN"));
        viewModel.TryBuild(out var request).Should().BeTrue();
        request!.Name.Should().Be("Personal VPN");
        request.Url.Should().Be("https://sub.example.com/personal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://sub.geksa.example/client")]
    [InlineData("vless://server.example:443")]
    [InlineData("https://user:password@sub.geksa.example/client")]
    public void TryBuild_InvalidOrNonHttpsSubscription_ShouldReject(string url)
    {
        var viewModel = new GeksaVpnSubscriptionViewModel(url);

        var valid = viewModel.TryBuild(out var request);

        valid.Should().BeFalse();
        request.Should().BeNull();
        viewModel.ValidationMessage.Should().NotBeNullOrWhiteSpace();
    }
}
