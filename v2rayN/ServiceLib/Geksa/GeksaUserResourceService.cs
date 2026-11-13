namespace ServiceLib.Geksa;

public sealed class GeksaUserResourceService
{
    private const int CurrentSchemaVersion = 1;
    private const int MaxResources = 200;
    internal const string StoreFileName = "gek-sa-v3-user-resources.json";

    private static readonly JsonSerializerOptions StoreOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GeksaUserResourceService(string? storePath = null)
    {
        _storePath = storePath ?? Utils.GetConfigPath(StoreFileName);
    }

    public async Task<IReadOnlyList<GeksaResourceProfile>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return (await LoadStateAsync(cancellationToken)).Resources;
    }

    public async Task<GeksaUserResourceState> LoadStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadStateUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        GeksaCorporateConfigurationCodec.ValidateResourceProfile(resource);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var resources = state.Resources.ToList();
            var index = resources.FindIndex(item => string.Equals(
                item.Id,
                resource.Id,
                StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                resources[index] = resource;
            }
            else
            {
                if (resources.Count >= MaxResources)
                {
                    throw new GeksaConfigurationException(
                        TriskoLocalization.Format("Нельзя сохранить больше {0} пользовательских подключений.", MaxResources));
                }
                resources.Add(resource);
            }

            await WriteUnsafeAsync(new GeksaUserResourceStore(
                CurrentSchemaVersion,
                resources,
                state.DeletedManagedResourceIds
                    .Where(id => !string.Equals(id, resource.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList()), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var resources = state.Resources.ToList();
            var removed = resources.RemoveAll(item => string.Equals(
                item.Id,
                resourceId,
                StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                await WriteUnsafeAsync(new GeksaUserResourceStore(
                    CurrentSchemaVersion,
                    resources,
                    state.DeletedManagedResourceIds.ToList()), cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HideManagedAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var resources = state.Resources
                .Where(item => !string.Equals(item.Id, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var deleted = state.DeletedManagedResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            deleted.Add(resourceId);
            await WriteUnsafeAsync(new GeksaUserResourceStore(
                CurrentSchemaVersion,
                resources,
                deleted.Order(StringComparer.OrdinalIgnoreCase).ToList()), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearManagedChangesAsync(
        IEnumerable<string> managedResourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managedResourceIds);
        var managedIds = managedResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (managedIds.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var resources = state.Resources.Where(resource => !managedIds.Contains(resource.Id)).ToList();
            var deleted = state.DeletedManagedResourceIds.Where(id => !managedIds.Contains(id)).ToList();
            await WriteUnsafeAsync(new GeksaUserResourceStore(
                CurrentSchemaVersion,
                resources,
                deleted), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static IReadOnlyList<GeksaResourceProfile> Merge(
        IEnumerable<GeksaResourceProfile> managed,
        IEnumerable<GeksaResourceProfile> userResources,
        IEnumerable<string>? deletedManagedResourceIds = null)
    {
        var result = new List<GeksaResourceProfile>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var deleted = (deletedManagedResourceIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in managed.Where(resource => !deleted.Contains(resource.Id)))
        {
            indexes[resource.Id] = result.Count;
            result.Add(resource);
        }

        foreach (var resource in userResources)
        {
            if (indexes.TryGetValue(resource.Id, out var index))
            {
                result[index] = resource;
            }
            else
            {
                indexes[resource.Id] = result.Count;
                result.Add(resource);
            }
        }

        return result;
    }

    private async Task<GeksaUserResourceState> LoadStateUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return new([], new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            var file = new FileInfo(_storePath);
            if (file.Length is <= 0 or > GeksaCorporateConfigurationCodec.MaxConfigurationBytes)
            {
                throw new InvalidDataException("Invalid user resource store size.");
            }

            await using var stream = new FileStream(
                _storePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var store = await JsonSerializer.DeserializeAsync<GeksaUserResourceStore>(
                stream,
                StoreOptions,
                cancellationToken);
            if (store is null ||
                store.SchemaVersion != CurrentSchemaVersion ||
                store.Resources is null ||
                store.Resources.Count > MaxResources)
            {
                throw new InvalidDataException("Invalid user resource store content.");
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in store.Resources)
            {
                GeksaCorporateConfigurationCodec.ValidateResourceProfile(resource);
                if (!ids.Add(resource.Id))
                {
                    throw new InvalidDataException("Duplicate user resource identifier.");
                }
            }
            var deletedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var deletedId in store.DeletedManagedResourceIds ?? [])
            {
                if (string.IsNullOrWhiteSpace(deletedId) || deletedId.Length > 64 ||
                    deletedId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
                {
                    throw new InvalidDataException("Invalid deleted managed resource identifier.");
                }
                deletedIds.Add(deletedId);
            }

            return new(store.Resources.ToArray(), deletedIds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaUserResourceService), ex);
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Не удалось прочитать сохранённые вручную подключения."));
        }
    }

    private async Task WriteUnsafeAsync(
        GeksaUserResourceStore store,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storePath)
            ?? throw new InvalidOperationException("User resource store has no directory.");
        Directory.CreateDirectory(directory);

        var temporary = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
                await JsonSerializer.SerializeAsync(stream, store, StoreOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record GeksaUserResourceStore(
        int SchemaVersion,
        List<GeksaResourceProfile> Resources,
        List<string>? DeletedManagedResourceIds = null);
}
