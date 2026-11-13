namespace ServiceLib.Geksa;

public sealed class GeksaStartupSettingsService
{
    private readonly Config _appConfig;
    private readonly Func<Config, Task<bool>> _updateStartup;
    private readonly Func<Config, Task<int>> _saveConfig;

    public GeksaStartupSettingsService(Config appConfig,
        Func<Config, Task<bool>>? updateStartup = null, Func<Config, Task<int>>? saveConfig = null)
    {
        _appConfig = appConfig;
        _updateStartup = updateStartup ?? AutoStartupHandler.UpdateTask;
        _saveConfig = saveConfig ?? ConfigHandler.SaveConfig;
    }

    public async Task SetRunAtLoginAsync(bool enabled)
    {
        var previous = _appConfig.GuiItem.AutoRun;
        _appConfig.GuiItem.AutoRun = enabled;
        try
        {
            if (!await _updateStartup(_appConfig))
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось изменить запуск при входе в систему. Проверьте права доступа."));
            if (await _saveConfig(_appConfig) != 0)
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось сохранить настройку запуска при входе в систему."));
        }
        catch
        {
            _appConfig.GuiItem.AutoRun = previous;
            if (!await _updateStartup(_appConfig))
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось сохранить или восстановить настройку запуска при входе в систему. Проверьте автозапуск Windows."));
            throw;
        }
    }

    public async Task SetAutoConnectAsync(bool enabled)
    {
        var previous = _appConfig.UiItem.GeksaAutoConnectVpnOnStartup;
        _appConfig.UiItem.GeksaAutoConnectVpnOnStartup = enabled;
        try
        {
            if (await _saveConfig(_appConfig) != 0)
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось сохранить настройку автоматического подключения VPN."));
        }
        catch
        {
            _appConfig.UiItem.GeksaAutoConnectVpnOnStartup = previous;
            throw;
        }
    }
}
