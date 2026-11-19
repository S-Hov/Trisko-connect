using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;

namespace ServiceLib.Trisko;

public sealed record TriskoSoftEtherStatus(bool Installed, bool Connected, string? Address, bool Configured = false);

[SupportedOSPlatform("windows")]
public sealed class TriskoSoftEtherService
{
    private const string Account = "TriskoWorkNetwork";
    // Modern Windows requires a driver instance name from SoftEther's signed catalog.
    internal const string Adapter = "VPN126";
    private const int ProfileMissing = 36;
    private const int ProfileOffline = 37;
    private const string InstallerHash = "AB006331F1F646BC319585088239D55BC54ECDE5C269009268E9598307DA7A44";

    private static string SettingsPath => Utils.GetConfigPath("trisko-work-network.json");

    public async Task<TriskoSoftEtherStatus> GetStatusAsync(TriskoWorkNetworkOptions? options)
    {
        var command = FindClient();
        if (command is null) return new(false, false, null);
        if (options is null || FindAdapter() is not { } nic) return new(true, false, null);
        var settings = await ReadSettingsAsync();
        if (!await HasManagedProfileAsync(command, settings, nic)) return new(true, false, null);

        var address = GetConfiguredAddress(nic, options.Subnet);
        var result = await RunVpnAsync(command, "AccountStatusGet", Account);
        if (result is not (ProfileMissing or ProfileOffline)) EnsureCommandSuccess(result, "AccountStatusGet");
        var configured = settings?.Profile is { } profile && profile.UsesSameAccount(options) && result != ProfileMissing;
        return new(true, configured && result == 0 && address is not null && nic.OperationalStatus == OperationalStatus.Up,
            address, configured);
    }

