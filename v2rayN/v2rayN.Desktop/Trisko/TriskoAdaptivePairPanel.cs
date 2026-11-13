using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace v2rayN.Desktop.Trisko;

/// <summary>Two columns at wide widths; full-width rows in compact layouts.</summary>
public class TriskoAdaptivePairPanel : Panel
{
    public static readonly StyledProperty<double> BreakpointProperty =
        AvaloniaProperty.Register<TriskoAdaptivePairPanel, double>(nameof(Breakpoint), 600);
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TriskoAdaptivePairPanel, double>(nameof(Spacing), 12);
    public static readonly StyledProperty<double> PrimaryFractionProperty =
        AvaloniaProperty.Register<TriskoAdaptivePairPanel, double>(nameof(PrimaryFraction), 0.6);
    public static readonly StyledProperty<bool> SecondaryAutoWidthProperty =
        AvaloniaProperty.Register<TriskoAdaptivePairPanel, bool>(nameof(SecondaryAutoWidth), true);
    public static readonly StyledProperty<bool> FillHeightProperty =
        AvaloniaProperty.Register<TriskoAdaptivePairPanel, bool>(nameof(FillHeight));

    static TriskoAdaptivePairPanel()
    {
        AffectsMeasure<TriskoAdaptivePairPanel>(BreakpointProperty, SpacingProperty,
            PrimaryFractionProperty, SecondaryAutoWidthProperty, FillHeightProperty);
    }

    public double Breakpoint { get => GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public double PrimaryFraction { get => GetValue(PrimaryFractionProperty); set => SetValue(PrimaryFractionProperty, value); }
    public bool SecondaryAutoWidth { get => GetValue(SecondaryAutoWidthProperty); set => SetValue(SecondaryAutoWidthProperty, value); }
    public bool FillHeight { get => GetValue(FillHeightProperty); set => SetValue(FillHeightProperty, value); }

    public TriskoAdaptivePairPanel()
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
        if (e.Property == IsVisibleProperty) InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Where(c => c.IsVisible).ToArray();
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Breakpoint;
        if (children.Length != 2 || width < Breakpoint)
        {
            foreach (var child in children)
                child.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, children.Sum(c => c.DesiredSize.Height) + Math.Max(0, children.Length - 1) * Spacing);
        }

        var usable = Math.Max(0, width - Spacing);
        var secondaryWidth = usable * (1 - Math.Clamp(PrimaryFraction, 0.1, 0.9));
        if (SecondaryAutoWidth)
        {
            children[1].Measure(new Size(usable / 2, availableSize.Height));
            secondaryWidth = Math.Min(usable / 2, children[1].DesiredSize.Width);
        }
        children[0].Measure(new Size(usable - secondaryWidth, availableSize.Height));
        children[1].Measure(new Size(secondaryWidth, availableSize.Height));
        return new Size(width, Math.Max(children[0].DesiredSize.Height, children[1].DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(c => c.IsVisible).ToArray();
        if (children.Length != 2 || finalSize.Width < Breakpoint)
        {
            double y = 0;
            var naturalHeight = children.Sum(c => c.DesiredSize.Height) + Math.Max(0, children.Length - 1) * Spacing;
            var extra = FillHeight && children.Length > 0 ? Math.Max(0, finalSize.Height - naturalHeight) / children.Length : 0;
            foreach (var child in children)
            {
                var height = child.DesiredSize.Height + extra;
                child.Arrange(new Rect(0, y, finalSize.Width, height));
                y += height + Spacing;
            }
        }
        else
        {
            var usable = Math.Max(0, finalSize.Width - Spacing);
            var secondaryWidth = SecondaryAutoWidth
                ? Math.Min(usable / 2, children[1].DesiredSize.Width)
                : usable * (1 - Math.Clamp(PrimaryFraction, 0.1, 0.9));
            children[0].Arrange(new Rect(0, 0, usable - secondaryWidth, finalSize.Height));
            children[1].Arrange(new Rect(finalSize.Width - secondaryWidth, 0, secondaryWidth, finalSize.Height));
        }
        return finalSize;
    }
}
