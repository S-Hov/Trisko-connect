namespace ServiceLib.Trisko;

public static partial class TriskoSubscriptionPolicy
{
    public static SubItem Resolve(IReadOnlyCollection<SubItem> subscriptions,
        TriskoVpnSubscriptionRequest request, string? editId = null)
    {
        // Tokens and paths are case sensitive. A shared host never means a shared subscription.
        var sameUrl = subscriptions.FirstOrDefault(item => string.Equals(item.Url, request.Url, StringComparison.Ordinal));
        var editing = editId is null ? null : subscriptions.FirstOrDefault(item => item.Id == editId)
            ?? throw new TriskoConfigurationException(TriskoLocalization.Text("Подписка больше не существует."));
        if (editing is not null && sameUrl is not null && editing.Id != sameUrl.Id)
            throw new TriskoConfigurationException(TriskoLocalization.Text("Эта ссылка уже используется другой подпиской."));
        return editing ?? sameUrl ?? new SubItem
        {
            Id = string.Empty,
            Remarks = request.Name.IsNotEmpty() ? request.Name : new Uri(request.Url).Host,
            Enabled = true,
            AutoUpdateInterval = TriskoSubscriptionPolicy.DefaultIntervalMinutes
        };
    }
}
