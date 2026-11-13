using Microsoft.Win32;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Runtime.InteropServices;

namespace ServiceLib.Geksa;

public sealed record GeksaVncServerStatus(bool Installed, bool Running, GeksaVncServerSettings Settings);

/// <summary>Manages only the separate GeksaVnc service, never an existing UltraVNC installation.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GeksaVncServerService
{
    public const string ServiceName = "GeksaVnc";
    private const string FirewallRule = "Trisko connect VNC";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly IReadOnlyDictionary<string, string> BundleHashes = new Dictionary<string, string>
    {
        ["winvnc.exe"] = "9EBD1EC90BA1274D4A183F5BF6588374700052DA907FA764A2AE7072E8AE19FE",
        ["vnchooks.dll"] = "8EB6F0F2D25F3D9FA075C311C6AD6F4D18CD4309D497F0D2A0E60B84F1FB19EB",
        ["ddengine64.dll"] = "B131EA39CA40544E6805246057213FAA9ED86E49B5B1CAC0029BC9532386CB75"
    };
    private static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Trisko connect VNC");
    private static string ServerPath => Path.Combine(InstallDirectory, "winvnc.exe");
    private static string IniPath => Path.Combine(InstallDirectory, ServiceName + ".ini");
    private static string SettingsPath => Path.Combine(InstallDirectory, "settings.json");
    private static string ImagePath => $"\"{ServerPath}\" -service";

    public Task<GeksaVncServerStatus> GetStatusAsync() => Task.Run(() =>
    {
        var installed = InspectOwnership();
        var settings = File.Exists(SettingsPath)
            ? JsonSerializer.Deserialize<GeksaVncServerSettings>(File.ReadAllText(SettingsPath)) ?? new()
            : new GeksaVncServerSettings();
        if (!installed) return new GeksaVncServerStatus(false, false, settings);
        using var service = new ServiceController(ServiceName);
        return new GeksaVncServerStatus(true, service.Status == ServiceControllerStatus.Running, settings);
    });

    public async Task EnableAsync(GeksaVncServerSettings settings, string password)
    {
        GeksaVncSettings.ValidatePort(settings.Port);
        GeksaVncSettings.ValidatePassword(password);
        settings = settings with { AllowedAddresses = GeksaVncSettings.NormalizeAllowedAddresses(settings.AllowedAddresses) };
        RequireAdministrator();
        await _gate.WaitAsync();
        try
        {
            var installed = InspectOwnership();
            var source = Path.Combine(AppContext.BaseDirectory, "Tools", "UltraVNC", "server-x64");
            foreach (var (name, hash) in BundleHashes)
            {
                var path = Path.Combine(source, name);
                if (!File.Exists(path) || !Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).Equals(hash, StringComparison.Ordinal))
                    throw new IOException(TriskoLocalization.Format("Файл сервера {0} отсутствует или повреждён. Переустановите программу.", name));
            }
            try
            {
                if (installed) await StopServiceAsync();
                // Refuse a collision instead of interfering with another remote access server.
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == settings.Port))
                    throw new IOException(TriskoLocalization.Text("Этот порт уже занят. Выберите другой порт сервера."));

                Directory.CreateDirectory(InstallDirectory);
                // Service binaries and reversible VNC credentials must not be writable/readable by ordinary users.
                await RunAsync("icacls.exe", InstallDirectory, "/inheritance:r", "/grant:r",
                    "*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F");
                foreach (var name in BundleHashes.Keys)
                    File.Copy(Path.Combine(source, name), Path.Combine(InstallDirectory, name), true);
                // Portable mode keeps this named service's INI separate from all other UltraVNC installations.
                await File.WriteAllTextAsync(Path.Combine(InstallDirectory, "ultravnc.portable"), "");
                await File.WriteAllTextAsync(IniPath, GeksaVncSettings.BuildServerConfiguration(settings, password), System.Text.Encoding.ASCII);
                await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(settings));

                await DeleteFirewallRuleAsync();
                if (!settings.LoopbackOnly)
                    await RunAsync("netsh.exe", "advfirewall", "firewall", "add", "rule", "name=" + FirewallRule,
                        "dir=in", "action=allow", "program=" + ServerPath, "protocol=TCP", "localport=" + settings.Port,
                        "remoteip=" + settings.AllowedAddresses, "profile=any", "enable=yes");
                if (!installed)
                    await RunAsync("sc.exe", "create", ServiceName, "binPath=", ImagePath, "start=", "demand",
                        "DisplayName=", "Trisko connect — VNC Server", "depend=", "Tcpip");
                await RunAsync("sc.exe", "config", ServiceName, "start=", settings.AutoStart ? "auto" : "demand");
                using var service = new ServiceController(ServiceName);
                service.Start();
                await WaitForStatusAsync(service, ServiceControllerStatus.Running);
                // The service starts a desktop helper; allow it time to bind its listening socket.
                var listening = false;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == settings.Port))
                    { listening = true; break; }
                    await Task.Delay(200);
                }
                if (!listening) throw new IOException(TriskoLocalization.Text("Служба запущена, но сервер не открыл порт. Проверьте журнал Windows."));
            }
            catch
            {
                // Never leave a partially configured remote access service enabled.
                if (InspectOwnership())
                {
                    await StopServiceAsync();
                    await RunAsync("sc.exe", "config", ServiceName, "start=", "disabled");
                }
                await DeleteFirewallRuleAsync();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DisableAsync()
    {
        RequireAdministrator();
        await _gate.WaitAsync();
        try
        {
            if (InspectOwnership())
            {
                await RunAsync("sc.exe", "config", ServiceName, "start=", "disabled");
                await StopServiceAsync();
            }
            await DeleteFirewallRuleAsync();
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<GeksaVncServerSettings>(await File.ReadAllTextAsync(SettingsPath)) ?? new();
                await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(settings with { AutoStart = false }));
            }
        }
        finally { _gate.Release(); }
    }

    private static bool InspectOwnership()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName);
        if (key == null) return false;
        if (!string.Equals(key.GetValue("ImagePath") as string, ImagePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException(TriskoLocalization.Text("Имя службы GeksaVnc занято другой установкой. Её настройки не изменены."));
        return true;
    }

    private static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException(TriskoLocalization.Text("Для настройки сервера запустите Trisko connect от имени администратора."));
        if (!Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException(TriskoLocalization.Text("В этой сборке сервер доступен для Windows x64."));
    }

    private static async Task StopServiceAsync()
    {
        using var service = new ServiceController(ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        await WaitForStatusAsync(service, ServiceControllerStatus.Stopped);
        // Desktop helper exits just after the service; wait for the executable to be released.
        await Task.Delay(500);
    }

    private static async Task WaitForStatusAsync(ServiceController service, ServiceControllerStatus desired)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            service.Refresh();
            if (service.Status == desired) return;
            await Task.Delay(100);
        }
        throw new System.TimeoutException(TriskoLocalization.Text("Служба VNC не завершила операцию за 15 секунд."));
    }

    private static Task DeleteFirewallRuleAsync() => Task.Run(() =>
    {
        // INetFwRules.Remove is a no-op when absent; unlike a localized netsh error,
        // this is safe on first installation while still surfacing access failures.
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
            ?? throw new PlatformNotSupportedException(TriskoLocalization.Text("Служба брандмауэра Windows недоступна."));
        dynamic policy = Activator.CreateInstance(type)!;
        object? rulesObject = null;
        try
        {
            rulesObject = policy.Rules;
            ((dynamic)rulesObject).Remove(FirewallRule);
        }
        finally
        {
            if (rulesObject != null) Marshal.FinalReleaseComObject(rulesObject);
            Marshal.FinalReleaseComObject(policy);
        }
    });

    private static async Task RunAsync(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, executable))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException(TriskoLocalization.Text("Не удалось запустить настройку службы VNC."));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new System.TimeoutException(TriskoLocalization.Text("Настройка службы VNC превысила время ожидания.")); }
        var result = (await output) + (await error);
        if (process.ExitCode != 0) throw new IOException(TriskoLocalization.Format("Не удалось настроить VNC ({0}, код {1}): {2}", executable, process.ExitCode, result.Trim()));
    }
}
