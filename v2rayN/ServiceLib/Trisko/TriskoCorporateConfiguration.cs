namespace ServiceLib.Trisko;

public sealed class TriskoCorporateConfiguration
{
    public int SchemaVersion { get; init; }

    public string ConfigurationVersion { get; init; } = string.Empty;

    public long Revision { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TriskoVpnProfileConfiguration? Vpn { get; init; }

    public List<TriskoResourceProfile> Resources { get; init; } = [];
}

public sealed class TriskoVpnProfileConfiguration
{
    public string DisplayName { get; init; } = string.Empty;

    public string ShareUri { get; init; } = string.Empty;

    public bool EnableTun { get; init; } = true;
}

public sealed class TriskoResourceProfile
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DriveLetter { get; init; }

    public bool RequiresVpn { get; init; } = true;

    public bool FullScreen { get; init; }

    public bool MultiMonitor { get; init; }

    public bool IsPinned { get; init; }

    public bool AutoLaunchOnVpn { get; init; }
}

public enum TriskoResourceType
{
    Rdp,
    Ssh,
    Smb
}

public sealed record TriskoActiveConfiguration(
    int SchemaVersion,
    string ConfigurationVersion,
    long Revision,
    string SourceHash,
    string ManagedProfileId,
    string ManagedProfileName,
    IReadOnlyList<TriskoResourceProfile> Resources)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TriskoVpnProfileConfiguration? Vpn { get; init; }
}

public sealed record TriskoConfigurationImportResult(
    TriskoActiveConfiguration Configuration,
    bool Changed);

public sealed record TriskoConfigurationImportDecision(
    bool Confirmed,
    bool KeepLocalChanges);

public sealed record TriskoConfigurationImportPreview
{
    public required string FileName { get; init; }

    public required string ConfigurationVersion { get; init; }

    public required long Revision { get; init; }

    public long? CurrentRevision { get; init; }

    public required int RdpCount { get; init; }

    public required int SshCount { get; init; }

    public required int SmbCount { get; init; }

    public required int AddedCount { get; init; }

    public required int UpdatedCount { get; init; }

    public required int RemovedCount { get; init; }

    public int LocalOverrideCount { get; init; }

    public int LocallyHiddenCount { get; init; }

    public bool IsAlreadyInstalled { get; init; }

    public bool HasLocalChanges => LocalOverrideCount > 0 || LocallyHiddenCount > 0;

    public string RevisionText => CurrentRevision is null
        ? TriskoLocalization.Format("Ревизия {0} · комплект устанавливается впервые", Revision)
        : TriskoLocalization.Format("Ревизия файла: {0} · установлена: {1}", Revision, CurrentRevision);

    public string ResourceCountText =>
        $"RDP: {RdpCount}  ·  SSH: {SshCount}  ·  SMB: {SmbCount}";

    public string ChangeCountText => IsAlreadyInstalled
        ? TriskoLocalization.Text("Содержимое совпадает с установленным комплектом.")
        : TriskoLocalization.Format("Будет добавлено: {0}  ·  изменено: {1}  ·  удалено: {2}", AddedCount, UpdatedCount, RemovedCount);

    public string LocalChangeText => HasLocalChanges
        ? TriskoLocalization.Format("Локальные изменения: {0}; скрытые корпоративные ресурсы: {1}.", LocalOverrideCount, LocallyHiddenCount)
        : TriskoLocalization.Text("Локальных изменений корпоративных ресурсов нет.");

    internal TriskoCorporateConfiguration Candidate { get; init; } = null!;

    internal IReadOnlySet<string> ManagedResourceIds { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record TriskoUserResourceState(
    IReadOnlyList<TriskoResourceProfile> Resources,
    IReadOnlySet<string> DeletedManagedResourceIds);
