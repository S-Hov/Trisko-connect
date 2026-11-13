using System.ComponentModel;
using MarcusW.VncClient;
using MarcusW.VncClient.Protocol.Implementation;
using MarcusW.VncClient.Protocol.Implementation.SecurityTypes;
using MarcusW.VncClient.Protocol.Implementation.Services.Transports;
using MarcusW.VncClient.Protocol.SecurityTypes;
using MarcusW.VncClient.Security;
using Microsoft.Extensions.Logging.Abstractions;
using v2rayN.Desktop.Trisko.Vnc;

namespace v2rayN.Desktop.Trisko;

public sealed class TriskoVncSessionWindow : Window
{
    private readonly VncView _screen = new();
    private readonly TextBlock _status = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly CancellationTokenSource _cancellation = new();
    private RfbConnection? _connection;
    private Task? _connectTask;
    private bool _closing;

    public TriskoVncSessionWindow(string host, int port, string password)
    {
        Title = $"VNC — {host}:{port}";
        Width = 1100; Height = 750; MinWidth = 600; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var disconnect = new Button { Content = TriskoLocalization.Text("Отключиться") };
        disconnect.Click += (_, _) => Close();
        var fullscreen = new Button { Content = TriskoLocalization.Text("Полный экран") };
        fullscreen.Click += (_, _) => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        var cad = new Button { Content = "Ctrl+Alt+Del" };
        cad.Click += (_, _) => { _screen.SendCtrlAltDelete(); _screen.Focus(); };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 12, Margin = new Thickness(12) };
        _status.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(fullscreen, 1); Grid.SetColumn(cad, 2); Grid.SetColumn(_status, 3);
        toolbar.Children.Add(disconnect); toolbar.Children.Add(fullscreen); toolbar.Children.Add(cad); toolbar.Children.Add(_status);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(toolbar);
        var viewport = new Border { Background = Brushes.Black, ClipToBounds = true,
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = _screen } };
        Grid.SetRow(viewport, 1); layout.Children.Add(viewport); Content = layout;
        Deactivated += (_, _) => _screen.ReleaseInput();
        Opened += (_, _) =>
        {
            var secret = password;
            password = string.Empty;
            _connectTask = ConnectAsync(host, port, secret);
        };
        Closing += (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true;
            _closing = true;
            _cancellation.Cancel();
            _ = CloseConnectionAsync();
        };
    }

    private async Task ConnectAsync(string host, int port, string password)
    {
        _status.Text = TriskoLocalization.Text("Подключение…");
        var authentication = new PasswordHandler(password);
        password = string.Empty;
        try
        {
            // Do not negotiate unauthenticated access when a server also offers security type None.
            var protocol = new DefaultImplementation(
                context => new ISecurityType[] { new VncAuthenticationSecurityType(context) },
                DefaultImplementation.GetDefaultMessageTypes, DefaultImplementation.GetDefaultEncodingTypes);
            var client = new VncClient(NullLoggerFactory.Instance, protocol);
            var connection = await client.ConnectAsync(new ConnectParameters
            {
                TransportParameters = new TcpTransportParameters { Host = host, Port = port },
                AuthenticationHandler = authentication,
                MaxReconnectAttempts = 0,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AllowSharedConnection = true,
                InitialRenderTarget = _screen
            }, _cancellation.Token);
            if (_closing)
            {
                await connection.CloseAsync(); connection.Dispose(); return;
            }
            _connection = connection;
            _screen.Connection = connection;
            connection.PropertyChanged += ConnectionChanged;
            UpdateStatus();
            _screen.Focus();
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = TriskoLocalization.Text("Подключение отменено"); }
        catch (Exception ex) { if (!_closing) { _status.Text = TriskoLocalization.Text("Ошибка: ") + ex.Message; _status.TextWrapping = TextWrapping.Wrap; } }
        finally { authentication.Clear(); }
    }

    private void ConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RfbConnection.ConnectionState))
            Dispatcher.UIThread.Post(UpdateStatus);
    }

    private void UpdateStatus()
    {
        if (!_closing) _status.Text = _connection?.ConnectionState == ConnectionState.Connected
            ? TriskoLocalization.Text("Подключено") : TriskoLocalization.Text("Соединение закрыто. Для повтора откройте подключение заново.");
    }

    private async Task CloseConnectionAsync()
    {
        try
        {
            _screen.ReleaseInput();
            // The connection may still be completing its handshake when the window is closed.
            // Wait for cancellation/cleanup before disposing its initial render target.
            if (_connectTask != null) await _connectTask;
            _screen.Connection = null;
            if (_connection != null)
            {
                _connection.PropertyChanged -= ConnectionChanged;
                await _connection.CloseAsync();
                _connection.Dispose();
                _connection = null;
            }
        }
        catch (Exception ex) { Logging.SaveLog("VNC close", ex); }
        finally { _screen.Dispose(); _cancellation.Dispose(); Close(); }
    }

    private sealed class PasswordHandler(string password) : IAuthenticationHandler
    {
        private string _password = password;
        public void Clear() => _password = string.Empty;
        public Task<TInput> ProvideAuthenticationInputAsync<TInput>(RfbConnection connection, ISecurityType securityType,
            IAuthenticationInputRequest<TInput> request) where TInput : class, IAuthenticationInput
        {
            if (typeof(TInput) != typeof(PasswordAuthenticationInput))
                throw new NotSupportedException(TriskoLocalization.Text("Этот способ авторизации VNC пока не поддерживается."));
            return Task.FromResult((TInput)(object)new PasswordAuthenticationInput(_password));
        }
    }
}
