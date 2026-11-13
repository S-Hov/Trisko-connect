namespace ServiceLib.Trisko;

public sealed record TriskoSubscriptionTrafficInfo(
    long Upload,
    long Download,
    long Total,
    long Expire)
{
    public long Used => Math.Max(0, Upload) + Math.Max(0, Download);

    public bool HasLimit => Total > 0;

    public static bool TryParse(string? header, out TriskoSubscriptionTrafficInfo? info)
    {
        info = null;
        if (header.IsNullOrEmpty())
        {
            return false;
        }

        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1 ||
                !long.TryParse(part[(separator + 1)..].Trim(), out var value))
            {
                continue;
            }

            values[part[..separator].Trim()] = Math.Max(0, value);
        }

        if (!values.TryGetValue("upload", out var upload) ||
            !values.TryGetValue("download", out var download) ||
            !values.TryGetValue("total", out var total))
        {
            return false;
        }

        values.TryGetValue("expire", out var expire);
        info = new(upload, download, total, expire);
        return true;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = [TriskoLocalization.Text("Б"), TriskoLocalization.Text("КБ"), TriskoLocalization.Text("МБ"), TriskoLocalization.Text("ГБ"), TriskoLocalization.Text("ТБ")];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value} {units[unit]}" : $"{display:0.##} {units[unit]}";
    }
}

public sealed record TriskoSubscriptionUsagePresentation(
    bool HasMetadata,
    bool HasLimit,
    double Percentage,
    string PrimaryText,
    string SecondaryText,
    string ExpiryText,
    string ProgressColor)
{
    public static TriskoSubscriptionUsagePresentation Empty { get; } = new(
        false,
        false,
        0,
        TriskoLocalization.Text("Лимит трафика не передан сервером"),
        string.Empty,
        string.Empty,
        "#5B8DFF");

    public static TriskoSubscriptionUsagePresentation NotConfigured { get; } = new(
        false,
        false,
        0,
        TriskoLocalization.Text("Подписка не настроена"),
        string.Empty,
        string.Empty,
        "#5B8DFF");

    public static TriskoSubscriptionUsagePresentation Create(IReadOnlyCollection<SubItem> subscriptions)
    {
        if (subscriptions.Count == 0)
        {
            return NotConfigured;
        }

        var withMetadata = subscriptions.Where(item => item.TriskoTrafficUpdatedAt > 0).ToList();
        if (withMetadata.Count == 0)
        {
            return Empty;
        }

        var used = withMetadata.Sum(item => Math.Max(0, item.TriskoTrafficUpload) + Math.Max(0, item.TriskoTrafficDownload));
        var limited = withMetadata.Where(item => item.TriskoTrafficTotal > 0).ToList();
        var total = limited.Sum(item => item.TriskoTrafficTotal);
        var hasLimit = total > 0;
        var percentage = hasLimit ? Math.Clamp(used * 100d / total, 0d, 100d) : 0d;
        var primary = hasLimit
            ? TriskoLocalization.Format("Использовано {0} из {1}", TriskoSubscriptionTrafficInfo.FormatBytes(used), TriskoSubscriptionTrafficInfo.FormatBytes(total))
            : TriskoLocalization.Format("Использовано {0}", TriskoSubscriptionTrafficInfo.FormatBytes(used));
        var secondary = hasLimit
            ? TriskoLocalization.Format("Осталось {0} · {1:0.#}% использовано", TriskoSubscriptionTrafficInfo.FormatBytes(Math.Max(0, total - used)), percentage)
            : TriskoLocalization.Text("Без лимита трафика");
        var expire = withMetadata
            .Where(item => item.TriskoTrafficExpire > 0)
            .Select(item => item.TriskoTrafficExpire)
            .DefaultIfEmpty(0)
            .Min();
        var expiryText = expire > 0
            ? TriskoLocalization.Format("Действует до {0:dd.MM.yyyy}", DateTimeOffset.FromUnixTimeSeconds(expire).ToLocalTime())
            : TriskoLocalization.Text("Без срока окончания");
        var color = percentage >= 90 ? "#F0717E" : percentage >= 70 ? "#F3B35A" : "#3FD69A";

        return new(true, hasLimit, percentage, primary, secondary, expiryText, color);
    }
}
