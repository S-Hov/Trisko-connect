namespace ServiceLib.Geksa;

public sealed class GeksaCorporateConfigurationService
{
    internal const string ManagedProfileId = "gek-sa-corporate-v3";
    internal const string ActiveConfigurationFileName = "gek-sa-v3-active.json";

    private static readonly JsonSerializerOptions StoreOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public GeksaCorporateConfigurationService(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
    }

    public EventChannel<GeksaActiveConfiguration?> ConfigurationChanged { get; } = new();

    public async Task<GeksaActiveConfiguration?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var path = GetActiveConfigurationPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > GeksaCorporateConfigurationCodec.MaxConfigurationBytes)
            {
                throw new InvalidDataException("Invalid active configuration size.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var active = await JsonSerializer.DeserializeAsync<GeksaActiveConfiguration>(
                stream,
                StoreOptions,
                cancellationToken);
            if (active is null ||
                active.SchemaVersion != GeksaCorporateConfigurationCodec.CurrentSchemaVersion ||
                active.Revision <= 0 ||
                active.SourceHash.Length != 64 ||
                active.Resources.Count > 200)
            {
                throw new InvalidDataException("Invalid active configuration content.");
            }

            foreach (var resource in active.Resources)
            {
                GeksaCorporateConfigurationCodec.ValidateResourceProfile(resource);
            }

            return active;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaCorporateConfigurationService), ex);
            return null;
        }
    }

    public async Task<GeksaConfigurationImportResult> ImportFileAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var preview = await PrepareImportFileAsync(sourcePath, null, cancellationToken);
        return await ImportAsync(preview.Candidate, cancellationToken);
    }

    public async Task<GeksaConfigurationImportPreview> PrepareImportFileAsync(
        string sourcePath,
        GeksaUserResourceState? userState = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл корпоративной конфигурации не выбран."));
        }

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Выбранный файл корпоративной конфигурации не найден."));
        }

        var file = new FileInfo(fullPath);
        if (file.Length is <= 0 or > GeksaCorporateConfigurationCodec.MaxConfigurationBytes)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Размер файла корпоративной конфигурации недопустим."));
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await GeksaConfigurationFileIO.ReadLimitedAsync(
            stream,
            GeksaCorporateConfigurationCodec.MaxConfigurationBytes,
            cancellationToken);
        try
        {
            var candidate = GeksaCorporateConfigurationCodec.Parse(bytes);
            var sourceHash = GeksaCorporateConfigurationCodec.ComputeSourceHash(candidate);
            var current = await LoadAsync(cancellationToken);
            EnsureVersionCanBeInstalled(current, candidate, sourceHash);
            return CreatePreview(
                Path.GetFileName(fullPath),
                candidate,
                sourceHash,
                current,
                userState);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task<GeksaConfigurationImportResult> ImportAsync(
        GeksaCorporateConfiguration candidate,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            GeksaCorporateConfigurationCodec.ValidateConfiguration(candidate);

            var sourceHash = GeksaCorporateConfigurationCodec.ComputeSourceHash(candidate);
            var current = await LoadAsync(cancellationToken);
            EnsureVersionCanBeInstalled(current, candidate, sourceHash);

            if (current is not null &&
                current.Revision == candidate.Revision &&
                string.Equals(current.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return new GeksaConfigurationImportResult(current, Changed: false);
            }

            var active = new GeksaActiveConfiguration(
                candidate.SchemaVersion,
                candidate.ConfigurationVersion,
                candidate.Revision,
                sourceHash,
                string.Empty,
                string.Empty,
                candidate.Resources.ToArray());

            await WriteActiveAsync(active, cancellationToken);
            ConfigurationChanged.Publish(active);
            return new GeksaConfigurationImportResult(active, Changed: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void EnsureVersionCanBeInstalled(
        GeksaActiveConfiguration? current,
        GeksaCorporateConfiguration candidate,
        string sourceHash)
    {
        if (current is null)
        {
            return;
        }

        if (candidate.Revision < current.Revision)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Нельзя установить старую ревизию {0}; уже установлена {1}.", candidate.Revision, current.Revision));
        }

        if (candidate.Revision == current.Revision &&
            !string.Equals(current.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Та же ревизия уже установлена с другим содержимым. Увеличьте revision."));
        }
    }

    internal static GeksaConfigurationImportPreview CreatePreview(
        string fileName,
        GeksaCorporateConfiguration candidate,
        string sourceHash,
        GeksaActiveConfiguration? current,
        GeksaUserResourceState? userState)
    {
        var currentById = (current?.Resources ?? [])
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var candidateById = candidate.Resources
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        var added = candidateById.Keys.Count(id => !currentById.ContainsKey(id));
        var removed = currentById.Keys.Count(id => !candidateById.ContainsKey(id));
        var updated = candidateById.Count(pair =>
            currentById.TryGetValue(pair.Key, out var oldResource) &&
            !string.Equals(
                GeksaCorporateConfigurationCodec.SerializeResourceProfile(oldResource),
                GeksaCorporateConfigurationCodec.SerializeResourceProfile(pair.Value),
                StringComparison.Ordinal));

        var managedIds = currentById.Keys
            .Concat(candidateById.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localOverrides = userState?.Resources.Count(resource => managedIds.Contains(resource.Id)) ?? 0;
        var locallyHidden = userState?.DeletedManagedResourceIds.Count(managedIds.Contains) ?? 0;

        return new GeksaConfigurationImportPreview
        {
            FileName = fileName,
            ConfigurationVersion = candidate.ConfigurationVersion,
            Revision = candidate.Revision,
            CurrentRevision = current?.Revision,
            RdpCount = candidate.Resources.Count(resource =>
                ParseTypeSafely(resource.Type) == GeksaResourceType.Rdp),
            SshCount = candidate.Resources.Count(resource =>
                ParseTypeSafely(resource.Type) == GeksaResourceType.Ssh),
            SmbCount = candidate.Resources.Count(resource =>
                ParseTypeSafely(resource.Type) == GeksaResourceType.Smb),
            AddedCount = added,
            UpdatedCount = updated,
            RemovedCount = removed,
            LocalOverrideCount = localOverrides,
            LocallyHiddenCount = locallyHidden,
            IsAlreadyInstalled = current is not null &&
                current.Revision == candidate.Revision &&
                string.Equals(current.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase),
            Candidate = candidate,
            ManagedResourceIds = managedIds
        };
    }

    private static GeksaResourceType? ParseTypeSafely(string value)
    {
        try
        {
            return GeksaCorporateConfigurationCodec.ParseResourceType(value);
        }
        catch (GeksaConfigurationException)
        {
            return null;
        }
    }

    private static async Task WriteActiveAsync(
        GeksaActiveConfiguration active,
        CancellationToken cancellationToken)
    {
        var destination = GetActiveConfigurationPath();
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, active, StoreOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string GetActiveConfigurationPath()
    {
        return Utils.GetConfigPath(ActiveConfigurationFileName);
    }
}
