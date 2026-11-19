using System.ComponentModel;
using MarcusW.VncClient;
using MarcusW.VncClient.Protocol.Implementation;
using MarcusW.VncClient.Protocol.Implementation.MessageTypes.Outgoing;
using MarcusW.VncClient.Protocol.Implementation.SecurityTypes;
using MarcusW.VncClient.Protocol.Implementation.Services.Transports;
using MarcusW.VncClient.Protocol.SecurityTypes;
using MarcusW.VncClient.Security;
using MarcusW.VncClient.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using v2rayN.Desktop.Trisko.Vnc;

namespace v2rayN.Desktop.Trisko;

public sealed class TriskoVncSessionWindow : Window
{
    private readonly VncView _screen = new();
    private readonly TextBlock _status = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TriskoNotificationCenter? _notifications;
    private RfbConnection? _connection;
    private Task? _connectTask;
    private bool _closing;
    private bool _reportedInterruption;

    public event Action<string>? Authenticated;
    public event Action<string>? PasswordRejected;

    public TriskoVncSessionWindow(string host, int port, string password, TriskoNotificationCenter? notifications)
    {
        _notifications = notifications;
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
        if (notifications is not null)
        {
            var messages = new TriskoNotificationsView
            {
                DataContext = notifications,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(12),
                MaxHeight = 300
            };
            Grid.SetRow(messages, 1);
            layout.Children.Add(messages);
        }
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
                RenderFlags = RenderFlags.UpdateByRectangle,
                JpegQualityLevel = 85,
                JpegSubsamplingLevel = JpegSubsamplingLevel.None,
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
            Authenticated?.Invoke(password);
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = TriskoLocalization.Text("Подключение отменено"); }
        catch (Exception ex)
        {
            if (!_closing)
            {
                var message = ex.Message;
                for (Exception? cause = ex; cause != null; cause = cause.InnerException)
                    if (cause is MarcusW.VncClient.Protocol.HandshakeFailedException && cause.Message.StartsWith("Authentication failed", StringComparison.Ordinal))
                    {
                        PasswordRejected?.Invoke(password);
                        message = TriskoLocalization.Text("Сервер отклонил пароль VNC. Подключитесь снова и проверьте пароль.");
                        break;
                    }
                _status.Text = message;
                _notifications?.Show(TriskoLocalization.Text("Не удалось подключиться по VNC"), message, TriskoNotificationKind.Error, ex.ToString());
            }
        }
        finally { authentication.Clear(); password = string.Empty; }

        if (_connection is not { } active) return;

        // UltraVNC can start by sending desktop changes only while screen capture warms up.
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), _cancellation.Token);
            if (active.ConnectionState == ConnectionState.Connected)
                active.EnqueueMessage(new FramebufferUpdateRequestMessage(false,
                    new MarcusW.VncClient.Rectangle(MarcusW.VncClient.Position.Origin, active.RemoteFramebufferSize)), _cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (active.ConnectionState != ConnectionState.Connected) { }
    }

    private void ConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RfbConnection.ConnectionState) or nameof(RfbConnection.UsedEncodingTypes))
            Dispatcher.UIThread.Post(UpdateStatus);
    }

    private void UpdateStatus()
    {
        if (!_closing && !_reportedInterruption && _connection is { ConnectionState: ConnectionState.Closed, InterruptionCause: { } error })
        {
            _reportedInterruption = true;
            _notifications?.Show(TriskoLocalization.Text("Соединение VNC прервано"), error.Message, TriskoNotificationKind.Error, error.ToString());
        }
        if (!_closing) _status.Text = _connection?.ConnectionState == ConnectionState.Connected
            ? DescribeConnection(_connection)
            : TriskoLocalization.Text("Соединение закрыто. Для повтора откройте подключение заново.");
    }

    private static string DescribeConnection(RfbConnection connection)
    {
        var encodings = string.Join(", ", connection.UsedEncodingTypes
            .OfType<MarcusW.VncClient.Protocol.EncodingTypes.IFrameEncodingType>().Select(encoding => encoding.Name));
        return string.IsNullOrEmpty(encodings) ? TriskoLocalization.Text("Подключено")
            : TriskoLocalization.Format("Подключено · {0}", encodings);
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
