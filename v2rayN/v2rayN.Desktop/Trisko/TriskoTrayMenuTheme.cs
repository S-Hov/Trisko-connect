using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace v2rayN.Desktop.Trisko;

/// <summary>Applies the shell palette to Avalonia's Windows tray menu and its submenus.</summary>
internal sealed class TriskoTrayMenuTheme : IDisposable
{
    // Avalonia 12 creates a separate window for each tray menu. It does not inherit
    // the shell's ThemeVariantScope, so both the variant and menu brushes go here.
    private static readonly (string MenuKey, string ShellKey)[] ResourceKeys =
    [
        ("MenuFlyoutBackground", "TriskoCardBackground"),
        ("MenuFlyoutBorderBrush", "TriskoCardBorder"),
        ("MenuFlyoutCornerRadius", "TriskoControlCornerRadius"),
        ("TextBlockDefaultForeground", "TriskoCardTitleForeground"),
        ("MenuItemForeground", "TriskoCardTitleForeground"),
        ("MenuItemPointeroverBackground", "TriskoNavHoverBackground"),
        ("MenuItemPointeroverForeground", "TriskoNavActiveForeground"),
        ("MenuItemPressedBackground", "TriskoNavActiveBackground"),
        ("MenuItemDisabledForeground", "TriskoSubCardLabelForeground"),
        ("MenuItemDisabledInputGestureTextForeground", "TriskoSubCardLabelForeground"),
        ("MenuItemDisabledExpandIconForeground", "TriskoSubCardLabelForeground"),
        ("MenuItemInputGestureTextForeground", "TriskoCardSubtitleForeground"),
        ("MenuItemExpandIconForeground", "TriskoCardSubtitleForeground"),
        ("MenuItemExpandIconOpenForeground", "TriskoNavActiveForeground"),
        ("MenuItemSeparatorBackground", "TriskoSeparatorBackground"),
        ("MenuFlyoutScrollViewerIconForeground", "TriskoCardSubtitleForeground")
    ];

    private readonly HashSet<Window> _menus = [];
    private readonly IDisposable _windowOpenedSubscription;
    private bool _disposed;

    public TriskoTrayMenuTheme()
    {
        _windowOpenedSubscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            // This name prefix is assigned by Avalonia's Win32 TrayIconImpl.
            if (!_disposed && window.Name?.StartsWith("AvaloniaTrayPopupRoot_", StringComparison.Ordinal) == true)
            {
                _menus.Add(window);
                window.Closed += OnMenuClosed;
                ApplyTheme(window);
            }
        });
        TriskoThemeVariants.CurrentVariantChanged += OnThemeChanged;
    }

    private static void ApplyTheme(Window window)
    {
        var variant = TriskoThemeVariants.CurrentVariant;
        window.RequestedThemeVariant = variant;
        foreach (var (menuKey, shellKey) in ResourceKeys)
        {
            if (Application.Current?.TryGetResource(shellKey, variant, out var value) == true)
            {
                window.Resources[menuKey] = value;
            }
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                foreach (var menu in _menus)
                {
                    ApplyTheme(menu);
                }
            }
        });
    }

    private void OnMenuClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnMenuClosed;
            _menus.Remove(window);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowOpenedSubscription.Dispose();
        TriskoThemeVariants.CurrentVariantChanged -= OnThemeChanged;
        foreach (var menu in _menus)
        {
            menu.Closed -= OnMenuClosed;
        }
        _menus.Clear();
    }
}
