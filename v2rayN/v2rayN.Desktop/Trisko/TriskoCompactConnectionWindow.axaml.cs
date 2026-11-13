using Avalonia.Controls;
using ServiceLib.Trisko;
using System;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoCompactConnectionWindow : Window
{
    public TriskoCompactConnectionWindow()
    {
        InitializeComponent();
        ApplyTheme();
        TriskoThemeVariants.CurrentVariantChanged += ThemeChanged;
        Closed += (_, _) => TriskoThemeVariants.CurrentVariantChanged -= ThemeChanged;
    }

    private void ThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        CompactThemeScope.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
    }

    private async void Server_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TriskoShellViewModel vm || sender is not ComboBox { SelectedItem: TriskoVpnProfileOption profile }
            || ReferenceEquals(profile, vm.SelectedVpnProfile)) return;
        try { await vm.SelectVpnProfileAsync(profile); }
        catch (Exception ex) { vm.Notifications.Show(TriskoLocalization.Text("Выбор сервера"), TriskoLocalization.Text("Не удалось выбрать сервер."), TriskoNotificationKind.Error, ex.ToString()); }
    }
}
