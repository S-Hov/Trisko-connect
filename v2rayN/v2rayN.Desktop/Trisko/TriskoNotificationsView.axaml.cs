using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using ServiceLib.Trisko;
using System;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoNotificationsView : UserControl
{
    public TriskoNotificationsView() => InitializeComponent();

    private async void CopyError_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: TriskoNotificationItem item }
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync($"{item.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n{item.Title}\n{item.Message}\n\n{item.Details}");
            (DataContext as TriskoNotificationCenter)?.Show(TriskoLocalization.Text("Скопировано"), TriskoLocalization.Text("Полная ошибка скопирована в буфер обмена."), TriskoNotificationKind.Success);
        }
        catch (Exception ex)
        {
            (DataContext as TriskoNotificationCenter)?.Show(TriskoLocalization.Text("Буфер обмена"), TriskoLocalization.Text("Не удалось скопировать ошибку."), TriskoNotificationKind.Error, ex.ToString());
        }
    }
}
