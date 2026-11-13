using ServiceLib.Geksa;
using ReactiveUI.Primitives.Signals;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaUsabilityTests
{
    [Fact]
    public void AnyDeskPreferencesOverrideNamesFavoritesAndPinsWithoutChangingRoster()
    {
        var original = new GeksaAnyDeskConnection("123456789", "Рабочий ПК", true);
        var result = GeksaAnyDeskPersonalization.Apply([original], new Dictionary<string, GeksaAnyDeskPreference>
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
        var roster = new[] { new GeksaAnyDeskConnection("123456789", "Альфа", false), new GeksaAnyDeskConnection("987654321", "Бета", false) };
        var preferences = new Dictionary<string, GeksaAnyDeskPreference> { ["987654321"] = new(Favorite: true) };
        Assert.Equal("Альфа", GeksaAnyDeskPersonalization.Apply(roster, preferences)[0].DisplayName);
        preferences["987654321"] = new(Pinned: true);
        Assert.Equal("Бета", GeksaAnyDeskPersonalization.Apply(roster, preferences)[0].DisplayName);
    }

    [Fact]
    public void AnyDeskEmptyNameResetsToNativeNameAndSavedEntriesSurviveRosterLoss()
    {
        var preferences = new Dictionary<string, GeksaAnyDeskPreference> { ["123456789"] = new(" ", true, true) };
        Assert.Equal("Native", Assert.Single(GeksaAnyDeskPersonalization.Apply([new("123456789", "Native", false)], preferences)).DisplayName);
        Assert.Equal("123456789", Assert.Single(GeksaAnyDeskPersonalization.Apply([], preferences)).Address);
    }

    [Theory]
    [InlineData("москва ssh", true)]
    [InlineData("SSH МОСКВА", true)]
    [InlineData("192.168", true)]
    [InlineData("москва smb", false)]
    [InlineData(" ", true)]
    public void SearchCombinesNameAddressAndSection(string query, bool expected) =>
        Assert.Equal(expected, GeksaSearchCatalog.Matches(query, "Сервер Москва", "Подключения → SSH", "192.168.1.5:22"));

    [Fact]
    public void SearchCatalogCoversEverySettingsSubpageAndHasIcons()
    {
        foreach (var page in Enum.GetNames<GeksaSettingsSubPage>())
            Assert.Contains(GeksaSearchCatalog.Pages, entry => entry.Target == "settings:" + page);
        Assert.All(GeksaSearchCatalog.Pages, entry => Assert.EndsWith(".png", GeksaSearchCatalog.IconPath(entry.Icon)));
    }

    [Fact]
    public void PersonalizationSettingsSurviveSerialization()
    {
        var settings = new UIItem
        {
            GeksaFavoriteResourceIds = ["ssh-id"],
            GeksaFavoriteVpnIds = ["vpn-id"],
            GeksaAnyDeskPreferences = new() { ["123456789"] = new("Мой ПК", false, true) }
        };
        var restored = JsonSerializer.Deserialize<UIItem>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.GeksaFavoriteResourceIds, restored.GeksaFavoriteResourceIds);
        Assert.Equal(settings.GeksaFavoriteVpnIds, restored.GeksaFavoriteVpnIds);
        Assert.Equal(settings.GeksaAnyDeskPreferences["123456789"], restored.GeksaAnyDeskPreferences["123456789"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResourceFavoriteChangesOnlyWhenSaveSucceeds(bool saveSucceeded)
    {
        var opened = false;
        var resource = new GeksaResourceItemViewModel(new GeksaResourceProfile { Id = "test", Type = "ssh", IsPinned = true },
            _ => { opened = true; return Task.CompletedTask; }, toggleFavorite: _ => Task.FromResult(saveSucceeded));
        await resource.ToggleFavoriteCommand.Execute();
        Assert.Equal(saveSucceeded, resource.IsFavorite);
        Assert.True(resource.IsPinned);
        Assert.False(opened);
    }
}
