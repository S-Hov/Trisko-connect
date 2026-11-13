using System.Text.Json;

namespace v2rayN.Desktop.Geksa;

public partial class GeksaShellView
{
    private static string RemoteDesktopLayoutPath => Utils.GetConfigPath("geksa-remote-desktop-layout.json");

    private void InitializeRemoteDesktopLayout()
    {
        AnyDeskSection.PropertyChanged += (_, change) =>
        {
            if (change.Property == Expander.IsExpandedProperty) UpdateRemoteSections();
        };
        AnyDeskAvailability.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty) UpdateRemoteSections();
        };
        try
        {
            if (File.Exists(RemoteDesktopLayoutPath) &&
                JsonSerializer.Deserialize<RemoteDesktopLayout>(File.ReadAllText(RemoteDesktopLayoutPath)) is { } layout)
                ApplyRemoteDesktopLayout(layout);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(InitializeRemoteDesktopLayout), ex);
        }
    }

    private async void RemoteSections_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var rdp = new CheckBox { Content = "RDP", IsChecked = RdpCard.IsVisible };
        var vnc = new CheckBox { Content = "VNC", IsChecked = VncCard.IsVisible };
        var anyDesk = new CheckBox { Content = "AnyDesk", IsChecked = AnyDeskCard.IsVisible };
        var form = new StackPanel { Spacing = 8, Children = { rdp, vnc, anyDesk } };
        var dialog = new GeksaRemoteSettingsWindow(TriskoLocalization.Text("Разделы удалённого доступа"),
            TriskoLocalization.Text("Выберите, что показывать на этой странице."), form, canApply: true);
        if (!await dialog.ShowDialog<bool>(owner)) return;

        var layout = new RemoteDesktopLayout(rdp.IsChecked == true, vnc.IsChecked == true, anyDesk.IsChecked == true);
        try
        {
            await GeksaConfigurationFileIO.WriteTextAtomicallyAsync(RemoteDesktopLayoutPath, JsonSerializer.Serialize(layout));
            ApplyRemoteDesktopLayout(layout);
            RemoteLayoutFeedback.Text = "";
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RemoteSections_Click), ex);
            RemoteLayoutFeedback.Text = TriskoLocalization.Text("Не удалось сохранить выбранные разделы.");
        }
    }

    private void ApplyRemoteDesktopLayout(RemoteDesktopLayout layout)
    {
        RdpCard.IsVisible = layout.Rdp;
        VncCard.IsVisible = layout.Vnc;
        AnyDeskCard.IsVisible = layout.AnyDesk;
        VncPanel.SetStatusMonitoring(layout.Vnc && ViewModel is { IsRemoteDesktopsPage: true });
        UpdateRemoteSections();
    }

    private void UpdateRemoteSections()
    {
        var showAnyDesk = AnyDeskAvailability.IsVisible && AnyDeskCard.IsVisible;
        RemoteSectionsEmpty.IsVisible = !RdpCard.IsVisible && !VncCard.IsVisible && !showAnyDesk;
        ViewModel?.SetAnyDeskSectionOpen(showAnyDesk && AnyDeskSection.IsExpanded);
    }

    private async void RdpSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not Window owner) return;
        var import = new Button { Content = TriskoLocalization.Text("Импортировать комплект"), Classes = { "geksaSecondary" } };
        var help = new Button { Content = TriskoLocalization.Text("Справка по формату"), Classes = { "geksaSecondary" } };
        var form = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { import, help } };
        var dialog = new GeksaRemoteSettingsWindow(TriskoLocalization.Text("Настройки RDP"), TriskoLocalization.Text("Импорт и справка по файлам подключений."), form);
        import.Click += (_, _) => dialog.Close("import");
        help.Click += (_, _) => dialog.Close("help");
        switch (await dialog.ShowDialog<string?>(owner))
        {
            case "import":
                viewModel.ImportCorporateConfigurationCommand.Execute().Subscribe();
                break;
            case "help":
                viewModel.ShowFormatHelpCommand.Execute().Subscribe();
                break;
        }
    }

    private async void AnyDeskSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        switch (await GeksaRemoteSettingsWindow.ShowFormAsync(this, AnyDeskSettingsSlot, TriskoLocalization.Text("Настройки AnyDesk"),
            TriskoLocalization.Text("Настройте прямое подключение или обновите список рабочих мест.")))
        {
            case "advanced":
                viewModel.NavigateToAnyDeskSettingsCommand.Execute().Subscribe();
                break;
        }
    }

    private void OpenAdvancedAnyDeskSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && TopLevel.GetTopLevel(control) is GeksaRemoteSettingsWindow dialog)
            dialog.Close("advanced");
    }

    private sealed record RemoteDesktopLayout(bool Rdp = true, bool Vnc = true, bool AnyDesk = true);
}
