using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace v2rayN.Desktop.Geksa;

public sealed class StringToGeometryConverter : IValueConverter
{
    public static readonly StringToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                return StreamGeometry.Parse(path);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
