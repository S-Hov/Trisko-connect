namespace ServiceLib.Geksa;

public sealed class WindowsGeksaSmbDriveService : IGeksaSmbDriveService
{
    private const string UserSessionHelperArgument = "--geksa-smb-user-session";
    private const string StatusOperation = "status";
    private const string ConnectOperation = "connect";
    private const string DisconnectOperation = "disconnect";
    private const string UserSessionMutexName = @"Local\TriskoConnect-Smb";

    private const int NoError = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorCancelled = 1223;
    private const int ErrorConnectionUnavailable = 1201;
    private const int ErrorNotConnected = 2250;
    private const int ConnectUpdateProfile = 0x00000001;
    private const int ConnectInteractive = 0x00000008;
    private const int ResourceTypeDisk = 0x00000001;

    private static readonly JsonSerializerOptions HelperJsonOptions = new(JsonSerializerDefaults.Web);

    public Task<GeksaSmbDriveStatus> GetStatusAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default)
    {
        ValidateResource(resource);
        return ExecuteInExplorerSessionAsync(StatusOperation, resource, cancellationToken);
    }

    public Task<GeksaSmbDriveStatus> ConnectAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default)
    {
        ValidateResource(resource);
        return ExecuteInExplorerSessionAsync(ConnectOperation, resource, cancellationToken);
    }

    public async Task DisconnectAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default)
    {
        ValidateResource(resource);
        _ = await ExecuteInExplorerSessionAsync(DisconnectOperation, resource, cancellationToken);
    }

    public async Task OpenAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default)
    {
        ValidateResource(resource);
        cancellationToken.ThrowIfCancellationRequested();
        var status = await GetStatusAsync(resource, cancellationToken);
        if (status.State != GeksaSmbDriveState.Connected || status.DriveLetter.IsNullOrEmpty())
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Сначала подключите сетевой диск."));
        }

        try
        {
            await WindowsExplorerShell.OpenFolderAsync(
                $"{status.DriveLetter}\\",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logging.SaveLog(nameof(WindowsGeksaSmbDriveService), exception);
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Не удалось открыть сетевой диск {0}: {1}", status.DriveLetter, exception.Message));
        }
    }

    /// <summary>
    /// Handles the private command used by the elevated application to run SMB
    /// operations in the unelevated Explorer logon session.
    /// </summary>
    public static bool TryRunUserSessionHelper(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 2 ||
            !string.Equals(args[0], UserSessionHelperArgument, StringComparison.Ordinal))
        {
            return false;
        }

        exitCode = RunUserSessionHelper(args[1]);
        return true;
    }

    private static int RunUserSessionHelper(string requestPath)
    {
        HelperRequest? request = null;
        try
        {
            var requestJson = File.ReadAllText(requestPath, Encoding.UTF8);
            request = JsonSerializer.Deserialize<HelperRequest>(requestJson, HelperJsonOptions)
                ?? throw new InvalidDataException(TriskoLocalization.Text("Пустой запрос SMB-помощника."));
            ValidateResource(request.Resource);

            var status = ExecuteHelperOperation(request);
            WriteHelperResponse(request.ResponsePath, new HelperResponse(status, null));
            return 0;
        }
        catch (GeksaConfigurationException exception)
        {
            if (request is not null)
            {
                WriteHelperResponse(request.ResponsePath, new HelperResponse(null, exception.UserMessage));
            }
            return 1;
        }
        catch (Exception exception)
        {
            if (request is not null)
            {
                WriteHelperResponse(
                    request.ResponsePath,
                    new HelperResponse(
                        null,
                        TriskoLocalization.Format("Не удалось выполнить операцию с сетевым диском: {0}", exception.Message)));
            }
            return 2;
        }
    }

    private static GeksaSmbDriveStatus ExecuteHelperOperation(HelperRequest request)
    {
        using var operationMutex = new Mutex(false, UserSessionMutexName);
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = operationMutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
            {
                throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Другая операция с сетевым диском ещё выполняется. Повторите попытку."));
            }

            return request.Operation switch
            {
                StatusOperation => GetStatus(request.Resource),
                ConnectOperation => Connect(request.Resource),
                DisconnectOperation => Disconnect(request.Resource),
                _ => throw new InvalidDataException(TriskoLocalization.Text("Неизвестная операция SMB-помощника."))
            };
        }
        finally
        {
            if (lockTaken)
            {
                operationMutex.ReleaseMutex();
            }
        }
    }

    private static async Task<GeksaSmbDriveStatus> ExecuteInExplorerSessionAsync(
        string operation,
        GeksaResourceProfile resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Сетевые диски поддерживаются только в Windows."));
        }

        var executable = Utils.GetExePath();
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Не удалось найти исполняемый файл для подключения сетевого диска."));
        }

        var requestId = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(
            Path.GetTempPath(),
            $"geksa-smb-{requestId}.request.json");
        var responsePath = Path.Combine(
            Path.GetTempPath(),
            $"geksa-smb-{requestId}.response.json");
        var temporaryResponsePath = responsePath + ".tmp";

        try
        {
            var request = new HelperRequest(operation, resource, responsePath);
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request, HelperJsonOptions),
                Encoding.UTF8,
                cancellationToken);

            var arguments = $"{UserSessionHelperArgument} {QuoteCommandLineArgument(requestPath)}";
            await WindowsExplorerShell.LaunchProcessAsync(
                executable,
                arguments,
                AppContext.BaseDirectory,
                cancellationToken);

            var timeoutAt = DateTimeOffset.UtcNow.AddMinutes(3);
            while (!File.Exists(responsePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow >= timeoutAt)
                {
                    throw new GeksaConfigurationException(
                        TriskoLocalization.Text("Пользовательский помощник Windows не ответил. ") +
                        TriskoLocalization.Text("Проверьте, не скрыто ли окно входа в сетевую папку за другими окнами."));
                }
                await Task.Delay(100, cancellationToken);
            }

            var responseJson = await File.ReadAllTextAsync(
                responsePath,
                Encoding.UTF8,
                cancellationToken);
            var response = JsonSerializer.Deserialize<HelperResponse>(responseJson, HelperJsonOptions)
                ?? throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Windows вернула пустой ответ при подключении сетевого диска."));
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                throw new GeksaConfigurationException(response.ErrorMessage);
            }

            return response.Status
                ?? throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Windows не вернула состояние сетевого диска."));
        }
        catch (GeksaConfigurationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logging.SaveLog(nameof(WindowsGeksaSmbDriveService), exception);
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Не удалось выполнить операцию с диском в пользовательском сеансе Windows."));
        }
        finally
        {
            TryDeleteFile(requestPath);
            TryDeleteFile(responsePath);
            TryDeleteFile(temporaryResponsePath);
        }
    }

    private static GeksaSmbDriveStatus Connect(GeksaResourceProfile resource)
    {
        var current = GetStatus(resource);
        if (current.State == GeksaSmbDriveState.Connected)
        {
            return current;
        }
        if (current.State == GeksaSmbDriveState.DriveLetterConflict)
        {
            throw new GeksaConfigurationException(current.Message!);
        }

        var driveLetter = NormalizeConfiguredDriveLetter(resource.DriveLetter)
            ?? FindFreeDriveLetter();
        var remotePath = GeksaSmbPath.Create(resource.Host, resource.Path);
        var networkResource = new NativeNetworkResource
        {
            Type = ResourceTypeDisk,
            LocalName = driveLetter,
            RemoteName = remotePath
        };

        // The helper already runs in Explorer's unelevated logon session, so
        // use the same connection primitive as the proven legacy client. The
        // session boundary and the network connection API are separate concerns.
        var result = WNetAddConnection2(
            ref networkResource,
            password: null,
            userName: null,
            ConnectUpdateProfile | ConnectInteractive);
        if (result != NoError)
        {
            throw CreateConnectionException(resource, driveLetter, result);
        }

        var connected = GetStatusForDrive(driveLetter, remotePath);
        if (connected.State != GeksaSmbDriveState.Connected)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Windows не подтвердил подключение сетевого диска {0}.", driveLetter));
        }
        return connected;
    }

    private static GeksaSmbDriveStatus Disconnect(GeksaResourceProfile resource)
    {
        var status = GetStatus(resource);
        if (status.State == GeksaSmbDriveState.Disconnected)
        {
            return status;
        }
        if (status.State == GeksaSmbDriveState.DriveLetterConflict)
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("Программа не будет отключать диск, занятый другим сетевым ресурсом."));
        }
        if (status.DriveLetter.IsNullOrEmpty())
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось определить букву сетевого диска."));
        }

        var result = WNetCancelConnection2(
            status.DriveLetter,
            ConnectUpdateProfile,
            force: false);
        if (result is not (NoError or ErrorNotConnected))
        {
            if (result == 2401)
            {
                throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Сетевой диск используется. Закройте открытые файлы и повторите отключение."));
            }

            throw new GeksaConfigurationException(
                TriskoLocalization.Format("Не удалось отключить сетевой диск {0}: ", status.DriveLetter) +
                new Win32Exception(result).Message);
        }

        return new GeksaSmbDriveStatus(
            GeksaSmbDriveState.Disconnected,
            status.DriveLetter);
    }

    private static GeksaSmbDriveStatus GetStatus(GeksaResourceProfile resource)
    {
        var remotePath = GeksaSmbPath.Create(resource.Host, resource.Path);
        var configuredDrive = NormalizeConfiguredDriveLetter(resource.DriveLetter);
        if (configuredDrive is not null)
        {
            return GetStatusForDrive(configuredDrive, remotePath);
        }

        foreach (var driveLetter in EnumerateCandidateDriveLetters())
        {
            var currentRemote = TryGetRemotePath(driveLetter, out var errorCode);
            if (errorCode == NoError && PathsEqual(currentRemote!, remotePath))
            {
                return new GeksaSmbDriveStatus(
                    GeksaSmbDriveState.Connected,
                    driveLetter,
                    currentRemote);
            }
        }

        return new GeksaSmbDriveStatus(GeksaSmbDriveState.Disconnected);
    }

    private static GeksaSmbDriveStatus GetStatusForDrive(string driveLetter, string expectedRemotePath)
    {
        var currentRemote = TryGetRemotePath(driveLetter, out var errorCode);
        if (errorCode is ErrorNotConnected or ErrorConnectionUnavailable)
        {
            return new GeksaSmbDriveStatus(GeksaSmbDriveState.Disconnected, driveLetter);
        }
        if (errorCode != NoError)
        {
            return new GeksaSmbDriveStatus(
                GeksaSmbDriveState.Error,
                driveLetter,
                Message: new Win32Exception(errorCode).Message);
        }
        if (!PathsEqual(currentRemote!, expectedRemotePath))
        {
            return new GeksaSmbDriveStatus(
                GeksaSmbDriveState.DriveLetterConflict,
                driveLetter,
                currentRemote,
                TriskoLocalization.Format("Диск {0} уже подключён к {1}. Выберите другую букву.", driveLetter, currentRemote));
        }

        return new GeksaSmbDriveStatus(
            GeksaSmbDriveState.Connected,
            driveLetter,
            currentRemote);
    }

    private static string FindFreeDriveLetter()
    {
        var logicalDrives = GetLogicalDrives();
        foreach (var driveLetter in EnumerateCandidateDriveLetters())
        {
            var driveIndex = char.ToUpperInvariant(driveLetter[0]) - 'A';
            var hasLogicalDrive = (logicalDrives & (1u << driveIndex)) != 0;
            _ = TryGetRemotePath(driveLetter, out var errorCode);
            if (!hasLogicalDrive && errorCode == ErrorNotConnected)
            {
                return driveLetter;
            }
        }

        throw new GeksaConfigurationException(
            TriskoLocalization.Text("Нет свободной буквы для сетевого диска в диапазоне D:–Z:. Освободите букву или задайте её вручную."));
    }

    private static IEnumerable<string> EnumerateCandidateDriveLetters()
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            yield return $"{letter}:";
        }
    }

    private static string? TryGetRemotePath(string driveLetter, out int errorCode)
    {
        var capacity = 512;
        while (true)
        {
            var remoteName = new StringBuilder(capacity);
            var length = remoteName.Capacity;
            errorCode = WNetGetConnection(driveLetter, remoteName, ref length);
            if (errorCode == NoError)
            {
                return remoteName.ToString();
            }
            if (errorCode != ErrorMoreData)
            {
                return null;
            }
            capacity = Math.Max(length, capacity * 2);
        }
    }

    private static void ValidateResource(GeksaResourceProfile resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!OperatingSystem.IsWindows())
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Сетевые диски поддерживаются только в Windows."));
        }
        GeksaCorporateConfigurationCodec.ValidateResourceProfile(resource);
        if (GeksaCorporateConfigurationCodec.ParseResourceType(resource.Type) != GeksaResourceType.Smb)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Ресурс не является SMB-хранилищем."));
        }
    }

    private static string? NormalizeConfiguredDriveLetter(string? driveLetter) =>
        string.IsNullOrWhiteSpace(driveLetter)
            ? null
            : driveLetter.Trim().ToUpperInvariant();

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd('\\'),
            right.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static GeksaConfigurationException CreateConnectionException(
        GeksaResourceProfile resource,
        string driveLetter,
        int errorCode)
    {
        var remotePath = GeksaSmbPath.Create(resource.Host, resource.Path);
        var message = errorCode switch
        {
            ErrorCancelled => TriskoLocalization.Text("Вход в сетевую папку отменён."),
            5 or 86 or 1326 or 2202 =>
                TriskoLocalization.Text("Сервер отклонил учётные данные Windows. Проверьте имя пользователя и пароль."),
            53 or 67 =>
                TriskoLocalization.Format("Сетевая папка {0} не найдена. Проверьте адрес и подключение к VPN.", remotePath),
            85 =>
                TriskoLocalization.Format("Диск {0} уже используется. Выберите другую букву.", driveLetter),
            1219 =>
                TriskoLocalization.Text("Windows уже подключена к этому серверу с другими учётными данными. ") +
                TriskoLocalization.Text("Отключите существующее подключение к серверу и повторите попытку."),
            1231 =>
                TriskoLocalization.Format("Сервер сетевой папки {0} недоступен по сети.", remotePath),
            _ =>
                TriskoLocalization.Format("Не удалось подключить сетевой диск {0}: ", driveLetter) +
                new Win32Exception(errorCode).Message
        };
        return new GeksaConfigurationException(message);
    }

    private static string QuoteCommandLineArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void WriteHelperResponse(string responsePath, HelperResponse response)
    {
        var temporaryPath = responsePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(response, HelperJsonOptions),
            Encoding.UTF8);
        File.Move(temporaryPath, responsePath, overwrite: true);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record HelperRequest(
        string Operation,
        GeksaResourceProfile Resource,
        string ResponsePath);

    private sealed record HelperResponse(
        GeksaSmbDriveStatus? Status,
        string? ErrorMessage);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeNetworkResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? LocalName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? RemoteName;

        public IntPtr Comment;
        public IntPtr Provider;
    }

    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NativeNetworkResource networkResource,
        string? password,
        string? userName,
        int flags);

    [DllImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(
        string name,
        int flags,
        [MarshalAs(UnmanagedType.Bool)] bool force);

    [DllImport("mpr.dll", EntryPoint = "WNetGetConnectionW", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(
        string localName,
        StringBuilder remoteName,
        ref int length);

    [DllImport("kernel32.dll")]
    private static extern uint GetLogicalDrives();

}
