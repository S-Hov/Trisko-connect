using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ServiceLib;

namespace v2rayN.Desktop.Trisko;

public sealed class StringToImageConverter : IValueConverter
{
    public static readonly StringToImageConverter Instance = new();
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            return Cache.GetOrAdd(path, p =>
            {
                try
                {
                    Uri uri;
                    if (p.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                    {
                        uri = new Uri(p);
                    }
                    else
                    {
                        var clean = p.TrimStart('/');
                        if (clean.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            clean = clean.Substring("Assets/".Length);
                        }
                        uri = new Uri($"{Global.AvaAssets}{clean}");
                    }

                    if (AssetLoader.Exists(uri))
                    {
                        using var stream = AssetLoader.Open(uri);
                        return new Bitmap(stream);
                    }
                }
                catch
                {
                    // Ignore and fall back to null
                }
                return null;
            });
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
