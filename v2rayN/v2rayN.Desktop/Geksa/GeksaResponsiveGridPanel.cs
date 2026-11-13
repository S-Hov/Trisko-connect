using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace v2rayN.Desktop.Geksa;

/// <summary>
/// Адаптивная грид-сетка для карточек ресурсов с автоматическим расчётом колонок
/// и равномерным распределением ширины (процентная/пропорциональная ширина на 100% контейнера).
/// </summary>
public class GeksaResponsiveGridPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<GeksaResponsiveGridPanel, double>(nameof(MinItemWidth), 300);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<GeksaResponsiveGridPanel, double>(nameof(Spacing), 18);

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleChildren = Children.Where(c => c.IsVisible).ToList();
        if (visibleChildren.Count == 0)
        {
            return new Size(0, 0);
        }

        var availableWidth = availableSize.Width;
        var width = double.IsInfinity(availableWidth) || availableWidth <= 0
            ? visibleChildren.Count * MinItemWidth
            : availableWidth;

        var spacing = Spacing;
        var minWidth = Math.Max(1, MinItemWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + spacing) / (minWidth + spacing)));
        var itemWidth = Math.Max(1, (width - (columns - 1) * spacing) / columns);

        var rows = ChunkBy(visibleChildren, columns);
        double totalHeight = 0;

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            double maxRowHeight = 0;

            foreach (var child in row)
            {
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                maxRowHeight = Math.Max(maxRowHeight, child.DesiredSize.Height);
            }

            totalHeight += maxRowHeight;
            if (r < rows.Count - 1)
            {
                totalHeight += spacing;
            }
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleChildren = Children.Where(c => c.IsVisible).ToList();
        if (visibleChildren.Count == 0)
        {
            return finalSize;
        }

        var width = finalSize.Width;
        var spacing = Spacing;
        var minWidth = Math.Max(1, MinItemWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + spacing) / (minWidth + spacing)));
        var itemWidth = Math.Max(1, (width - (columns - 1) * spacing) / columns);

        var rows = ChunkBy(visibleChildren, columns);
        double y = 0;

        foreach (var row in rows)
        {
            double maxRowHeight = 0;
            foreach (var child in row)
            {
                maxRowHeight = Math.Max(maxRowHeight, child.DesiredSize.Height);
            }

            double x = 0;
            for (int i = 0; i < row.Count; i++)
            {
                var child = row[i];
                child.Arrange(new Rect(x, y, itemWidth, maxRowHeight));
                x += itemWidth + spacing;
            }

            y += maxRowHeight + spacing;
        }

        return finalSize;
    }

    private static List<List<Control>> ChunkBy(List<Control> source, int chunkSize)
    {
        var result = new List<List<Control>>();
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            result.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        }
        return result;
    }
}
