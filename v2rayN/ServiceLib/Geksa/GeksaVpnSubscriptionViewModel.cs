namespace ServiceLib.Geksa;

public sealed record GeksaVpnSubscriptionEditContext(
    string? Url,
    string? Name = null);

public sealed record GeksaVpnSubscriptionRequest(string Url, string Name = "");

public partial class GeksaVpnSubscriptionViewModel : MyReactiveObject
{
    public GeksaVpnSubscriptionViewModel(string? currentUrl)
        : this(new GeksaVpnSubscriptionEditContext(currentUrl))
    {
    }

    public GeksaVpnSubscriptionViewModel(GeksaVpnSubscriptionEditContext? context = null)
    {
        SubscriptionUrl = context?.Url ?? string.Empty;
        SubscriptionName = context?.Name ?? string.Empty;
    }

    [Reactive] public partial string SubscriptionName { get; set; } = string.Empty;

    [Reactive]
    public partial string SubscriptionUrl { get; set; }

    [Reactive]
    public partial string? ValidationMessage { get; set; }

    public bool TryBuild(out GeksaVpnSubscriptionRequest? request)
    {
        request = null;
        var value = SubscriptionUrl.Trim();
        if (SubscriptionName.Trim().Length > 100 || SubscriptionName.Any(char.IsControl))
        {
            ValidationMessage = TriskoLocalization.Text("Название подписки должно содержать не более 100 символов без управляющих знаков.");
            return false;
        }
        if (value.Length is < 12 or > 4096 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host.IsNullOrEmpty() ||
            !uri.UserInfo.IsNullOrEmpty())
        {
            ValidationMessage = TriskoLocalization.Text("Введите полную HTTPS-ссылку подписки.");
            return false;
        }

        ValidationMessage = null;
        request = new GeksaVpnSubscriptionRequest(
            value,
            SubscriptionName.Trim());
        return true;
    }
}
