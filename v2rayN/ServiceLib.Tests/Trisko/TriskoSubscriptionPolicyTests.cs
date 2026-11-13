using ServiceLib.Trisko;
using ServiceLib.Models.Entities;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoSubscriptionPolicyTests
{
    private static readonly SubItem First = new() { Id = "one", Url = "https://sub.example.com/TokenA", Remarks = "First" };
    private static readonly SubItem Second = new() { Id = "two", Url = "https://sub.example.com/TokenB", Remarks = "Second" };

    [Fact]
    public void AThirdSubscriptionNeverReplacesTheFirstOne()
    {
        var result = TriskoSubscriptionPolicy.Resolve([First, Second], new("https://sub.example.com/TokenC", "Third"));
        Assert.Empty(result.Id);
        Assert.Equal("Third", result.Remarks);
        Assert.Equal("https://sub.example.com/TokenA", First.Url);
        Assert.Equal("https://sub.example.com/TokenB", Second.Url);
    }

    [Fact]
    public void ReimportingTheSameUrlReusesItsOwnRecord() =>
        Assert.Same(Second, TriskoSubscriptionPolicy.Resolve([First, Second], new(Second.Url)));

    [Fact]
    public void TokensDifferingOnlyByCaseRemainIndependent() =>
        Assert.Empty(TriskoSubscriptionPolicy.Resolve([First], new("https://sub.example.com/tokena")).Id);

    [Fact]
    public void EditingAnAddressKeepsTheSubscriptionIdentity() =>
        Assert.Same(Second, TriskoSubscriptionPolicy.Resolve([First, Second], new("https://new.example.com/sub"), "two"));

    [Fact]
    public void EditingCannotCreateDuplicateUrls() =>
        Assert.Throws<TriskoConfigurationException>(() => TriskoSubscriptionPolicy.Resolve([First, Second], new(First.Url), "two"));

    [Fact]
    public void MissingEditTargetCannotOverwriteAnotherSubscription() =>
        Assert.Throws<TriskoConfigurationException>(() => TriskoSubscriptionPolicy.Resolve([First], new(First.Url), "missing"));
}
