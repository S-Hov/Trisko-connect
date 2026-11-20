using Avalonia.Styling;

namespace v2rayN.Desktop.Trisko;

public static class TriskoThemeVariants
{
    public static readonly ThemeVariant Brand = new ThemeVariant("Trisko", ThemeVariant.Dark);

    public static ThemeVariant FromMode(TriskoThemeMode mode) => mode switch
    {
        TriskoThemeMode.Light => ThemeVariant.Light,
        TriskoThemeMode.Dark => ThemeVariant.Dark,
        TriskoThemeMode.Brand => Brand,
        _ => ThemeVariant.Default
    };

    private static ThemeVariant _currentVariant = Brand;
    public static ThemeVariant CurrentVariant 
    { 
        get => _currentVariant;
        set 
        {
            if (_currentVariant != value)
            {
                _currentVariant = value;
                CurrentVariantChanged?.Invoke(null, System.EventArgs.Empty);
            }
        }
    }

    public static event System.EventHandler? CurrentVariantChanged;
}
