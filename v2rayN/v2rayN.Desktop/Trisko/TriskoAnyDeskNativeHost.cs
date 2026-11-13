using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform;
using Avalonia.Threading;
using ServiceLib;

namespace v2rayN.Desktop.Trisko;

public sealed class TriskoAnyDeskNativeHost : NativeControlHost
{
    public static readonly StyledProperty<string?> ExecutablePathProperty =
        AvaloniaProperty.Register<TriskoAnyDeskNativeHost, string?>(nameof(ExecutablePath));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<TriskoAnyDeskNativeHost, bool>(nameof(IsActive));

    public static readonly StyledProperty<bool> IsWindowAttachedProperty =
        AvaloniaProperty.Register<TriskoAnyDeskNativeHost, bool>(
            nameof(IsWindowAttached),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowTitleBarProperty =
        AvaloniaProperty.Register<TriskoAnyDeskNativeHost, bool>(nameof(ShowTitleBar));

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsPopup = unchecked((long)0x80000000L);
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsClipSiblings = 0x04000000L;
    private const long WsClipChildren = 0x02000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;
    private const uint WmSize = 0x0005;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int EmbeddedTitleBarHeight = 32;
    private const uint DefaultDpi = 96;

    private IntPtr _containerWindow;
    private IntPtr _anyDeskWindow;
    private IntPtr _originalParent;
    private IntPtr _originalStyle;
    private IntPtr _originalExStyle;
    private WINDOWPLACEMENT _originalPlacement;
    private CancellationTokenSource? _attachCancellation;

    public string? ExecutablePath
    {
        get => GetValue(ExecutablePathProperty);
        set => SetValue(ExecutablePathProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsWindowAttached
    {
        get => GetValue(IsWindowAttachedProperty);
        set => SetValue(IsWindowAttachedProperty, value);
    }

    public bool ShowTitleBar
    {
        get => GetValue(ShowTitleBarProperty);
        set => SetValue(ShowTitleBarProperty, value);
    }

    public bool CanAcceptWindow =>
        _containerWindow != IntPtr.Zero
        && GetClientRect(_containerWindow, out var rectangle)
        && rectangle.Right - rectangle.Left > 1
        && rectangle.Bottom - rectangle.Top > 1;

    // The AnyDesk HWND stays a child during the handoff: it never becomes a
    // second desktop window and its active remote session remains intact.
    public bool TransferWindowTo(TriskoAnyDeskNativeHost destination)
    {
        if (ReferenceEquals(this, destination)
            || _anyDeskWindow == IntPtr.Zero
            || !IsWindow(_anyDeskWindow)
            || destination._containerWindow == IntPtr.Zero)
        {
            return false;
        }

        CancelAttach();
        destination.CancelAttach();
        if (destination._anyDeskWindow != IntPtr.Zero)
        {
            return false;
        }

        var window = _anyDeskWindow;
        ShowWindow(window, SwHide);
        Marshal.SetLastPInvokeError(0);
        if (SetParent(window, destination._containerWindow) == IntPtr.Zero
            && Marshal.GetLastPInvokeError() != 0)
        {
            ShowWindow(window, IsActive ? SwShow : SwHide);
            return false;
        }

        destination._anyDeskWindow = window;
        destination._originalParent = _originalParent;
        destination._originalStyle = _originalStyle;
        destination._originalExStyle = _originalExStyle;
        destination._originalPlacement = _originalPlacement;
        _anyDeskWindow = IntPtr.Zero;
        IsWindowAttached = false;
        destination.IsWindowAttached = true;
        destination.ResizeEmbeddedWindow();
        if (destination.IsActive)
        {
            ShowWindow(window, SwShow);
            destination.RefreshEmbeddedWindow();
            destination.QueueRefreshEmbeddedWindow();
        }
        return true;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        _containerWindow = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            (uint)(WsChild | WsVisible | WsClipChildren | WsClipSiblings),
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (_containerWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException(TriskoLocalization.Text("Не удалось создать контейнер для окна AnyDesk."));
        }

        QueueAttach();
        return new PlatformHandle(_containerWindow, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!OperatingSystem.IsWindows())
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        CancelAttach();
        DetachAnyDeskWindow();
        if (control.Handle != IntPtr.Zero)
        {
            DestroyWindow(control.Handle);
        }
        _containerWindow = IntPtr.Zero;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (change.Property == IsActiveProperty)
        {
            if (_containerWindow != IntPtr.Zero) ShowWindow(_containerWindow, IsActive ? SwShow : SwHide);
            if (change.GetNewValue<bool>())
            {
                if (_anyDeskWindow != IntPtr.Zero && IsWindow(_anyDeskWindow))
                {
                    ShowWindow(_anyDeskWindow, SwShow);
                    ResizeEmbeddedWindow();
                    RefreshEmbeddedWindow();
                }
                else
                {
                    QueueAttach();
                }
            }
            else if (_anyDeskWindow != IntPtr.Zero)
            {
                if (IsWindow(_anyDeskWindow))
                {
                    ShowWindow(_anyDeskWindow, SwHide);
                }
                else
                {
                    _anyDeskWindow = IntPtr.Zero;
                    IsWindowAttached = false;
                }
            }
        }
        else if (change.Property == ExecutablePathProperty)
        {
            DetachAnyDeskWindow();
            QueueAttach();
        }
        else if (change.Property == BoundsProperty)
        {
            ResizeEmbeddedWindow();
        }
        else if (change.Property == ShowTitleBarProperty)
        {
            ResizeEmbeddedWindow();
        }
    }

    private void QueueAttach()
    {
        if (!IsActive || _containerWindow == IntPtr.Zero || ExecutablePath is null)
        {
            return;
        }
        CancelAttach();
        _attachCancellation = new CancellationTokenSource();
        var cancellationToken = _attachCancellation.Token;
        Dispatcher.UIThread.Post(() => _ = AttachWhenReadyAsync(cancellationToken));
    }

    private async Task AttachWhenReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                var window = FindAnyDeskTopLevelWindow(ExecutablePath!);
                if (window != IntPtr.Zero)
                {
                    AttachAnyDeskWindow(window);
                    return;
                }
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(TriskoAnyDeskNativeHost), ex);
        }
    }

