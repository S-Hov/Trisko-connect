namespace ServiceLib.Geksa;

public sealed record GeksaResourceEditRequest(
    GeksaResourceType ResourceType,
    GeksaResourceProfile? Resource = null,
    bool CanDisconnectSmb = false);

public sealed record GeksaResourceEditResult(
    GeksaResourceProfile? Resource = null,
    bool DisconnectSmbRequested = false,
    bool DeleteRequested = false);

public partial class GeksaResourceEditorViewModel : MyReactiveObject
{
    private string _resourceId;

    public GeksaResourceEditorViewModel(GeksaResourceEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResourceType = request.ResourceType;
        var resource = request.Resource;
        _resourceId = resource?.Id ?? $"local-{Guid.NewGuid():N}";
        DisplayName = resource?.DisplayName ?? string.Empty;
        Description = resource?.Description ?? string.Empty;
        Host = resource?.Host ?? string.Empty;
        PortText = (resource?.Port ?? GetDefaultPort(ResourceType)).ToString();
        UserName = resource?.UserName ?? string.Empty;
        Path = resource?.Path ?? string.Empty;
        DriveLetter = resource?.DriveLetter ?? string.Empty;
        RequiresVpn = resource?.RequiresVpn ?? true;
        FullScreen = resource?.FullScreen ?? false;
        MultiMonitor = resource?.MultiMonitor ?? false;
        IsPinned = resource?.IsPinned ?? false;
        AutoLaunchOnVpn = resource?.AutoLaunchOnVpn ?? false;
        IsEditing = resource is not null;
        CanDisconnectSmb = IsEditing && IsSmb && request.CanDisconnectSmb;
    }

    public GeksaResourceType ResourceType { get; }

    public bool IsEditing { get; }

    public bool CanDelete => IsEditing;

    public bool CanDisconnectSmb { get; }

    public string WindowTitle => IsEditing
        ? TriskoLocalization.Format("Изменить {0}-подключение", TypeBadge)
        : TriskoLocalization.Format("Добавить {0}-подключение", TypeBadge);

    public string TypeBadge => ResourceType switch
    {
        GeksaResourceType.Rdp => "RDP",
        GeksaResourceType.Ssh => "SSH",
        GeksaResourceType.Smb => "SMB",
        _ => TriskoLocalization.Text("Подключение")
    };

    public bool IsRdp => ResourceType == GeksaResourceType.Rdp;

    public bool IsSsh => ResourceType == GeksaResourceType.Ssh;

    public bool IsSmb => ResourceType == GeksaResourceType.Smb;

    public bool HasUserName => IsRdp || IsSsh;

    public string UserNameLabel => IsSsh ? TriskoLocalization.Text("Имя пользователя *") : TriskoLocalization.Text("Имя пользователя");

    [Reactive] public partial string DisplayName { get; set; }

    [Reactive] public partial string Description { get; set; }

    [Reactive] public partial string Host { get; set; }

    [Reactive] public partial string PortText { get; set; }

    [Reactive] public partial string UserName { get; set; }

    [Reactive] public partial string Path { get; set; }

    [Reactive] public partial string DriveLetter { get; set; }

    [Reactive] public partial bool RequiresVpn { get; set; }

    [Reactive] public partial bool FullScreen { get; set; }

    [Reactive] public partial bool MultiMonitor { get; set; }

    [Reactive] public partial bool IsPinned { get; set; }

    [Reactive] public partial bool AutoLaunchOnVpn { get; set; }

    [Reactive] public partial string? ValidationMessage { get; set; }

    [Reactive] public partial string? OperationMessage { get; set; }

    public void ApplyProfile(GeksaResourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var importedType = GeksaCorporateConfigurationCodec.ParseResourceType(profile.Type);
        if (importedType != ResourceType)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Файл содержит {0}-подключение, ", importedType.ToString().ToUpperInvariant()) +
                TriskoLocalization.Format("а открыт редактор {0}. Откройте соответствующий раздел и повторите импорт.", TypeBadge));
        }

        if (!IsEditing)
        {
            _resourceId = profile.Id;
        }
        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            DisplayName = profile.DisplayName;
        }
        if (!string.IsNullOrWhiteSpace(profile.Description))
        {
            Description = profile.Description;
        }
        if (!string.IsNullOrWhiteSpace(profile.Host))
        {
            Host = profile.Host;
        }
        if (profile.Port > 0)
        {
            PortText = profile.Port.ToString();
        }
        if (!string.IsNullOrWhiteSpace(profile.UserName))
        {
            UserName = profile.UserName;
        }
        if (!string.IsNullOrWhiteSpace(profile.Path))
        {
            Path = profile.Path;
        }
        if (!string.IsNullOrWhiteSpace(profile.DriveLetter))
        {
            DriveLetter = profile.DriveLetter;
        }
        RequiresVpn = profile.RequiresVpn;
        FullScreen = profile.FullScreen;
        MultiMonitor = profile.MultiMonitor;
        IsPinned = profile.IsPinned;
        AutoLaunchOnVpn = profile.AutoLaunchOnVpn;
        ValidationMessage = null;
        OperationMessage = TriskoLocalization.Format("Данные {0}-подключения загружены из файла. Проверьте их и нажмите «Сохранить».", TypeBadge);
    }

    public bool TryBuild(out GeksaResourceProfile? resource)
    {
        resource = null;
        ValidationMessage = null;
        OperationMessage = null;
        if (!int.TryParse(PortText, out var port))
        {
            ValidationMessage = TriskoLocalization.Text("Порт должен быть целым числом от 1 до 65535.");
            return false;
        }

        var candidate = new GeksaResourceProfile
        {
            Id = _resourceId,
            Type = ResourceType.ToString().ToLowerInvariant(),
            DisplayName = DisplayName.Trim(),
            Description = Description.Trim(),
            Host = Host.Trim(),
            Port = port,
            UserName = UserName.Trim(),
            Path = Path.Trim(),
            DriveLetter = IsSmb && !string.IsNullOrWhiteSpace(DriveLetter)
                ? DriveLetter.Trim().ToUpperInvariant()
                : null,
            RequiresVpn = RequiresVpn,
            FullScreen = IsRdp && FullScreen,
            MultiMonitor = IsRdp && MultiMonitor,
            IsPinned = IsPinned,
            AutoLaunchOnVpn = AutoLaunchOnVpn
        };

        try
        {
            GeksaCorporateConfigurationCodec.ValidateResourceProfile(candidate);
            resource = candidate;
            return true;
        }
        catch (GeksaConfigurationException ex)
        {
            ValidationMessage = ex.UserMessage;
            return false;
        }
    }

    private static int GetDefaultPort(GeksaResourceType resourceType) => resourceType switch
    {
        GeksaResourceType.Rdp => 3389,
        GeksaResourceType.Ssh => 22,
        GeksaResourceType.Smb => 445,
        _ => 0
    };
}
