namespace ServiceLib.Geksa;

public static class TriskoSubscriptionPolicy
{
    public static SubItem Resolve(IReadOnlyCollection<SubItem> subscriptions,
        GeksaVpnSubscriptionRequest request, string? editId = null)
    {
        // Tokens and paths are case sensitive. A shared host never means a shared subscription.
        var sameUrl = subscriptions.FirstOrDefault(item => string.Equals(item.Url, request.Url, StringComparison.Ordinal));
        var editing = editId is null ? null : subscriptions.FirstOrDefault(item => item.Id == editId)
            ?? throw new GeksaConfigurationException(TriskoLocalization.Text("Подписка больше не существует."));
        if (editing is not null && sameUrl is not null && editing.Id != sameUrl.Id)
            throw new GeksaConfigurationException(TriskoLocalization.Text("Эта ссылка уже используется другой подпиской."));
        return editing ?? sameUrl ?? new SubItem
        {
            Id = string.Empty,
            Remarks = request.Name.IsNotEmpty() ? request.Name : new Uri(request.Url).Host,
            Enabled = true,
            AutoUpdateInterval = GeksaSubscriptionPolicy.DefaultIntervalMinutes
        };
    }
}
