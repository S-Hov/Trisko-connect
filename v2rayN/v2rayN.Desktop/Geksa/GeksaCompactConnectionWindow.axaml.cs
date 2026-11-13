using Avalonia.Controls;
using ServiceLib.Geksa;
using System;

namespace v2rayN.Desktop.Geksa;

public partial class GeksaCompactConnectionWindow : Window
{
    public GeksaCompactConnectionWindow()
    {
        InitializeComponent();
        ApplyTheme();
        GeksaThemeVariants.CurrentVariantChanged += ThemeChanged;
        Closed += (_, _) => GeksaThemeVariants.CurrentVariantChanged -= ThemeChanged;
    }

    private void ThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        CompactThemeScope.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
    }

    private async void Server_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not GeksaShellViewModel vm || sender is not ComboBox { SelectedItem: GeksaVpnProfileOption profile }
            || ReferenceEquals(profile, vm.SelectedVpnProfile)) return;
        try { await vm.SelectVpnProfileAsync(profile); }
        catch (Exception ex) { vm.Notifications.Show(TriskoLocalization.Text("Выбор сервера"), TriskoLocalization.Text("Не удалось выбрать сервер."), GeksaNotificationKind.Error, ex.ToString()); }
    }
}
