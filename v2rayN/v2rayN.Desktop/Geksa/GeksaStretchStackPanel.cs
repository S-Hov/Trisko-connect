using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace v2rayN.Desktop.Geksa;

/// <summary>Collapses hidden blocks and shares spare height between stretching blocks.</summary>
public class GeksaStretchStackPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<GeksaStretchStackPanel, double>(nameof(Spacing), 14);
    public static readonly AttachedProperty<double> NaturalHeightLimitProperty =
        AvaloniaProperty.RegisterAttached<GeksaStretchStackPanel, Control, double>("NaturalHeightLimit", double.PositiveInfinity);

    public static double GetNaturalHeightLimit(Control control) => control.GetValue(NaturalHeightLimitProperty);
    public static void SetNaturalHeightLimit(Control control, double value) => control.SetValue(NaturalHeightLimitProperty, value);

    static GeksaStretchStackPanel() => AffectsMeasure<GeksaStretchStackPanel>(SpacingProperty);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public GeksaStretchStackPanel()
    {
        Children.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (Control child in e.OldItems) child.PropertyChanged -= ChildPropertyChanged;
            if (e.NewItems is not null)
                foreach (Control child in e.NewItems) child.PropertyChanged += ChildPropertyChanged;
        };
    }

    private void ChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == VerticalAlignmentProperty
            || e.Property == NaturalHeightLimitProperty)
            InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Where(child => child.IsVisible).ToArray();
        foreach (var child in children)
            child.Measure(new Size(availableSize.Width, GetNaturalHeightLimit(child)));
        return new Size(children.Select(child => child.DesiredSize.Width).DefaultIfEmpty().Max(),
            children.Sum(child => child.DesiredSize.Height) + Math.Max(0, children.Length - 1) * Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(child => child.IsVisible).ToArray();
        var stretchCount = children.Count(child => child.VerticalAlignment == VerticalAlignment.Stretch);
        var naturalHeight = children.Sum(child => child.DesiredSize.Height)
            + Math.Max(0, children.Length - 1) * Spacing;
        var extra = stretchCount == 0 ? 0 : Math.Max(0, finalSize.Height - naturalHeight) / stretchCount;
        double y = 0;
        foreach (var child in children)
        {
            var height = child.DesiredSize.Height
                + (child.VerticalAlignment == VerticalAlignment.Stretch ? extra : 0);
            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height + Spacing;
        }
        return finalSize;
    }
}