    private void AttachAnyDeskWindow(IntPtr window)
    {
        if (_containerWindow == IntPtr.Zero || !IsWindow(window))
        {
            return;
        }

        _anyDeskWindow = window;
        IsWindowAttached = true;
        _originalParent = GetParent(window);
        _originalStyle = GetWindowLongPtr(window, GwlStyle);
        _originalExStyle = GetWindowLongPtr(window, GwlExStyle);
        _originalPlacement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(window, ref _originalPlacement);

        var embeddedStyle = _originalStyle.ToInt64();
        embeddedStyle &= ~(WsPopup | WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
        embeddedStyle |= WsChild | WsVisible | WsClipChildren | WsClipSiblings;
        SetWindowLongPtr(window, GwlStyle, new IntPtr(embeddedStyle));
        SetParent(window, _containerWindow);
        ResizeEmbeddedWindow();
        ShowWindow(window, SwShow);
        RefreshEmbeddedWindow();
        QueueRefreshEmbeddedWindow();
    }

    private void ResizeEmbeddedWindow()
    {
        if (_containerWindow == IntPtr.Zero
            || _anyDeskWindow == IntPtr.Zero
            || !IsWindow(_anyDeskWindow)
            || !GetClientRect(_containerWindow, out var rectangle))
        {
            return;
        }

        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        var dpi = GetDpiForWindow(_containerWindow);
        var titleBarHeight = ShowTitleBar
            ? 0
            : (int)Math.Round(
                EmbeddedTitleBarHeight * Math.Max(DefaultDpi, dpi) / (double)DefaultDpi);
        SetWindowPos(
            _anyDeskWindow,
            IntPtr.Zero,
            0,
            -titleBarHeight,
            width,
            height + titleBarHeight,
            SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }

    private void RefreshEmbeddedWindow()
    {
        if (_anyDeskWindow == IntPtr.Zero || !IsWindow(_anyDeskWindow))
        {
            return;
        }

        if (GetClientRect(_anyDeskWindow, out var rectangle))
        {
            var width = Math.Max(1, rectangle.Right - rectangle.Left);
            var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
            PostMessage(_anyDeskWindow, WmSize, IntPtr.Zero, new IntPtr((height << 16) | (width & 0xFFFF)));
        }
        RedrawWindow(
            _anyDeskWindow,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow | RdwFrame);
    }

    private void QueueRefreshEmbeddedWindow()
    {
        DispatcherTimer.RunOnce(() =>
        {
            ResizeEmbeddedWindow();
            RefreshEmbeddedWindow();
        }, TimeSpan.FromMilliseconds(80));
        DispatcherTimer.RunOnce(() =>
        {
            ResizeEmbeddedWindow();
            RefreshEmbeddedWindow();
        }, TimeSpan.FromMilliseconds(280));
    }

    private void DetachAnyDeskWindow()
    {
        if (_anyDeskWindow == IntPtr.Zero)
        {
            return;
        }

        if (IsWindow(_anyDeskWindow))
        {
            ShowWindow(_anyDeskWindow, SwHide);
            SetParent(_anyDeskWindow, _originalParent);
            SetWindowLongPtr(_anyDeskWindow, GwlStyle, _originalStyle);
            SetWindowLongPtr(_anyDeskWindow, GwlExStyle, _originalExStyle);
            if (_originalPlacement.Length != 0)
            {
                SetWindowPlacement(_anyDeskWindow, ref _originalPlacement);
            }
            SetWindowPos(
                _anyDeskWindow,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoActivate | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
            ShowWindow(_anyDeskWindow, _originalPlacement.ShowCommand is > 0 and <= 11
                ? _originalPlacement.ShowCommand
                : SwShow);
        }
        _anyDeskWindow = IntPtr.Zero;
        IsWindowAttached = false;
    }

    private void CancelAttach()
    {
        var cancellation = Interlocked.Exchange(ref _attachCancellation, null);
        if (cancellation is null)
        {
            return;
        }
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private static IntPtr FindAnyDeskTopLevelWindow(string executablePath)
    {
        var expectedPath = Path.GetFullPath(executablePath);
        var bestWindow = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetParent(window) != IntPtr.Zero)
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var processPath = process.MainModule?.FileName;
                if (processPath is null
                    || !string.Equals(
                        Path.GetFullPath(processPath),
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase)
                    || !GetWindowRect(window, out var rectangle))
                {
                    return true;
                }

                var area = (long)Math.Max(0, rectangle.Right - rectangle.Left)
                    * Math.Max(0, rectangle.Bottom - rectangle.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    bestWindow = window;
                }
            }
            catch
            {
            }
            return true;
        }, IntPtr.Zero);
        return bestWindow;
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public POINT MinimumPosition;
        public POINT MaximumPosition;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out RECT rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr window, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr window, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr window,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
}
