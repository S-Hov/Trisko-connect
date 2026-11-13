using System.Text.Json.Nodes;
using ServiceLib.Trisko;
using ServiceLib.Models.Entities;
using SQLite;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoDataCompatibilityTests
{
    [Fact]
    public void EarlierSettingsKeepTheirValuesAndUserText()
    {
        var migrated = JsonNode.Parse(TriskoDataCompatibility.NormalizeConfigJson("""
            {"UiItem":{"GeksaThemeMode":1,"GeksaFavoriteVpnIds":["profile-one"]},
             "TunModeItem":{"GeksaManualVpnRoutes":["10.20.0.0/16"]},
             "UserText":"Geksa is part of my resource name"}
            """))!;
        Assert.Equal(1, (int)migrated["UiItem"]!["TriskoThemeMode"]!);
        Assert.Equal("profile-one", (string)migrated["UiItem"]!["TriskoFavoriteVpnIds"]![0]!);
        Assert.Equal("10.20.0.0/16", (string)migrated["TunModeItem"]!["TriskoManualVpnRoutes"]![0]!);
        Assert.Equal("Geksa is part of my resource name", (string)migrated["UserText"]!);
    }

    [Fact]
    public void NewSettingsTakePriorityAndMigrationCanBeRepeated()
    {
        var migrated = TriskoDataCompatibility.NormalizeConfigJson("""{"TriskoThemeMode":2,"GeksaThemeMode":0}""");
        Assert.Equal(2, (int)JsonNode.Parse(migrated)!["TriskoThemeMode"]!);
        Assert.Equal(migrated, TriskoDataCompatibility.NormalizeConfigJson(migrated));
    }

    [Fact]
    public void EarlierResourceFilesAreCopiedWithoutOverwritingNewerData()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trisko-migration-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var old = Path.Combine(folder, "gek-sa-v3-user-resources.json");
            var current = Path.Combine(folder, "trisko-v3-user-resources.json");
            File.WriteAllText(old, "first");
            TriskoDataCompatibility.PrepareFiles(folder);
            Assert.Equal("first", File.ReadAllText(current));
            File.WriteAllText(current, "newer");
            TriskoDataCompatibility.PrepareFiles(folder);
            Assert.Equal("newer", File.ReadAllText(current));
            Assert.Equal("first", File.ReadAllText(old));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void RenamedSubscriptionPropertiesReadExistingTrafficColumns()
    {
        using var database = new SQLiteConnection(":memory:");
        database.Execute("CREATE TABLE SubItem (Id TEXT PRIMARY KEY, GeksaTrafficDownload INTEGER, GeksaTrafficTotal INTEGER)");
        database.Execute("INSERT INTO SubItem VALUES ('existing', 123, 1024)");
        var item = database.Query<SubItem>("SELECT * FROM SubItem").Single();
        Assert.Equal(123, item.TriskoTrafficDownload);
        Assert.Equal(1024, item.TriskoTrafficTotal);
    }
}
