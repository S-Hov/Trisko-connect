using ServiceLib.Geksa;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaStartupSettingsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAtLoginUpdatesExistingWindowsSettingAndPersists(bool enabled)
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.GuiItem.AutoRun = !enabled;
        var applied = new List<bool>();
        var saved = false;
        var service = new GeksaStartupSettingsService(config,
            cfg => { applied.Add(cfg.GuiItem.AutoRun); return Task.FromResult(true); },
            cfg => { saved = cfg.GuiItem.AutoRun == enabled; return Task.FromResult(0); });

        await service.SetRunAtLoginAsync(enabled);

        Assert.Equal([enabled], applied);
        Assert.True(saved);
        Assert.Equal(enabled, config.GuiItem.AutoRun);
    }

    [Fact]
    public async Task FailedSaveRestoresPreviousWindowsStartupSetting()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        var applied = new List<bool>();
        var service = new GeksaStartupSettingsService(config,
            cfg => { applied.Add(cfg.GuiItem.AutoRun); return Task.FromResult(true); },
            _ => Task.FromResult(-1));

        await Assert.ThrowsAsync<GeksaConfigurationException>(() => service.SetRunAtLoginAsync(true));

        Assert.False(config.GuiItem.AutoRun);
        Assert.Equal([true, false], applied);
    }

    [Fact]
    public async Task FailedWindowsStartupUpdateDoesNotPersistSuccess()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        int updates = 0, saves = 0;
        var service = new GeksaStartupSettingsService(config,
            _ => Task.FromResult(++updates > 1),
            _ => { saves++; return Task.FromResult(0); });

        await Assert.ThrowsAsync<GeksaConfigurationException>(() => service.SetRunAtLoginAsync(true));

        Assert.False(config.GuiItem.AutoRun);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task AutoConnectPersistsWithoutChangingWindowsStartup()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        var service = new GeksaStartupSettingsService(config,
            _ => throw new InvalidOperationException("Must not touch Windows startup"), _ => Task.FromResult(0));

        await service.SetAutoConnectAsync(true);

        Assert.True(config.UiItem.GeksaAutoConnectVpnOnStartup);
        var restored = JsonSerializer.Deserialize<UIItem>(JsonSerializer.Serialize(config.UiItem))!;
        Assert.True(restored.GeksaAutoConnectVpnOnStartup);
        Assert.False(config.GuiItem.AutoRun);
    }

    [Fact]
    public async Task AutoConnectDefaultsOffAndFailedSaveRestoresPreviousValue()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        Assert.False(config.UiItem.GeksaAutoConnectVpnOnStartup);
        var service = new GeksaStartupSettingsService(config, saveConfig: _ => Task.FromResult(-1));

        await Assert.ThrowsAsync<GeksaConfigurationException>(() => service.SetAutoConnectAsync(true));

        Assert.False(config.UiItem.GeksaAutoConnectVpnOnStartup);
    }
}
