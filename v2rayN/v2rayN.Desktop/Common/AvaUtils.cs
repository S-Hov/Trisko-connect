using Avalonia.Input.Platform;

namespace v2rayN.Desktop.Common;

internal class AvaUtils
{
    public static async Task<string?> GetClipboardData(Window owner)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
            if (clipboard == null)
            {
                return null;
            }

            return await clipboard.TryGetTextAsync();
        }
        catch
        {
            return null;
        }
    }

    public static async Task SetClipboardData(Visual? visual, string strData)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            await clipboard.SetTextAsync(strData);
        }
        catch
        {
        }
    }

    public static WindowIcon GetAppIcon(ESysProxyType sysProxyType = default)
    {
        var customPath = Utils.GetPath("trisko-connect.ico");
        if (File.Exists(customPath))
        {
            return new(customPath);
        }

        var uri = new Uri(Path.Combine(Global.AvaAssets, "Trisko/Brand/trisko-connect.ico"));
        using var bitmap = new Bitmap(AssetLoader.Open(uri));
        return new(bitmap);
    }
}
