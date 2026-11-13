namespace ServiceLib.Trisko;

public sealed record TriskoManualVpnProfile(
    string DisplayName,
    string ShareUri,
    bool EnableTun);

public partial class TriskoManualVpnViewModel : MyReactiveObject
{
    [Reactive] public partial string DisplayName { get; set; } = "VPN";

    [Reactive] public partial string ShareUri { get; set; } = string.Empty;

    [Reactive] public partial bool EnableTun { get; set; } = true;

    [Reactive] public partial string? ValidationMessage { get; set; }

    public bool TryBuild(out TriskoManualVpnProfile? profile)
    {
        profile = null;
        ValidationMessage = null;
        var displayName = DisplayName.Trim();
        var shareUri = ShareUri.Trim();
        if (displayName.Length is < 1 or > 100 || displayName.Any(char.IsControl))
        {
            ValidationMessage = TriskoLocalization.Text("Название VPN должно содержать от 1 до 100 символов.");
            return false;
        }
        if (shareUri.Length is < 8 or > 16_384)
        {
            ValidationMessage = TriskoLocalization.Text("Вставьте полную ссылку VPN-профиля.");
            return false;
        }

        var parsed = FmtHandler.ResolveConfig(shareUri, out _);
        if (parsed is null || !parsed.IsValid())
        {
            ValidationMessage = TriskoLocalization.Text("v2rayN не распознал эту VPN-ссылку.");
            return false;
        }

        profile = new(displayName, shareUri, EnableTun);
        return true;
    }
}
