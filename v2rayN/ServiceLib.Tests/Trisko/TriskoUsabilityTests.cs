using ServiceLib.Trisko;
using ReactiveUI.Primitives.Signals;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoUsabilityTests
{
    [Fact]
    public void AnyDeskPreferencesOverrideNamesFavoritesAndPinsWithoutChangingRoster()
    {
        var original = new TriskoAnyDeskConnection("123456789", "Рабочий ПК", true);
        var result = TriskoAnyDeskPersonalization.Apply([original], new Dictionary<string, TriskoAnyDeskPreference>
        {
            [original.Address] = new("Мой компьютер", false, true)
        });
        var item = Assert.Single(result);
        Assert.Equal("Мой компьютер", item.DisplayName);
        Assert.False(item.IsFavorite);
        Assert.True(item.IsPinned);
        Assert.True(original.IsFavorite);
        Assert.Equal("Рабочий ПК", original.DisplayName);
    }

    [Fact]
    public void AnyDeskPinnedEntriesGoFirstButFavoritesDoNotChangeOrder()
    {
        var roster = new[] { new TriskoAnyDeskConnection("123456789", "Альфа", false), new TriskoAnyDeskConnection("987654321", "Бета", false) };
        var preferences = new Dictionary<string, TriskoAnyDeskPreference> { ["987654321"] = new(Favorite: true) };
        Assert.Equal("Альфа", TriskoAnyDeskPersonalization.Apply(roster, preferences)[0].DisplayName);
        preferences["987654321"] = new(Pinned: true);
        Assert.Equal("Бета", TriskoAnyDeskPersonalization.Apply(roster, preferences)[0].DisplayName);
    }

    [Fact]
    public void AnyDeskEmptyNameResetsToNativeNameAndSavedEntriesSurviveRosterLoss()
    {
        var preferences = new Dictionary<string, TriskoAnyDeskPreference> { ["123456789"] = new(" ", true, true) };
        Assert.Equal("Native", Assert.Single(TriskoAnyDeskPersonalization.Apply([new("123456789", "Native", false)], preferences)).DisplayName);
        Assert.Equal("123456789", Assert.Single(TriskoAnyDeskPersonalization.Apply([], preferences)).Address);
    }

    [Theory]
    [InlineData("москва ssh", true)]
    [InlineData("SSH МОСКВА", true)]
    [InlineData("192.168", true)]
    [InlineData("москва smb", false)]
    [InlineData(" ", true)]
    public void SearchCombinesNameAddressAndSection(string query, bool expected) =>
        Assert.Equal(expected, TriskoSearchCatalog.Matches(query, "Сервер Москва", "Подключения → SSH", "192.168.1.5:22"));

    [Fact]
    public void SearchCatalogCoversEverySettingsSubpageAndHasIcons()
    {
        foreach (var page in Enum.GetNames<TriskoSettingsSubPage>())
            Assert.Contains(TriskoSearchCatalog.Pages, entry => entry.Target == "settings:" + page);
        Assert.All(TriskoSearchCatalog.Pages, entry => Assert.EndsWith(".png", TriskoSearchCatalog.IconPath(entry.Icon)));
    }

    [Fact]
    public void PersonalizationSettingsSurviveSerialization()
    {
        var settings = new UIItem
        {
            TriskoFavoriteResourceIds = ["ssh-id"],
            TriskoFavoriteVpnIds = ["vpn-id"],
            TriskoAnyDeskPreferences = new() { ["123456789"] = new("Мой ПК", false, true) }
        };
        var restored = JsonSerializer.Deserialize<UIItem>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.TriskoFavoriteResourceIds, restored.TriskoFavoriteResourceIds);
        Assert.Equal(settings.TriskoFavoriteVpnIds, restored.TriskoFavoriteVpnIds);
        Assert.Equal(settings.TriskoAnyDeskPreferences["123456789"], restored.TriskoAnyDeskPreferences["123456789"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResourceFavoriteChangesOnlyWhenSaveSucceeds(bool saveSucceeded)
    {
        var opened = false;
        var resource = new TriskoResourceItemViewModel(new TriskoResourceProfile { Id = "test", Type = "ssh", IsPinned = true },
            _ => { opened = true; return Task.CompletedTask; }, toggleFavorite: _ => Task.FromResult(saveSucceeded));
        await resource.ToggleFavoriteCommand.Execute();
        Assert.Equal(saveSucceeded, resource.IsFavorite);
        Assert.True(resource.IsPinned);
        Assert.False(opened);
    }
}
