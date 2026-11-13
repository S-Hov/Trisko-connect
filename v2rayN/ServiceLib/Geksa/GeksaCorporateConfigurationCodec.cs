namespace ServiceLib.Geksa;

public static class GeksaCorporateConfigurationCodec
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxConfigurationBytes = 1024 * 1024;

    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SshUserPattern = new(
        "^[A-Za-z0-9._-]{1,64}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RdpUserPattern = new(
        "^[A-Za-z0-9._@\\\\-]{0,128}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex DriveLetterPattern = new(
        "^[D-Zd-z]:$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly JsonSerializerOptions HashOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static GeksaCorporateConfiguration Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл корпоративной конфигурации пуст."));
        }

        if (utf8Json.Length > MaxConfigurationBytes)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл корпоративной конфигурации больше 1 МиБ."));
        }

        GeksaCorporateConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<GeksaCorporateConfiguration>(utf8Json, ReadOptions);
        }
        catch (JsonException)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл корпоративной конфигурации содержит неверный JSON."));
        }

        if (configuration is null)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось прочитать корпоративную конфигурацию."));
        }

        Validate(configuration);
        return WithoutLegacyVpn(configuration);
    }

    public static GeksaResourceProfile ParseResourceProfile(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл подключения пуст."));
        }

        if (utf8Json.Length > MaxConfigurationBytes)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл подключения больше 1 МиБ."));
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Файл должен содержать одно подключение в виде JSON-объекта."));
            }

            var propertyNames = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (propertyNames.Contains("vpn") || propertyNames.Contains("resources") ||
                propertyNames.Contains("schemaVersion"))
            {
                throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Выбран полный корпоративный комплект. Импортируйте .gac3 в разделе «Корпоративная конфигурация»."));
            }
        }
        catch (GeksaConfigurationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Файл подключения содержит неверный JSON."));
        }

        GeksaResourceProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<GeksaResourceProfile>(utf8Json, ReadOptions);
        }
        catch (JsonException)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Файл подключения содержит неверный JSON или неизвестные поля."));
        }

        if (profile is null)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось прочитать подключение из файла."));
        }

        ValidateResource(profile);
        return profile;
    }

    public static string ComputeSourceHash(GeksaCorporateConfiguration configuration)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(WithoutLegacyVpn(configuration), HashOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static GeksaResourceType ParseResourceType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Тип ресурса не указан."));
        }
        return value.Trim().ToLowerInvariant() switch
        {
            "rdp" => GeksaResourceType.Rdp,
            "ssh" => GeksaResourceType.Ssh,
            "smb" => GeksaResourceType.Smb,
            _ => throw new GeksaConfigurationException(
                TriskoLocalization.Format("Ресурс имеет неподдерживаемый тип «{0}».", SafeLabel(value)))
        };
    }

    public static void ValidateResourceProfile(GeksaResourceProfile resource)
    {
        ValidateResource(resource);
    }

    public static void ValidateConfiguration(GeksaCorporateConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration);
    }

    private static void Validate(GeksaCorporateConfiguration configuration)
    {
        if (configuration.SchemaVersion != CurrentSchemaVersion)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Поддерживается schemaVersion {0}.", CurrentSchemaVersion));
        }

        if (string.IsNullOrWhiteSpace(configuration.ConfigurationVersion) ||
            !IdentifierPattern.IsMatch(configuration.ConfigurationVersion))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("configurationVersion должен содержать 1–64 латинских символа, цифры, точку, дефис или подчёркивание."));
        }

        if (configuration.Revision <= 0)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("revision должен быть положительным целым числом."));
        }

        if (configuration.Resources is null)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Отсутствует секция resources."));
        }

        if (configuration.Resources.Count > 200)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("В одном файле разрешено не более 200 ресурсов."));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in configuration.Resources)
        {
            if (resource is null)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("Секция resources содержит пустой ресурс."));
            }
            ValidateResource(resource);
            if (!ids.Add(resource.Id))
            {
                throw new GeksaConfigurationException(
                    TriskoLocalization.Format("Идентификатор ресурса «{0}» повторяется.", SafeLabel(resource.Id)));
            }
        }
    }

    private static void ValidateResource(GeksaResourceProfile resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Id) ||
            !IdentifierPattern.IsMatch(resource.Id))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Идентификатор каждого ресурса должен содержать 1–64 латинских символа, цифры, точку, дефис или подчёркивание."));
        }

        var type = ParseResourceType(resource.Type);
        ValidateDisplayText(resource.DisplayName, TriskoLocalization.Text("Название ресурса"), 100, required: true);
        ValidateDisplayText(resource.Description, TriskoLocalization.Text("Описание ресурса"), 300, required: false);
        ValidateHost(resource.Host);

        if (resource.Port is <= 0 or > 65535)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Ресурс «{0}» содержит неверный порт.", SafeLabel(resource.DisplayName)));
        }

        switch (type)
        {
            case GeksaResourceType.Rdp:
                if (resource.UserName is null || !RdpUserPattern.IsMatch(resource.UserName))
                {
                    throw new GeksaConfigurationException(
                        TriskoLocalization.Format("RDP-ресурс «{0}» содержит неверное имя пользователя.", SafeLabel(resource.DisplayName)));
                }
                break;
            case GeksaResourceType.Ssh:
                if (string.IsNullOrWhiteSpace(resource.UserName) ||
                    !SshUserPattern.IsMatch(resource.UserName))
                {
                    throw new GeksaConfigurationException(
                        TriskoLocalization.Format("SSH-ресурс «{0}» должен содержать безопасное имя пользователя.", SafeLabel(resource.DisplayName)));
                }
                break;
            case GeksaResourceType.Smb:
                ValidateSmbPath(resource.Path, resource.DisplayName);
                ValidateDriveLetter(resource.DriveLetter, resource.DisplayName);
                break;
        }
    }

    private static void ValidateDisplayText(string value, string fieldName, int maxLength, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) ||
            value is null ||
            value.Length > maxLength ||
            value.Any(char.IsControl))
        {
            throw new GeksaConfigurationException(TriskoLocalization.Format("{0} заполнено неверно.", fieldName));
        }
    }

    private static void ValidateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace) ||
            host.Contains('/') || host.Contains('\\') || host.Contains(':'))
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Адрес ресурса заполнен неверно."));
        }

        if (!IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Адрес ресурса не является IPv4-адресом или DNS-именем."));
        }
    }

    private static void ValidateSmbPath(string path, string displayName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("SMB-ресурс «{0}» содержит неверный путь.", SafeLabel(displayName)));
        }
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length is < 1 or > 240 || normalized.Any(char.IsControl) ||
            normalized.Contains(':') || normalized.Contains('"') ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("SMB-ресурс «{0}» содержит неверный путь.", SafeLabel(displayName)));
        }
    }

    private static void ValidateDriveLetter(string? driveLetter, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(driveLetter) &&
            !DriveLetterPattern.IsMatch(driveLetter.Trim()))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("SMB-ресурс «{0}» содержит неверную букву диска. ", SafeLabel(displayName)) +
                TriskoLocalization.Text("Используйте D:–Z: или оставьте поле пустым для автоматического выбора."));
        }
    }

    private static string SafeLabel(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return TriskoLocalization.Text("без названия");
        }
        var safe = new string(value.Where(character => !char.IsControl(character)).Take(80).ToArray());
        return safe.Length == 0 ? TriskoLocalization.Text("без названия") : safe;
    }

    public static string SerializeConfiguration(GeksaCorporateConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration);
        return JsonSerializer.Serialize(WithoutLegacyVpn(configuration), ExportOptions);
    }

    private static GeksaCorporateConfiguration WithoutLegacyVpn(
        GeksaCorporateConfiguration configuration) => new()
        {
            SchemaVersion = configuration.SchemaVersion,
            ConfigurationVersion = configuration.ConfigurationVersion,
            Revision = configuration.Revision,
            Resources = configuration.Resources
        };

    public static string SerializeResourceProfile(GeksaResourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateResource(profile);
        return JsonSerializer.Serialize(profile, ExportOptions);
    }
}
