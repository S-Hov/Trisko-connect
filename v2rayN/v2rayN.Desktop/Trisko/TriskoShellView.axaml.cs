using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using ServiceLib.Trisko;
using ServiceLib.Manager;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoShellView : ReactiveUserControl<TriskoShellViewModel>
{
    private TriskoAnyDeskFullscreenWindow? _anyDeskFullscreenWindow;
    private TriskoCompactConnectionWindow? _compactConnectionWindow;

    public TriskoShellView()
    {
        InitializeComponent();
        InitializeRemoteDesktopLayout();

        var initialMode = AppManager.Instance.Config?.UiItem?.TriskoThemeMode ?? TriskoThemeMode.Brand;
        TriskoThemeVariants.CurrentVariant = initialMode switch
        {
            TriskoThemeMode.Light => ThemeVariant.Light,
            TriskoThemeMode.Dark => ThemeVariant.Dark,
            TriskoThemeMode.Brand => TriskoThemeVariants.Brand,
            _ => ThemeVariant.Default
        };

        TriskoThemeVariants.CurrentVariantChanged += (s, e) => this.TriskoThemeScope.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        this.TriskoThemeScope.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;

        AddHandler(KeyDownEvent, (sender, args) =>
        {
            if (args.Key == Key.K && args.KeyModifiers.HasFlag(KeyModifiers.Control) && ViewModel is not null && !ViewModel.IsAnyModalOpen)
            {
                ViewModel.OpenGlobalSearchCommand.Execute().Subscribe();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && ViewModel is { IsGlobalSearchOpen: true })
            {
                ViewModel.IsGlobalSearchOpen = false;
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && ViewModel is { IsAnyDeskNameOpen: true })
            {
                ViewModel.IsAnyDeskNameOpen = false;
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && ViewModel is { IsGlobalSearchOpen: true } vm && GlobalSearchTextBox.IsFocused)
            {
                var first = vm.GlobalSearchGroups.FirstOrDefault()?.Items.FirstOrDefault();
                if (first is not null) vm.ActivateSearchResultCommand.Execute(first).Subscribe();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && ViewModel is { IsAnyDeskNameOpen: true } nameVm && AnyDeskNameTextBox.IsFocused)
            {
                nameVm.SaveAnyDeskNameCommand.Execute().Subscribe();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && ViewModel is { IsVpnServersModalOpen: true })
            {
                ViewModel.IsVpnServersModalOpen = false;
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        this.WhenActivated(disposables =>
        {
            disposables.Add(this.WhenAnyValue(x => x.ViewModel!.IsRemoteDesktopsPage)
                .Subscribe(UpdateRemoteMonitoring));
            disposables.Add(new DelegateDisposable(() => UpdateRemoteMonitoring(false)));
            disposables.Add(this.WhenAnyValue(x => x.ViewModel!.IsGlobalSearchOpen).Where(open => open)
                .Subscribe(_ => Dispatcher.UIThread.Post(() => GlobalSearchTextBox.Focus())));
            disposables.Add(this.WhenAnyValue(x => x.ViewModel!.IsAnyDeskNameOpen).Where(open => open)
                .Subscribe(_ => Dispatcher.UIThread.Post(() => { AnyDeskNameTextBox.Focus(); AnyDeskNameTextBox.SelectAll(); })));
            this.WhenAnyValue(x => x.ViewModel!.ThemeMode)
                .Subscribe(mode =>
                {
                    if (Application.Current != null)
                    {
                        var newVariant = mode switch
                        {
                            TriskoThemeMode.Light => ThemeVariant.Light,
                            TriskoThemeMode.Dark => ThemeVariant.Dark,
                            TriskoThemeMode.Brand => TriskoThemeVariants.Brand,
                            _ => ThemeVariant.Default
                        };
                        TriskoThemeVariants.CurrentVariant = newVariant;
                    }
                })
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.ViewModel!.IsVpnServersModalOpen)
                .Subscribe(isOpen =>
                {
                    if (isOpen)
                    {
                        AnimateModalItemsEntrance();
                    }
                    else
                    {
                        _modalAnimCts?.Cancel();
                        EnsureAllContainersVisible();
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private void OpenCompactConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (_compactConnectionWindow is { } existing) { existing.Activate(); return; }
        if (ViewModel is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var window = new TriskoCompactConnectionWindow { DataContext = ViewModel };
        _compactConnectionWindow = window;
        void CloseCompact(object? s, EventArgs args) => window.Close();
        owner.Closed += CloseCompact;
        window.Closed += (_, _) =>
        {
            owner.Closed -= CloseCompact;
            _compactConnectionWindow = null;
        };
        window.Show();
    }

    private void AnyDeskFullscreenButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_anyDeskFullscreenWindow is { } existingWindow)
        {
            existingWindow.Activate();
            return;
        }

        if (ViewModel is not { IsAnyDeskEmbeddedWindowAttached: true }
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var fullscreenWindow = new TriskoAnyDeskFullscreenWindow(ViewModel.EffectiveAnyDeskExecutablePath);
        _anyDeskFullscreenWindow = fullscreenWindow;
        ViewModel.IsAnyDeskFullscreenOpen = true;
        fullscreenWindow.Opened += (_, _) => _ = TransferAnyDeskToFullscreenAsync(fullscreenWindow);
        fullscreenWindow.Closing += (_, _) =>
        {
            if (fullscreenWindow.AnyDeskHost.IsWindowAttached)
            {
                fullscreenWindow.AnyDeskHost.TransferWindowTo(EmbeddedAnyDeskHost);
            }
        };
        fullscreenWindow.Closed += (_, _) =>
        {
            _anyDeskFullscreenWindow = null;
            if (ViewModel is { } viewModel)
            {
                viewModel.IsAnyDeskFullscreenOpen = false;
            }
        };
        fullscreenWindow.Show(owner);
    }

    private async Task TransferAnyDeskToFullscreenAsync(TriskoAnyDeskFullscreenWindow fullscreenWindow)
    {
        for (var attempt = 0; attempt < 20 && ReferenceEquals(_anyDeskFullscreenWindow, fullscreenWindow); attempt++)
        {
            if (fullscreenWindow.AnyDeskHost.CanAcceptWindow)
            {
                if (!EmbeddedAnyDeskHost.TransferWindowTo(fullscreenWindow.AnyDeskHost))
                {
                    fullscreenWindow.Close();
                }
                return;
            }

            await Task.Delay(50);
        }

        if (ReferenceEquals(_anyDeskFullscreenWindow, fullscreenWindow))
        {
            fullscreenWindow.Close();
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private void ModalBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not null && ViewModel.IsVpnServersModalOpen)
        {
            ViewModel.IsVpnServersModalOpen = false;
        }
    }

    private void UsabilityBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.IsGlobalSearchOpen = false;
        ViewModel.IsAnyDeskNameOpen = false;
        e.Handled = true;
    }

    private async void VpnProfileItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || ViewModel.CanSelectVpnProfile)
        {
            return;
        }

        // Р•СЃР»Рё РЅР°Р¶Р°С‚РёРµ РїСЂРѕРёР·РѕС€Р»Рѕ РїРѕ РёРЅС‚РµСЂР°РєС‚РёРІРЅРѕРјСѓ СЌР»РµРјРµРЅС‚Сѓ РІРЅСѓС‚СЂРё СЃС‚СЂРѕРєРё (РЅР°РїСЂРёРјРµСЂ, РєРЅРѕРїРєРµ СЃРїРёРґРѕРјРµС‚СЂР°), СЂР°Р·СЂРµС€Р°РµРј РєР»РёРє
        if (e.Source is Visual sourceVisual && (sourceVisual is Button || sourceVisual.FindAncestorOfType<Button>() is not null))
        {
            return;
        }

        // Р•СЃР»Рё СЌС‚Рѕ РїСЂР°РІС‹Р№ РєР»РёРє РґР»СЏ РІС‹Р·РѕРІР° РєРѕРЅС‚РµРєСЃС‚РЅРѕРіРѕ РјРµРЅСЋ, РЅРµ Р±Р»РѕРєРёСЂСѓРµРј
        var point = e.GetCurrentPoint(sender as Visual);
        if (point.Properties.IsRightButtonPressed)
        {
            return;
        }

        // РќРµ РґР°С‘Рј ListBox СЃР±РёС‚СЊ РїРѕРґСЃРІРµС‚РєСѓ Р°РєС‚РёРІРЅРѕРіРѕ СЃРµСЂРІРµСЂР°, РЅРѕ РѕР±СЉСЏСЃРЅСЏРµРј РїРѕР»СЊР·РѕРІР°С‚РµР»СЋ,
        // РїРѕС‡РµРјСѓ РїРµСЂРµРєР»СЋС‡РµРЅРёРµ СЃРµР№С‡Р°СЃ РЅРµРґРѕСЃС‚СѓРїРЅРѕ.
        e.Handled = true;
        if (sender is Control { DataContext: TriskoVpnProfileOption profile })
        {
            await ViewModel.SelectVpnProfileAsync(profile);
        }
    }

    private async void VpnProfiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: TriskoVpnProfileOption profile } list && ViewModel is not null)
        {
            if (!ViewModel.CanSelectVpnProfile)
            {
                await ViewModel.SelectVpnProfileAsync(profile);
                if (!ReferenceEquals(list.SelectedItem, ViewModel.SelectedVpnProfile))
                {
                    list.SelectedItem = ViewModel.SelectedVpnProfile;
                }
                return;
            }

            if (!ReferenceEquals(profile, ViewModel.SelectedVpnProfile))
            {
                await ViewModel.SelectVpnProfileAsync(profile);
                if (ReferenceEquals(list.SelectedItem, profile))
                {
                    list.SelectedItem = ViewModel.SelectedVpnProfile;
                }
            }
        }
    }

    private async void VpnProfileItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        // Р•СЃР»Рё РґРІРѕР№РЅРѕР№ РєР»РёРє РїСЂРѕРёР·РѕС€С‘Р» РїРѕ РІРЅСѓС‚СЂРµРЅРЅРµР№ РєРЅРѕРїРєРµ (РЅР°РїСЂРёРјРµСЂ, СЃРїРёРґРѕРјРµС‚СЂСѓ РёР»Рё РјРµРЅСЋ), РЅРµ Р·Р°РєСЂС‹РІР°РµРј РјРѕРґР°Р»СЊРЅРѕРµ РѕРєРЅРѕ
        if (e.Source is Visual sourceVisual)
        {
            var btn = sourceVisual as Button ?? sourceVisual.FindAncestorOfType<Button>();
            if (btn is not null)
            {
                return;
            }
        }

        if (sender is Control { DataContext: TriskoVpnProfileOption profile })
        {
            if (!ViewModel.CanSelectVpnProfile)
            {
                await ViewModel.SelectVpnProfileAsync(profile);
                e.Handled = true;
                return;
            }

            await ViewModel.SelectVpnProfileAsync(profile);
            ViewModel.IsVpnServersModalOpen = false;
        }
    }

    private CancellationTokenSource? _modalAnimCts;
    private long _modalAnimationGeneration;

    private void AnimateModalItemsEntrance()
    {
        _modalAnimCts?.Cancel();
        _modalAnimCts = new CancellationTokenSource();
        var ct = _modalAnimCts.Token;
        var generation = Interlocked.Increment(ref _modalAnimationGeneration);

        Dispatcher.UIThread.Post(() => _ = RunModalItemsEntranceAsync(generation, ct));
    }

    private async Task RunModalItemsEntranceAsync(long generation, CancellationToken ct)
    {
        try
        {
            // Р”Р°С‘Рј РјРѕРґР°Р»СЊРЅРѕРјСѓ РѕРєРЅСѓ Р·Р°РІРµСЂС€РёС‚СЊ layout Рё СЃРѕР·РґР°С‚СЊ РІРёРґРёРјС‹Рµ РєРѕРЅС‚РµР№РЅРµСЂС‹.
            await Task.Delay(40, ct);
            if (!IsCurrentModalAnimation(generation, ct))
            {
                return;
            }

            var rows = new List<Grid>();
            foreach (var listBox in FindVpnServerLists())
            {
                for (int i = 0; i < listBox.ItemCount; i++)
                {
                    if (listBox.ContainerFromIndex(i) is { } container &&
                        container.GetVisualDescendants().OfType<Grid>()
                            .FirstOrDefault(grid => grid.Classes.Contains("vpnProfileItemRow")) is { } row)
                    {
                        rows.Add(row);
                    }
                }

                rows.AddRange(listBox.GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(grid => grid.Classes.Contains("vpnProfileItemRow"))
                    .Where(row => !rows.Contains(row)));
            }

            foreach (var row in rows)
            {
                SetRowEntranceState(row, isVisible: false);
            }

            var animations = rows.Select((row, index) =>
                AnimateRowEntranceAsync(row, index * 35, generation, ct));
            await Task.WhenAll(animations);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Р—Р°РєСЂС‹С‚РёРµ РёР»Рё РїРѕРІС‚РѕСЂРЅРѕРµ РѕС‚РєСЂС‹С‚РёРµ РѕРєРЅР° С€С‚Р°С‚РЅРѕ РѕС‚РјРµРЅСЏРµС‚ РїСЂРµРґС‹РґСѓС‰РёР№ Р·Р°РїСѓСЃРє.
        }
        finally
        {
            // РўРѕР»СЊРєРѕ Р°РєС‚СѓР°Р»СЊРЅС‹Р№ Р·Р°РїСѓСЃРє РІРїСЂР°РІРµ СЃР±СЂР°СЃС‹РІР°С‚СЊ СЃРІРѕР№СЃС‚РІР°: СЃС‚Р°СЂС‹Рµ Р·Р°РґР°С‡Рё Р±РѕР»СЊС€Рµ
            // РЅРµ РјРѕРіСѓС‚ РІРјРµС€Р°С‚СЊСЃСЏ РІ РЅРѕРІРѕРµ РѕС‚РєСЂС‹С‚РёРµ РјРѕРґР°Р»СЊРЅРѕРіРѕ РѕРєРЅР°.
            if (generation == Volatile.Read(ref _modalAnimationGeneration))
            {
                EnsureAllContainersVisible();
            }
        }
    }

    private async Task AnimateRowEntranceAsync(
        Grid row,
        int delayMilliseconds,
        long generation,
        CancellationToken ct)
    {
        if (delayMilliseconds > 0)
        {
            await Task.Delay(delayMilliseconds, ct);
        }

        if (!IsCurrentModalAnimation(generation, ct))
        {
            return;
        }

        var animation = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.0),
                        new Setter(TranslateTransform.YProperty, 12.0)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1.0),
                        new Setter(TranslateTransform.YProperty, 0.0)
                    }
                }
            }
        };

        await animation.RunAsync(row, ct);
        if (IsCurrentModalAnimation(generation, ct))
        {
            SetRowEntranceState(row, isVisible: true);
        }
    }

    private bool IsCurrentModalAnimation(long generation, CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        generation == Volatile.Read(ref _modalAnimationGeneration) &&
        ViewModel is { IsVpnServersModalOpen: true };

    private static void SetRowEntranceState(Grid row, bool isVisible)
    {
        row.Opacity = isVisible ? 1.0 : 0.0;
        if (row.RenderTransform is TranslateTransform translate)
        {
            translate.Y = isVisible ? 0.0 : 12.0;
        }
        else
        {
            row.RenderTransform = new TranslateTransform(0, isVisible ? 0.0 : 12.0);
        }
    }

    private void EnsureAllContainersVisible()
    {
        foreach (var listBox in FindVpnServerLists())
        {
            foreach (var row in listBox.GetVisualDescendants()
                         .OfType<Grid>()
                         .Where(grid => grid.Classes.Contains("vpnProfileItemRow")))
            {
                SetRowEntranceState(row, isVisible: true);
            }
        }
    }

    private IEnumerable<ListBox> FindVpnServerLists()
    {
        if (this.FindControl<ListBox>("RegularVpnServersListBox") is { } regular)
        {
            yield return regular;
        }
        if (this.FindControl<ListBox>("AnyDeskVpnServersListBox") is { } anyDesk)
        {
            yield return anyDesk;
        }
    }
}
