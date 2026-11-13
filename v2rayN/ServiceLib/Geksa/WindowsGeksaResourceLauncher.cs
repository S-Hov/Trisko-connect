using System.Runtime.InteropServices;

namespace ServiceLib.Geksa;

public sealed class WindowsGeksaResourceLauncher : IGeksaResourceLauncher
{
    public Task LaunchAsync(
        GeksaResourceProfile resource,
        GeksaVpnSnapshot vpnSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        GeksaCorporateConfigurationCodec.ValidateResourceProfile(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Корпоративные ресурсы поддерживаются только в Windows."));
        }

        if (resource.RequiresVpn && vpnSnapshot.State != GeksaVpnState.Connected)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Сначала подключитесь к корпоративному VPN."));
        }

        return GeksaCorporateConfigurationCodec.ParseResourceType(resource.Type) switch
        {
            GeksaResourceType.Rdp => LaunchRdpAsync(resource, cancellationToken),
            GeksaResourceType.Ssh => LaunchSshAsync(resource, cancellationToken),
            GeksaResourceType.Smb => LaunchSmbAsync(resource, cancellationToken),
            _ => throw new GeksaConfigurationException(TriskoLocalization.Text("Тип ресурса не поддерживается."))
        };
    }

    private static async Task LaunchRdpAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
        if (!File.Exists(executable))
        {
            throw new GeksaConfigurationException(TriskoLocalization.Text("Системный клиент удалённого рабочего стола не найден."));
        }

        // Автоматически синхронизируем учётные данные Windows RDP:
        // архивируем старого пользователя в защищённое хранилище Trisko и восстанавливаем пароль нужного пользователя,
        // чтобы не приходилось повторно вводить пароль при смене рабочего стола.
        PrepareRdpCredentials(resource.Host, resource.Port, resource.UserName);

        var directory = Utils.GetConfigPath("gek-sa-rdp");
        Directory.CreateDirectory(directory);
        var configurationPath = Path.Combine(directory, resource.Id + ".rdp");
        var fullAddress = $"{resource.Host}:{resource.Port}";
        var lines = new[]
        {
            $"full address:s:{fullAddress}",
            $"username:s:{resource.UserName}",
            $"screen mode id:i:{(resource.FullScreen ? 2 : 1)}",
            $"use multimon:i:{(resource.MultiMonitor ? 1 : 0)}",
            "authentication level:i:2",
            "enablecredsspsupport:i:1",
            "prompt for credentials on client:i:1",
            "promptcredentialonce:i:0",
            "disableconnectionsharing:i:1",
            "redirectclipboard:i:1",
            "redirectprinters:i:0",
            "drivestoredirect:s:",
            string.Empty
        };
        await File.WriteAllTextAsync(
            configurationPath,
            string.Join(Environment.NewLine, lines),
            Encoding.Unicode,
            cancellationToken);

        StartProcess(executable, [configurationPath], useShellExecute: true);
    }

    private static Task LaunchSshAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ssh = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");
        if (!File.Exists(ssh))
        {
            throw new GeksaConfigurationException(
                TriskoLocalization.Text("В Windows не установлен дополнительный компонент OpenSSH Client."));
        }

        var destination = $"{resource.UserName}@{resource.Host}";
        try
        {
            StartProcess(
                "wt.exe",
                ["new-tab", "--title", resource.DisplayName, ssh, "-p", resource.Port.ToString(), destination],
                useShellExecute: false);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
        {
            var command = $"& '{EscapePowerShellLiteral(ssh)}' -p {resource.Port} " +
                $"'{EscapePowerShellLiteral(destination)}'";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            StartProcess("powershell.exe", ["-NoExit", "-EncodedCommand", encoded], useShellExecute: true);
        }

        return Task.CompletedTask;
    }

    private static Task LaunchSmbAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uncPath = GeksaSmbPath.Create(resource.Host, resource.Path);

        // Trisko is normally launched elevated for TUN. Starting explorer.exe as a
        // child of that process is unreliable: the already running non-elevated
        // Windows Explorer can ignore the requested UNC path. Let the Windows
        // shell open the folder itself so it is handed to the user's Explorer.
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uncPath,
                UseShellExecute = true,
                Verb = "open",
                ErrorDialog = true
            });
        }
        catch (Win32Exception exception)
        {
            Logging.SaveLog(nameof(WindowsGeksaResourceLauncher), exception);
            throw CreateSmbLaunchException(uncPath, exception);
        }

        return Task.CompletedTask;
    }

    private static GeksaConfigurationException CreateSmbLaunchException(
        string uncPath,
        Win32Exception exception)
    {
        var message = exception.NativeErrorCode switch
        {
            5 => TriskoLocalization.Format("Нет доступа к сетевой папке {0}. Проверьте учётные данные Windows.", uncPath),
            53 or 67 => TriskoLocalization.Format("Сетевая папка {0} не найдена. Проверьте адрес и подключение к VPN.", uncPath),
            1219 => TriskoLocalization.Text("Windows уже подключена к этому серверу с другими учётными данными."),
            1231 => TriskoLocalization.Format("Сервер сетевой папки {0} недоступен по сети.", uncPath),
            _ => TriskoLocalization.Format("Не удалось открыть сетевую папку {0}: {1}", uncPath, exception.Message)
        };
        return new GeksaConfigurationException(message);
    }

    private static void StartProcess(
        string executable,
        IReadOnlyList<string> arguments,
        bool useShellExecute)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = useShellExecute
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new GeksaConfigurationException(TriskoLocalization.Text("Windows не подтвердил запуск ресурса."));
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    #region Windows RDP Credential Helpers & Vault

    public static string GetVaultTargetName(string host, string userName)
    {
        var cleanHost = host.Trim().ToLowerInvariant();
        var cleanUser = ExtractUsername(userName).ToLowerInvariant();
        return $"TriskoConnect/RdpVault/{cleanHost}/{cleanUser}";
    }

    private static void PrepareRdpCredentials(string host, int port, string? expectedUserName)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(expectedUserName) || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"TERMSRV/{host}",
                $"TERMSRV/{host}:{port}",
                $"TERMSRV/{host.ToLowerInvariant()}",
                $"TERMSRV/{host.ToUpperInvariant()}"
            };

            bool activeMatchesExpected = false;

            foreach (var target in targets)
            {
                // Проверяем Generic (1) и Domain Password (2)
                foreach (var type in new[] { 1, 2 })
                {
                    if (CredRead(target, type, 0, out var credPtr))
                    {
                        try
                        {
                            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
                            var savedUserName = Marshal.PtrToStringUni(cred.UserName);

                            if (UserNamesMatch(savedUserName, expectedUserName))
                            {
                                activeMatchesExpected = true;
                                if (!string.IsNullOrWhiteSpace(savedUserName))
                                {
                                    SaveCredentialToVault(host, savedUserName, credPtr);
                                }
                            }
                            else
                            {
                                // Конфликтующий пользователь: архивируем его пароль в хранилище Geksa, затем удаляем из TERMSRV
                                if (!string.IsNullOrWhiteSpace(savedUserName))
                                {
                                    SaveCredentialToVault(host, savedUserName, credPtr);
                                }
                                CredDelete(target, type, 0);
                            }
                        }
                        finally
                        {
                            CredFree(credPtr);
                        }
                    }
                }
            }

            // Если активный логин не совпал с требуемым, восстанавливаем сохранённый пароль из хранилища Geksa
            if (!activeMatchesExpected)
            {
                RestoreCredentialFromVault(host, port, expectedUserName);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(PrepareRdpCredentials), ex);
        }
    }

    private static void SaveCredentialToVault(string host, string userName, IntPtr sourceCredPtr)
    {
        try
        {
            var source = Marshal.PtrToStructure<NativeCredential>(sourceCredPtr);
            if (source.CredentialBlobSize == 0 || source.CredentialBlob == IntPtr.Zero)
            {
                return;
            }

            var vaultTarget = GetVaultTargetName(host, userName);
            var targetNamePtr = Marshal.StringToHGlobalUni(vaultTarget);
            var commentPtr = Marshal.StringToHGlobalUni("Trisko RDP Credential Vault");

            try
            {
                var vaultCred = new NativeCredential
                {
                    Flags = 0,
                    Type = 1, // CRED_TYPE_GENERIC
                    TargetName = targetNamePtr,
                    Comment = commentPtr,
                    LastWritten = 0,
                    CredentialBlobSize = source.CredentialBlobSize,
                    CredentialBlob = source.CredentialBlob,
                    Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = IntPtr.Zero,
                    UserName = source.UserName
                };

                CredWrite(ref vaultCred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(targetNamePtr);
                Marshal.FreeHGlobal(commentPtr);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(SaveCredentialToVault), ex);
        }
    }

    private static bool RestoreCredentialFromVault(string host, int port, string expectedUserName)
    {
        var vaultTarget = GetVaultTargetName(host, expectedUserName);
        if (!CredRead(vaultTarget, 1, 0, out var vaultCredPtr))
        {
            return false;
        }

        try
        {
            var vaultCred = Marshal.PtrToStructure<NativeCredential>(vaultCredPtr);
            if (vaultCred.CredentialBlobSize == 0 || vaultCred.CredentialBlob == IntPtr.Zero)
            {
                return false;
            }

            var targetsToSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"TERMSRV/{host}"
            };
            if (port > 0 && port != 3389)
            {
                targetsToSet.Add($"TERMSRV/{host}:{port}");
            }

            foreach (var target in targetsToSet)
            {
                var targetNamePtr = Marshal.StringToHGlobalUni(target);
                var commentPtr = Marshal.StringToHGlobalUni("Trisko RDP Active Credential");
                try
                {
                    var activeCred = new NativeCredential
                    {
                        Flags = 0,
                        Type = 1, // CRED_TYPE_GENERIC
                        TargetName = targetNamePtr,
                        Comment = commentPtr,
                        LastWritten = 0,
                        CredentialBlobSize = vaultCred.CredentialBlobSize,
                        CredentialBlob = vaultCred.CredentialBlob,
                        Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                        AttributeCount = 0,
                        Attributes = IntPtr.Zero,
                        TargetAlias = IntPtr.Zero,
                        UserName = vaultCred.UserName
                    };

                    CredWrite(ref activeCred, 0);
                }
                finally
                {
                    Marshal.FreeHGlobal(targetNamePtr);
                    Marshal.FreeHGlobal(commentPtr);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RestoreCredentialFromVault), ex);
            return false;
        }
        finally
        {
            CredFree(vaultCredPtr);
        }
    }

    public static bool UserNamesMatch(string? existingUser, string? expectedUser)
    {
        if (string.IsNullOrWhiteSpace(existingUser) || string.IsNullOrWhiteSpace(expectedUser))
        {
            return false;
        }

        if (string.Equals(existingUser.Trim(), expectedUser.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var cleanExisting = ExtractUsername(existingUser);
        var cleanExpected = ExtractUsername(expectedUser);

        return string.Equals(cleanExisting, cleanExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractUsername(string raw)
    {
        var s = raw.Trim();
        var slashIndex = s.LastIndexOf('\\');
        if (slashIndex >= 0 && slashIndex < s.Length - 1)
        {
            s = s[(slashIndex + 1)..];
        }
        var atIndex = s.IndexOf('@');
        if (atIndex > 0)
        {
            s = s[..atIndex];
        }
        return s;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, int type, int flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    #endregion
}
