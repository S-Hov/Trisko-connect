using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using ServiceLib.Geksa;
using System;

namespace v2rayN.Desktop.Geksa;

public partial class GeksaNotificationsView : UserControl
{
    public GeksaNotificationsView() => InitializeComponent();

    private async void CopyError_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: GeksaNotificationItem item }
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync($"{item.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n{item.Title}\n{item.Message}\n\n{item.Details}");
            (DataContext as GeksaNotificationCenter)?.Show(TriskoLocalization.Text("Скопировано"), TriskoLocalization.Text("Полная ошибка скопирована в буфер обмена."), GeksaNotificationKind.Success);
        }
        catch (Exception ex)
        {
            (DataContext as GeksaNotificationCenter)?.Show(TriskoLocalization.Text("Буфер обмена"), TriskoLocalization.Text("Не удалось скопировать ошибку."), GeksaNotificationKind.Error, ex.ToString());
        }
    }
}
