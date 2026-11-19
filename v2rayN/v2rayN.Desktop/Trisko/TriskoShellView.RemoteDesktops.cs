using System.Text.Json;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoShellView
{
    private static string RemoteDesktopLayoutPath => Utils.GetConfigPath("trisko-remote-desktop-layout.json");

    private RemoteDesktopLayout _remoteLayout = new();
    private Button? _activeRemoteTab;

    private void InitializeRemoteDesktopLayout()
    {
        AnyDeskAvailability.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty) UpdateRemoteSections();
        };
        try
        {
            if (File.Exists(RemoteDesktopLayoutPath) &&
                JsonSerializer.Deserialize<RemoteDesktopLayout>(File.ReadAllText(RemoteDesktopLayoutPath)) is { } layout)
                _remoteLayout = layout;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(InitializeRemoteDesktopLayout), ex);
        }
        UpdateRemoteSections();
    }

    private async void RemoteSections_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var rdp = new CheckBox { Content = "RDP", IsChecked = _remoteLayout.Rdp };
        var vnc = new CheckBox { Content = "VNC", IsChecked = _remoteLayout.Vnc };
        var anyDesk = new CheckBox { Content = "AnyDesk", IsChecked = _remoteLayout.AnyDesk };
        var form = new StackPanel { Spacing = 8, Children = { rdp, vnc, anyDesk } };
        var dialog = new TriskoRemoteSettingsWindow(TriskoLocalization.Text("Разделы удалённого доступа"),
            TriskoLocalization.Text("Выберите, что показывать на этой странице."), form, canApply: true);
        if (!await dialog.ShowDialog<bool>(owner)) return;

        var layout = new RemoteDesktopLayout(rdp.IsChecked == true, vnc.IsChecked == true, anyDesk.IsChecked == true);
        try
        {
            await TriskoConfigurationFileIO.WriteTextAtomicallyAsync(RemoteDesktopLayoutPath, JsonSerializer.Serialize(layout));
            _remoteLayout = layout;
            UpdateRemoteSections();
            RemoteLayoutFeedback.Text = "";
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RemoteSections_Click), ex);
            RemoteLayoutFeedback.Text = TriskoLocalization.Text("Не удалось сохранить выбранные разделы.");
        }
    }

    private void RemoteTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button tab || tab == _activeRemoteTab) return;
        _activeRemoteTab = tab;
        UpdateRemoteSections();
        RemoteDesktopScroll.Offset = default;
    }

    private void UpdateRemoteSections()
    {
        RdpTab.IsVisible = _remoteLayout.Rdp;
        VncTab.IsVisible = _remoteLayout.Vnc;
        AnyDeskTab.IsVisible = _remoteLayout.AnyDesk && AnyDeskAvailability.IsVisible;
        Button[] tabs = [RdpTab, VncTab, AnyDeskTab];
        if (_activeRemoteTab is not { IsVisible: true })
            _activeRemoteTab = tabs.FirstOrDefault(tab => tab.IsVisible);
        foreach (var tab in tabs)
        {
            var selected = tab == _activeRemoteTab;
            tab.Classes.Set("primaryAction", selected);
            tab.Classes.Set("secondaryAction", !selected);
        }

        RdpCard.IsVisible = _activeRemoteTab == RdpTab;
        VncCard.IsVisible = _activeRemoteTab == VncTab;
        AnyDeskCard.IsVisible = _activeRemoteTab == AnyDeskTab;
        RemoteSectionsEmpty.IsVisible = _activeRemoteTab is null;
        UpdateRemoteMonitoring(ViewModel is { IsRemoteDesktopsPage: true });
    }

    private void UpdateRemoteMonitoring(bool pageIsOpen)
    {
        VncPanel.SetStatusMonitoring(pageIsOpen && VncCard.IsVisible);
        ViewModel?.SetAnyDeskSectionOpen(pageIsOpen && AnyDeskCard.IsVisible);
    }

    private async void RdpSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not Window owner) return;
        var import = new Button { Content = TriskoLocalization.Text("Импортировать комплект"), Classes = { "triskoSecondary" } };
        var help = new Button { Content = TriskoLocalization.Text("Справка по формату"), Classes = { "triskoSecondary" } };
        var form = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { import, help } };
        var dialog = new TriskoRemoteSettingsWindow(TriskoLocalization.Text("Настройки RDP"), TriskoLocalization.Text("Импорт и справка по файлам подключений."), form);
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
        switch (await TriskoRemoteSettingsWindow.ShowFormAsync(this, AnyDeskSettingsSlot, TriskoLocalization.Text("Настройки AnyDesk"),
            TriskoLocalization.Text("Настройте прямое подключение или обновите список рабочих мест.")))
        {
            case "advanced":
                viewModel.NavigateToAnyDeskSettingsCommand.Execute().Subscribe();
                break;
        }
    }

    private void OpenAdvancedAnyDeskSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && TopLevel.GetTopLevel(control) is TriskoRemoteSettingsWindow dialog)
            dialog.Close("advanced");
    }

    private sealed record RemoteDesktopLayout(bool Rdp = true, bool Vnc = true, bool AnyDesk = true);
}