    public async Task InstallAsync()
    {
        if (FindClient() is not null) return;
        var installer = Path.Combine(AppContext.BaseDirectory, "Tools", "SoftEther", "vpnclient-setup.exe");
        if (!File.Exists(installer))
            throw new IOException(TriskoLocalization.Text("Установщик SoftEther отсутствует. Установите SoftEther VPN Client или используйте полный комплект Trisko для Windows."));

        using var file = File.OpenRead(installer);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(InstallerHash, StringComparison.Ordinal))
            throw new IOException(TriskoLocalization.Text("Установщик SoftEther не прошёл проверку целостности."));
        var startInfo = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" };
        startInfo.ArgumentList.Add("/HIDESTARTCOMMAND:yes");
        startInfo.ArgumentList.Add("/DISABLEAUTOIMPORT:yes");
        using var process = Process.Start(startInfo) ?? throw new IOException(TriskoLocalization.Text("Не удалось запустить установщик SoftEther."));
        await process.WaitForExitAsync();
        if (FindClient() is null)
            throw new IOException(TriskoLocalization.Text("Установка не завершена. В установщике выберите SoftEther VPN Client."));
    }

    public async Task ConnectAsync(TriskoWorkNetworkOptions options, Guid deviceId, int proxyPort, string password)
    {
        if (proxyPort is < 1 or > 65535)
            throw new ArgumentException(TriskoLocalization.Text("Локальный порт прокси Trisko недоступен."));
        if (password.Length > 63 || password.Any(c => c is < ' ' or > '~' or '"'))
            throw new ArgumentException(TriskoLocalization.Text("Пароль SoftEther должен содержать до 63 печатных ASCII-символов без двойных кавычек."));

        var command = FindClient() ?? throw new IOException(TriskoLocalization.Text("Сначала установите SoftEther VPN Client."));
        var settings = await ReadSettingsAsync();
        var hasProfile = await HasManagedProfileAsync(command, settings, FindAdapter());
        var createProfile = !hasProfile;
        var accountChanged = settings?.Profile is not { } previous || !previous.UsesSameAccount(options);
        if ((createProfile || accountChanged) && string.IsNullOrEmpty(password))
            throw new ArgumentException(TriskoLocalization.Text("Введите пароль SoftEther для настройки подключения."));

        var subnet = options.Subnet;
        var nic = await EnsureAdapterAsync(command, settings);
        var address = GetConfiguredAddress(nic, subnet) ?? TriskoWorkNetwork.GetAddress(deviceId, subnet).ToString();
        CheckAddressConflict(nic, subnet);
        if (hasProfile) await DisconnectAsync(command);
        await ConfigureAddressAsync(nic, address, subnet);

        if (createProfile)
            await RequireAsync(command, "AccountCreate", Account, $"/SERVER:{options.Server}", $"/HUB:{options.Hub}",
                $"/USERNAME:{options.Username}", $"/NICNAME:{Adapter}");
        try
        {
            if (!createProfile && accountChanged)
            {
                // Invalidate the previous credentials before changing the destination of the account.
                await SaveSettingsAsync(new(nic.Id, null));
                await RequireAsync(command, "AccountSet", Account, $"/SERVER:{options.Server}", $"/HUB:{options.Hub}");
                await RequireAsync(command, "AccountUsernameSet", Account, $"/USERNAME:{options.Username}");
            }
            await RequireAsync(command, "AccountProxyHttp", Account, $"/SERVER:127.0.0.1:{proxyPort}");
            await RequireAsync(command, "AccountDetailSet", Account, "/MAXTCP:1", "/INTERVAL:1", "/TTL:0",
                "/HALF:no", "/BRIDGE:no", "/MONITOR:no", "/NOTRACK:yes", "/NOQOS:no");
            await RequireAsync(command, "AccountRetrySet", Account, "/NUM:0", "/INTERVAL:15");
            if (password.Length != 0)
            {
                // vpncmd reads the password from stdin, away from process arguments and logs.
                var result = await RunAsync(command, ["localhost", "/CLIENT"],
                    $"AccountPasswordSet {Account} /TYPE:standard /PASSWORD:\"{password}\"\r\nexit\r\n");
                if (result.ExitCode != 0)
                    throw new IOException(TriskoLocalization.Text("Не удалось сохранить пароль SoftEther."));
            }
            await SaveSettingsAsync(new(nic.Id, options));
        }
        catch
        {
            if (createProfile) await RunVpnAsync(command, "AccountDelete", Account);
            throw;
        }

        try
        {
            await RequireAsync(command, "AccountConnect", Account);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (await IsConnectedAsync(command) && FindAdapter() is { } activeAdapter &&
                    activeAdapter.Id == nic.Id && GetConfiguredAddress(activeAdapter, subnet) == address)
                {
                    var state = activeAdapter.GetIPProperties().UnicastAddresses
                        .Single(item => item.Address.ToString() == address).DuplicateAddressDetectionState;
                    if (state == DuplicateAddressDetectionState.Duplicate)
                    {
                        address = TriskoWorkNetwork.GetAddress(Guid.NewGuid(), subnet).ToString();
                        await ConfigureAddressAsync(activeAdapter, address, subnet);
                    }
                    else if (state == DuplicateAddressDetectionState.Preferred)
                        return;
                }
                await Task.Delay(350);
            }
            throw new IOException(TriskoLocalization.Text("SoftEther не установил сессию. Проверьте пароль рабочей сети и доступ к SoftEther через текущий VPN."));
        }
        catch
        {
            await DisconnectAsync(command);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (FindClient() is { } command && await HasManagedProfileAsync(command, await ReadSettingsAsync(), FindAdapter()))
            await DisconnectAsync(command);
    }

    private static async Task DisconnectAsync(string command)
    {
        var result = await RunVpnAsync(command, "AccountDisconnect", Account);
        if (result is not (ProfileMissing or ProfileOffline)) EnsureCommandSuccess(result, "AccountDisconnect");
    }

    private static async Task<bool> IsConnectedAsync(string command)
    {
        var result = await RunVpnAsync(command, "AccountStatusGet", Account);
        if (result is ProfileMissing or ProfileOffline) return false;
        EnsureCommandSuccess(result, "AccountStatusGet");
        return FindAdapter()?.OperationalStatus == OperationalStatus.Up;
    }

    private static async Task<NetworkInterface> EnsureAdapterAsync(string command, WorkSettings? settings)
    {
        if (FindAdapter() is { } existing)
        {
            if (!MatchesAdapter(settings, existing))
                throw new IOException(TriskoLocalization.Format("Адаптер SoftEther {0} уже существует. Он не принадлежит Trisko.", Adapter));
            return existing;
        }

        await RequireAsync(command, "NicCreate", Adapter);
        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (FindAdapter() is { } nic)
                {
                    await SaveSettingsAsync(new(nic.Id, null));
                    return nic;
                }
                await Task.Delay(250);
            }
            throw new IOException(TriskoLocalization.Text("Windows не обнаружила виртуальный адаптер SoftEther."));
        }
        catch
        {
            await RunVpnAsync(command, "NicDelete", Adapter);
            throw;
        }
    }

    private static bool MatchesAdapter(WorkSettings? settings, NetworkInterface nic) =>
        Guid.TryParse(settings?.AdapterId, out var saved) && Guid.TryParse(nic.Id, out var actual) && saved == actual;

    private static async Task<bool> HasManagedProfileAsync(string command, WorkSettings? settings, NetworkInterface? nic)
    {
        var result = await RunAsync(command, ["localhost", "/CLIENT", "/CSV", "/CMD", "AccountList"], captureOutput: true);
        EnsureCommandSuccess(result.ExitCode, "AccountList");
        return HasManagedProfile(result.Output, nic is not null && MatchesAdapter(settings, nic), settings?.Profile);
    }

    internal static bool HasManagedProfile(string csv, bool ownsAdapter, TriskoWorkNetworkOptions? saved)
    {
        using var parser = new TextFieldParser(new StringReader(csv));
        parser.SetDelimiters(",");
        var hasProfile = false;
        while (!parser.EndOfData)
        {
            if (parser.ReadFields() is not { Length: 5 } fields) continue;
            var isManaged = fields[0].Equals(Account, StringComparison.OrdinalIgnoreCase);
            var usesAdapter = fields[4].Equals(Adapter, StringComparison.OrdinalIgnoreCase);
            if (usesAdapter && !isManaged)
                throw new IOException(TriskoLocalization.Format("Адаптер SoftEther {0} назначен другому профилю.", Adapter));
            if (!isManaged) continue;
            if (!usesAdapter || !ownsAdapter || (saved is not null &&
                (!fields[3].Equals(saved.Hub, StringComparison.OrdinalIgnoreCase) ||
                !(fields[2].Equals(saved.Server, StringComparison.OrdinalIgnoreCase) ||
                  fields[2].StartsWith(saved.Server + " (", StringComparison.OrdinalIgnoreCase)))))
                throw new IOException(TriskoLocalization.Format("Профиль SoftEther {0} был изменён вне Trisko.", Account));
            hasProfile = true;
        }
        return hasProfile;
    }

    private static async Task ConfigureAddressAsync(NetworkInterface nic, string address, IPNetwork subnet)
    {
        if (GetConfiguredAddress(nic, subnet) == address) return;
        var index = nic.GetIPProperties().GetIPv4Properties().Index;
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var arg in new[] { "interface", "ipv4", "set", "address", $"name={index}", "source=static",
            $"address={address}", $"mask={TriskoWorkNetwork.GetMask(subnet)}", "gateway=none", "store=persistent" })
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new IOException(TriskoLocalization.Text("Не удалось настроить сетевой адаптер Windows."));
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new IOException(TriskoLocalization.Text("Windows не назначила адрес виртуальному адаптеру SoftEther."));
    }

    public static NetworkInterface? FindAdapter() => NetworkInterface.GetAllNetworkInterfaces()
        .SingleOrDefault(nic => nic.Description.Equals($"VPN Client Adapter - {Adapter}", StringComparison.OrdinalIgnoreCase));

    private static string? GetConfiguredAddress(NetworkInterface? nic, IPNetwork subnet) => nic?.GetIPProperties().UnicastAddresses
        .FirstOrDefault(item => item.PrefixLength == subnet.PrefixLength && subnet.Contains(item.Address) &&
            !item.Address.Equals(subnet.BaseAddress) && !item.Address.Equals(TriskoWorkNetwork.GetBroadcast(subnet)))
        ?.Address.ToString();

    private static void CheckAddressConflict(NetworkInterface managed, IPNetwork subnet)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.Id == managed.Id || nic.OperationalStatus != OperationalStatus.Up) continue;
            var conflict = nic.GetIPProperties().UnicastAddresses.Any(item =>
                item.Address.AddressFamily == AddressFamily.InterNetwork &&
                (subnet.Contains(item.Address) || SharesPrefix(item.Address, subnet.BaseAddress, item.PrefixLength)));
            if (conflict)
                throw new IOException(TriskoLocalization.Format("Подсеть рабочей сети уже используется адаптером «{0}».", nic.Name));
        }
    }

    private static bool SharesPrefix(IPAddress left, IPAddress right, int prefix)
    {
        if (prefix == 0) return false;
        var leftValue = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(left.GetAddressBytes());
        var rightValue = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(right.GetAddressBytes());
        return (leftValue >> (32 - prefix)) == (rightValue >> (32 - prefix));
    }

    public static void CheckPeerRoute(NetworkInterface nic, IPAddress peer)
    {
        var destination = BitConverter.ToUInt32(peer.GetAddressBytes());
        if (GetBestInterface(destination, out var index) != 0 || index != nic.GetIPProperties().GetIPv4Properties().Index)
            throw new IOException(TriskoLocalization.Text("Windows направляет трафик к рабочему ПК через другой адаптер. Переподключите VPN и проверьте исключения TUN."));
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetBestInterface(uint destination, out uint index);

    private static async Task<WorkSettings?> ReadSettingsAsync()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<WorkSettings>(await File.ReadAllTextAsync(SettingsPath)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task SaveSettingsAsync(WorkSettings settings) =>
        TriskoConfigurationFileIO.WriteTextAtomicallyAsync(SettingsPath, JsonSerializer.Serialize(settings));

    private sealed record WorkSettings(string AdapterId, TriskoWorkNetworkOptions? Profile);

    private static string? FindClient()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SEVPNCLIENT");
        var image = (key?.GetValue("ImagePath") as string)?.Trim();
        if (string.IsNullOrEmpty(image)) return null;
        var match = Regex.Match(image, @"^""?(.+?\.exe)""?(?:\s|$)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var directory = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(match.Groups[1].Value));
        if (directory is null) return null;
        var command = Path.Combine(directory, "vpncmd.exe");
        return File.Exists(command) ? command : null;
    }

    private static async Task RequireAsync(string command, params string[] args)
        => EnsureCommandSuccess(await RunVpnAsync(command, args), args[0]);

    private static void EnsureCommandSuccess(int result, string operation)
    {
        if (result != 0) throw new IOException(TriskoLocalization.Format("SoftEther не смог выполнить операцию «{0}» (код {1}).", operation, result));
    }

    private static async Task<int> RunVpnAsync(string command, params string[] args)
        => (await RunAsync(command, ["localhost", "/CLIENT", "/CMD", .. args])).ExitCode;

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string command, string[] args, string? input = null, bool captureOutput = false)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new IOException(TriskoLocalization.Text("Не удалось запустить vpncmd."));
        var seconds = args.Contains("NicCreate", StringComparer.Ordinal) ? 90 : 15;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var captured = captureOutput ? process.StandardOutput.ReadToEndAsync(timeout.Token) : null;
        Task output = captured ?? (Task)process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
            return (process.ExitCode, captured is null ? "" : await captured);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill();
            throw new IOException(TriskoLocalization.Text("SoftEther VPN Client не ответил. Проверьте, запущена ли его служба Windows."));
        }
    }
}
