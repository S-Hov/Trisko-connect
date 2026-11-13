using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using ServiceLib.Common;
using ServiceLib.Geksa;
using ServiceLib.ViewModels;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop.Geksa;

public sealed class GeksaTrayMenuManager : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly GeksaShellViewModel _shellViewModel;
    private readonly StatusBarViewModel _statusBarViewModel;
    private IDisposable? _vpnSubscription;
    private bool _disposed;

    public GeksaTrayMenuManager(
        MainWindow mainWindow,
        MainWindowViewModel mainWindowViewModel,
        GeksaShellViewModel shellViewModel)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
        _shellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));
        _statusBarViewModel = StatusBarViewModel.Instance;

        Init();
    }

    private void Init()
    {
        _vpnSubscription = _shellViewModel
            .WhenAnyValue(x => x.IsConnected, x => x.IsBusy, x => x.ProfileName, x => x.HasResources)
            .Subscribe(_ => Dispatcher.UIThread.Post(RebuildMenu, DispatcherPriority.Normal));

        if (_shellViewModel.Resources is INotifyCollectionChanged notifyResources)
        {
            notifyResources.CollectionChanged += OnResourcesCollectionChanged;
        }

        RebuildMenu();
    }

    private void OnResourcesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RebuildMenu, DispatcherPriority.Normal);
    }

    public void RebuildMenu()
    {
        if (_disposed)
        {
            return;
        }

        var icons = TrayIcon.GetIcons(Application.Current);
        if (icons == null || icons.Count == 0)
        {
            return;
        }

        var trayIcon = icons[0];
        trayIcon.ToolTipText = _shellViewModel.IsConnected
            ? TriskoLocalization.Format("Trisko connect: Подключено ({0})", _shellViewModel.ProfileName)
            : _shellViewModel.IsBusy
                ? TriskoLocalization.Text("Trisko connect: Подключение…")
                : TriskoLocalization.Text("Trisko connect: Отключено");

        var menu = new NativeMenu();

        // 1. Открыть главное окно
        var openAppItem = new NativeMenuItem(TriskoLocalization.Text("Открыть Trisko connect"));
        openAppItem.Click += (_, _) => _mainWindow.ShowHideWindow(true);
        menu.Items.Add(openAppItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // 2. Управление VPN (Подключить / Отключить)
        if (_shellViewModel.IsConnected)
        {
            var disconnectItem = new NativeMenuItem(TriskoLocalization.Format("Отключить VPN ({0})", _shellViewModel.ProfileName))
            {
                Command = _shellViewModel.DisconnectCommand
            };
            menu.Items.Add(disconnectItem);
        }
        else if (_shellViewModel.IsBusy)
        {
            var connectingItem = new NativeMenuItem(TriskoLocalization.Text("Подключение к VPN…")) { IsEnabled = false };
            menu.Items.Add(connectingItem);
        }
        else
        {
            var connectItem = new NativeMenuItem(TriskoLocalization.Text("Подключить VPN"))
            {
                Command = _shellViewModel.ConnectCommand
            };
            menu.Items.Add(connectItem);
        }

        menu.Items.Add(new NativeMenuItemSeparator());

        // 3. Рабочие столы
        var rdpSubMenu = new NativeMenu();
        var rdpItems = _shellViewModel.Resources.Where(r => r.IsRdp).ToList();
        if (rdpItems.Count > 0)
        {
            foreach (var rdp in rdpItems)
            {
                var targetResource = rdp.Resource;
                var item = new NativeMenuItem(rdp.DisplayName);
                item.Click += async (_, _) => await _shellViewModel.LaunchResourceAsync(targetResource);
                rdpSubMenu.Items.Add(item);
            }
        }
        else
        {
            rdpSubMenu.Items.Add(new NativeMenuItem(TriskoLocalization.Text("(нет подключений RDP)")) { IsEnabled = false });
        }

        var rdpMenuItem = new NativeMenuItem(TriskoLocalization.Text("Рабочие столы")) { Menu = rdpSubMenu };
        menu.Items.Add(rdpMenuItem);

        // 4. Файлы и сетевые диски (SMB)
        var smbSubMenu = new NativeMenu();
        var smbItems = _shellViewModel.Resources.Where(r => r.IsSmb).ToList();
        if (smbItems.Count > 0)
        {
            foreach (var smb in smbItems)
            {
                var targetResource = smb.Resource;
                var label = !string.IsNullOrWhiteSpace(smb.DriveLabel)
                    ? $"{smb.DisplayName} ({smb.DriveLabel})"
                    : smb.DisplayName;
                var item = new NativeMenuItem(label);
                item.Click += async (_, _) => await _shellViewModel.LaunchResourceAsync(targetResource);
                smbSubMenu.Items.Add(item);
            }
        }
        else
        {
            smbSubMenu.Items.Add(new NativeMenuItem(TriskoLocalization.Text("(нет сетевых папок SMB)")) { IsEnabled = false });
        }

        var smbMenuItem = new NativeMenuItem(TriskoLocalization.Text("Файлы и диски (SMB)")) { Menu = smbSubMenu };
        menu.Items.Add(smbMenuItem);

        // 5. SSH-доступ
        var sshSubMenu = new NativeMenu();
        var sshItems = _shellViewModel.Resources.Where(r => r.IsSsh).ToList();
        if (sshItems.Count > 0)
        {
            foreach (var ssh in sshItems)
            {
                var targetResource = ssh.Resource;
                var item = new NativeMenuItem(ssh.DisplayName);
                item.Click += async (_, _) => await _shellViewModel.LaunchResourceAsync(targetResource);
                sshSubMenu.Items.Add(item);
            }
        }
        else
        {
            sshSubMenu.Items.Add(new NativeMenuItem(TriskoLocalization.Text("(нет серверов SSH)")) { IsEnabled = false });
        }

        var sshMenuItem = new NativeMenuItem(TriskoLocalization.Text("SSH-доступ")) { Menu = sshSubMenu };
        menu.Items.Add(sshMenuItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // 6. Расширенные настройки (v2rayN)
        var v2raySubMenu = new NativeMenu();

        var openV2RayNItem = new NativeMenuItem(TriskoLocalization.Text("Открыть интерфейс v2rayN"));
        openV2RayNItem.Click += (_, _) =>
        {
            _mainWindow.ShowAdvancedVpnSettings(true);
            _mainWindow.ShowHideWindow(true);
        };
        v2raySubMenu.Items.Add(openV2RayNItem);

        var copyProxyItem = new NativeMenuItem(TriskoLocalization.Text("Копировать команду прокси в буфер"))
        {
            Command = _statusBarViewModel.CopyProxyCmdToClipboardCmd
        };
        v2raySubMenu.Items.Add(copyProxyItem);

        v2raySubMenu.Items.Add(new NativeMenuItemSeparator());

        var clearProxyItem = new NativeMenuItem(TriskoLocalization.Text("Очистить системный прокси"))
        {
            Command = _statusBarViewModel.SystemProxyClearCmd,
            IsChecked = _statusBarViewModel.BlSystemProxyClear
        };
        v2raySubMenu.Items.Add(clearProxyItem);

        var setProxyItem = new NativeMenuItem(TriskoLocalization.Text("Установить системный прокси"))
        {
            Command = _statusBarViewModel.SystemProxySetCmd,
            IsChecked = _statusBarViewModel.BlSystemProxySet
        };
        v2raySubMenu.Items.Add(setProxyItem);

        var nothingProxyItem = new NativeMenuItem(TriskoLocalization.Text("Не трогать системный прокси"))
        {
            Command = _statusBarViewModel.SystemProxyNothingCmd,
            IsChecked = _statusBarViewModel.BlSystemProxyNothing
        };
        v2raySubMenu.Items.Add(nothingProxyItem);

        var pacProxyItem = new NativeMenuItem(TriskoLocalization.Text("PAC-режим"))
        {
            Command = _statusBarViewModel.SystemProxyPacCmd,
            IsChecked = _statusBarViewModel.BlSystemProxyPac,
            IsVisible = _statusBarViewModel.BlSystemProxyPacVisible
        };
        v2raySubMenu.Items.Add(pacProxyItem);

        v2raySubMenu.Items.Add(new NativeMenuItemSeparator());

        var addServerClipboardItem = new NativeMenuItem(TriskoLocalization.Text("Добавить сервер из буфера обмена"));
        addServerClipboardItem.Click += (_, _) => AppEvents.AddServerViaClipboardRequested.Publish();
        v2raySubMenu.Items.Add(addServerClipboardItem);

        var subUpdateItem = new NativeMenuItem(TriskoLocalization.Text("Обновить подписки"))
        {
            Command = _statusBarViewModel.SubUpdateCmd
        };
        v2raySubMenu.Items.Add(subUpdateItem);

        var subUpdateViaProxyItem = new NativeMenuItem(TriskoLocalization.Text("Обновить подписки через прокси"))
        {
            Command = _statusBarViewModel.SubUpdateViaProxyCmd
        };
        v2raySubMenu.Items.Add(subUpdateViaProxyItem);

        var v2rayMenuItem = new NativeMenuItem(TriskoLocalization.Text("Расширенные настройки (v2rayN)")) { Menu = v2raySubMenu };
        menu.Items.Add(v2rayMenuItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // 7. Выход
        var exitItem = new NativeMenuItem(TriskoLocalization.Text("Выход"));
        exitItem.Click += async (_, _) =>
        {
            await _mainWindow.RequestApplicationExitAsync(confirm: false);
        };
        menu.Items.Add(exitItem);

        trayIcon.Menu = menu;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vpnSubscription?.Dispose();
        if (_shellViewModel.Resources is INotifyCollectionChanged notifyResources)
        {
            notifyResources.CollectionChanged -= OnResourcesCollectionChanged;
        }
    }
}
