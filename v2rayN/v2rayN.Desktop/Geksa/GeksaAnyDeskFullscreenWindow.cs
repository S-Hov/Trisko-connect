using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace v2rayN.Desktop.Geksa;

// A second application window, not a second application or AnyDesk process.
public sealed class GeksaAnyDeskFullscreenWindow : Window
{
    private const double OverlayWidth = 150;
    private const double OverlayHeight = 40;
    private Window? _controlsOverlay;

    public GeksaAnyDeskNativeHost AnyDeskHost { get; }

    public GeksaAnyDeskFullscreenWindow(string executablePath)
    {
        Title = "AnyDesk · Trisko connect";
        WindowState = WindowState.FullScreen;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Black;

        AnyDeskHost = new GeksaAnyDeskNativeHost
        {
            ExecutablePath = executablePath,
            IsActive = true,
            ShowTitleBar = true
        };
        Content = AnyDeskHost;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ShowControlsOverlay();
        Dispatcher.UIThread.Post(PositionControlsOverlay, DispatcherPriority.Loaded);
        DispatcherTimer.RunOnce(PositionControlsOverlay, TimeSpan.FromMilliseconds(150));
    }

    protected override void OnClosed(EventArgs e)
    {
        _controlsOverlay?.Close();
        _controlsOverlay = null;
        base.OnClosed(e);
    }

    private void ShowControlsOverlay()
    {
        var minimizeButton = new Button
        {
            Content = TriskoLocalization.Text("Свернуть"),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Padding = new Thickness(12, 0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(Color.Parse("#242424")),
            Foreground = new SolidColorBrush(Color.Parse("#58A6FF")),
            FontWeight = FontWeight.SemiBold
        };
        minimizeButton.Click += (_, _) => Close();

        _controlsOverlay = new Window
        {
            Width = OverlayWidth,
            Height = OverlayHeight,
            Content = minimizeButton,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#242424"))
        };
        _controlsOverlay.Show(this);
    }

    private void PositionControlsOverlay()
    {
        if (_controlsOverlay is null)
        {
            return;
        }

        var scaling = Math.Max(1, RenderScaling);
        var clientWidthPixels = (int)Math.Round(ClientSize.Width * scaling);
        var overlayWidthPixels = (int)Math.Round(OverlayWidth * scaling);
        _controlsOverlay.Position = new PixelPoint(
            Position.X + Math.Max(0, clientWidthPixels - overlayWidthPixels),
            Position.Y);
    }
}
