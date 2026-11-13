using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaVpnRouteParserTests
{
    [Theory]
    [InlineData("192.168.3.222", "192.168.3.222/32")]
    [InlineData(" 10.20.30.40 ", "10.20.30.40/32")]
    [InlineData("10.20.4.99/16", "10.20.0.0/16")]
    public void TryNormalize_ShouldAcceptIpv4AndCanonicalizeCidr(
        string input,
        string expected)
    {
        var success = GeksaVpnRouteParser.TryNormalize(
            input,
            out var normalized,
            out var error);

        success.Should().BeTrue(error);
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("server.company.local")]
    [InlineData("300.1.1.1")]
    [InlineData("0.0.0.0/0")]
    [InlineData("2001:db8::1")]
    public void TryNormalize_ShouldRejectUnsafeOrUnsupportedValues(string input)
    {
        var success = GeksaVpnRouteParser.TryNormalize(
            input,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NormalizeMany_ShouldDeduplicateSortAndDropInvalidRoutes()
    {
        var routes = GeksaVpnRouteParser.NormalizeMany([
            "192.168.3.222",
            "bad",
            "10.0.0.1/24",
            "192.168.3.222/32",
        ]);

        routes.Should().Equal("10.0.0.0/24", "192.168.3.222/32");
    }
}
