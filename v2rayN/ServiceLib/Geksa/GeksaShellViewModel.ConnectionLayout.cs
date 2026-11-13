using ServiceLib.Handler;

namespace ServiceLib.Geksa;

public partial class GeksaShellViewModel
{
    private bool _showConnectionStatus = true;
    private bool _showConnectionResources = true;
    private bool _showConnectionConfiguration = true;
    private bool _showConnectionLogs = true;

    public bool ShowConnectionStatus
    {
        get => _showConnectionStatus;
        set => SetConnectionBlockVisibility(ref _showConnectionStatus, value, nameof(ShowConnectionStatus));
    }

    public bool ShowConnectionResources
    {
        get => _showConnectionResources;
        set => SetConnectionBlockVisibility(ref _showConnectionResources, value, nameof(ShowConnectionResources));
    }

    public bool ShowConnectionConfiguration
    {
        get => _showConnectionConfiguration;
        set => SetConnectionBlockVisibility(ref _showConnectionConfiguration, value, nameof(ShowConnectionConfiguration));
    }

    public bool ShowConnectionLogs
    {
        get => _showConnectionLogs;
        set => SetConnectionBlockVisibility(ref _showConnectionLogs, value, nameof(ShowConnectionLogs));
    }

    public bool HasConnectionSideBlocks => ShowConnectionResources
        || ShowConnectionConfiguration || ShowConnectionLogs;

    private void InitializeConnectionLayout()
    {
        var ui = _appConfig.UiItem;
        _showConnectionStatus = ui.GeksaShowConnectionStatus;
        _showConnectionResources = ui.GeksaShowConnectionResources;
        _showConnectionConfiguration = ui.GeksaShowConnectionConfiguration;
        _showConnectionLogs = ui.GeksaShowConnectionLogs;
    }

    private void SetConnectionBlockVisibility(ref bool field, bool value, string propertyName)
    {
        if (field == value) return;
        this.RaiseAndSetIfChanged(ref field, value, propertyName);
        this.RaisePropertyChanged(nameof(HasConnectionSideBlocks));
        if (_appConfig.UiItem is not { } ui) return;
        ui.GeksaShowConnectionStatus = ShowConnectionStatus;
        ui.GeksaShowConnectionResources = ShowConnectionResources;
        ui.GeksaShowConnectionConfiguration = ShowConnectionConfiguration;
        ui.GeksaShowConnectionLogs = ShowConnectionLogs;
        _ = ConfigHandler.SaveConfig(_appConfig);
    }
}
