using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoAnyDeskServiceTests
{


    [Fact]
    public void ParseRoster_reads_recent_and_favorite_connections_without_secrets()
    {
        const string text = "ad.roster.items=123456789,123456789,Бухгалтерия,fav;office@example,office@example,,\n" +
                            "ad.roster.favorites=office@example";

        var items = TriskoAnyDeskConfigurationParser.ParseRoster(text);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.True(item.IsFavorite));
        Assert.Equal("Бухгалтерия", items.Single(item => item.Address == "123456789").DisplayName);
        Assert.Equal("office@example", items.Single(item => item.Address == "office@example").DisplayName);
    }

    [Theory]
    [InlineData("123 456 789", true)]
    [InlineData("office@example", true)]
    [InlineData("--get-status", false)]
    [InlineData("bad\nvalue", false)]
    public void IsSafeAddress_rejects_command_like_or_control_input(string value, bool expected)
    {
        Assert.Equal(expected, TriskoAnyDeskConfigurationParser.IsSafeAddress(value));
    }
}
