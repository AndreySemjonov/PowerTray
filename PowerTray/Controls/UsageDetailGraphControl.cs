using System.Windows;
using System.Windows.Media;
using XPSBatteryTray.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace XPSBatteryTray.Controls;

public sealed class UsageDetailGraphControl : FrameworkElement
{
    private const double PeakLabelHeadroomMultiplier = 1.35;

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double?>), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(Array.Empty<double?>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PeaksProperty =
        DependencyProperty.Register(nameof(Peaks), typeof(IEnumerable<UsagePeakInfo>), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(Array.Empty<UsagePeakInfo>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(MediaBrush), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(MediaBrushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(MediaBrush), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxPointsProperty =
        DependencyProperty.Register(nameof(MaxPoints), typeof(int), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(300, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double?> Values
    {
        get => (IEnumerable<double?>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IEnumerable<UsagePeakInfo> Peaks
    {
        get => (IEnumerable<UsagePeakInfo>)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public MediaBrush Stroke
    {
        get => (MediaBrush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public MediaBrush? Fill
    {
        get => (MediaBrush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public int MaxPoints
    {
        get => (int)GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        const double leftPadding = 4;
        const double topPadding = 8;
        const double rightAxisWidth = 45;
        const double bottomLabelHeight = 28;
        double plotWidth = Math.Max(1, bounds.Width - leftPadding - rightAxisWidth);
        double plotHeight = Math.Max(1, bounds.Height - topPadding - bottomLabelHeight);
        double plotBottom = topPadding + plotHeight;

        int maxPoints = Math.Max(2, MaxPoints);
        double?[] raw = Values?.TakeLast(maxPoints).ToArray() ?? [];
        int originalCount = Values?.Count() ?? raw.Length;
        int firstVisibleIndex = Math.Max(0, originalCount - raw.Length);
        double scaleMaximum = CalculateScaleMaximum(raw);

        DrawGrid(drawingContext, leftPadding, topPadding, plotWidth, plotHeight);
        DrawAxisLabels(drawingContext, leftPadding + plotWidth + 7, topPadding, plotHeight, scaleMaximum);
        DrawTimeLabels(drawingContext, leftPadding, plotWidth, plotBottom + 8);

        var points = BuildPoints(raw, firstVisibleIndex, maxPoints, scaleMaximum, leftPadding, topPadding, plotWidth, plotHeight);

        if (points.Count < 2)
        {
            var emptyPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
            drawingContext.DrawLine(emptyPen, new WindowsPoint(leftPadding, topPadding + plotHeight / 2), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight / 2));
            return;
        }

        DrawArea(drawingContext, points, plotBottom);
        DrawLine(drawingContext, points);
        DrawPeaks(drawingContext, points, bounds);
    }

    private void DrawArea(DrawingContext context, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points, double bottom)
    {
        if (Fill is null)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using StreamGeometryContext stream = geometry.Open();
        stream.BeginFigure(new WindowsPoint(points[0].Point.X, bottom), true, true);
        stream.LineTo(points[0].Point, true, false);
        for (int i = 1; i < points.Count; i++)
        {
            stream.LineTo(points[i].Point, true, false);
        }

        stream.LineTo(new WindowsPoint(points[^1].Point.X, bottom), true, false);
        geometry.Freeze();
        context.DrawGeometry(Fill, null, geometry);
    }

    private void DrawLine(DrawingContext context, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext stream = geometry.Open();
        stream.BeginFigure(points[0].Point, false, false);
        for (int i = 1; i < points.Count; i++)
        {
            stream.LineTo(points[i].Point, true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, CreateCurvePen(Stroke), geometry);
    }

    private void DrawPeaks(DrawingContext context, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points, Rect bounds)
    {
        Dictionary<int, WindowsPoint> pointsByIndex = points.ToDictionary(point => point.Index, point => point.Point);
        var usedLabelRects = new List<Rect>();
        foreach (UsagePeakInfo peak in (Peaks ?? []).OrderBy(p => p.Rank))
        {
            if (!pointsByIndex.TryGetValue(peak.Index, out WindowsPoint point))
            {
                continue;
            }

            context.DrawEllipse(new SolidColorBrush(MediaColor.FromArgb(210, 26, 31, 36)), new MediaPen(Stroke, 1.4), point, 3.7, 3.7);
            DrawPeakLabel(context, peak.LabelText, point, bounds, usedLabelRects);
        }
    }

    private void DrawPeakLabel(DrawingContext context, string text, WindowsPoint point, Rect bounds, IList<Rect> usedLabelRects)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            9,
            new SolidColorBrush(MediaColor.FromRgb(240, 244, 248)),
            1.0)
        {
            MaxTextWidth = Math.Min(150, Math.Max(70, bounds.Width - 70)),
            Trimming = TextTrimming.CharacterEllipsis
        };

        double width = formatted.Width + 10;
        double height = formatted.Height + 5;
        double x = Math.Clamp(point.X - width / 2d, 6, Math.Max(6, bounds.Width - width - 48));
        double y = point.Y - height - 8;
        if (y < 4)
        {
            y = point.Y + 8;
        }

        var labelRect = new Rect(x, y, width, height);
        int attempts = 0;
        while (usedLabelRects.Any(rect => rect.IntersectsWith(labelRect)) && attempts < 5)
        {
            y += height + 4;
            if (y + height > bounds.Height - 28)
            {
                y = point.Y - height - 8 - (attempts + 1) * (height + 4);
            }

            labelRect = new Rect(x, y, width, height);
            attempts++;
        }

        usedLabelRects.Add(labelRect);
        context.DrawRoundedRectangle(new SolidColorBrush(MediaColor.FromArgb(230, 25, 30, 36)), new MediaPen(Stroke, 1), labelRect, 4, 4);
        context.DrawText(formatted, new WindowsPoint(labelRect.X + 5, labelRect.Y + 2));
    }

    private static IReadOnlyList<(int Index, WindowsPoint Point, double Value)> BuildPoints(
        IReadOnlyList<double?> values,
        int firstVisibleIndex,
        int maxPoints,
        double scaleMaximum,
        double left,
        double top,
        double width,
        double height)
    {
        var points = new List<(int Index, WindowsPoint Point, double Value)>();
        int slotCount = Math.Max(2, maxPoints);
        int firstSlot = Math.Max(0, slotCount - values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } value)
            {
                continue;
            }

            value = Math.Clamp(value, 0, 100);
            int slot = firstSlot + i;
            double x = left + slot * (width - 1) / (slotCount - 1);
            double y = top + height - Math.Clamp(value / scaleMaximum, 0, 1) * (height - 4);
            points.Add((firstVisibleIndex + i, new WindowsPoint(x, y), value));
        }

        return points;
    }

    private static double CalculateScaleMaximum(IEnumerable<double?> values)
    {
        double maximum = values.Where(v => v.HasValue).Select(v => Math.Clamp(v!.Value, 0, 100)).DefaultIfEmpty(0).Max();
        if (maximum <= 0.05)
        {
            return 10;
        }

        double target = Math.Clamp(maximum * PeakLabelHeadroomMultiplier, 10, 100);
        double step = target switch
        {
            <= 20 => 5,
            <= 50 => 10,
            _ => 25
        };

        return Math.Clamp(Math.Ceiling(target / step) * step, 10, 100);
    }

    private static void DrawGrid(DrawingContext context, double left, double top, double width, double height)
    {
        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(62, 94, 101, 111)), 1)
        {
            DashStyle = new DashStyle([4, 4], 0)
        };
        for (int i = 0; i <= 4; i++)
        {
            double y = top + i * height / 4d;
            context.DrawLine(gridPen, new WindowsPoint(left, y), new WindowsPoint(left + width, y));
        }

        var axisPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(47, 54, 64)), 1);
        context.DrawLine(axisPen, new WindowsPoint(left, top + height), new WindowsPoint(left + width, top + height));
        context.DrawLine(axisPen, new WindowsPoint(left + width, top), new WindowsPoint(left + width, top + height));
    }

    private static void DrawAxisLabels(DrawingContext context, double x, double top, double height, double scaleMaximum)
    {
        string[] labels =
        [
            FormatPercent(scaleMaximum),
            FormatPercent(scaleMaximum * 0.75),
            FormatPercent(scaleMaximum * 0.5),
            FormatPercent(scaleMaximum * 0.25),
            "0%"
        ];
        var brush = new SolidColorBrush(MediaColor.FromRgb(183, 188, 196));
        for (int i = 0; i < labels.Length; i++)
        {
            double y = top + i * height / 4d - 7;
            if (i == labels.Length - 1)
            {
                y -= 1;
            }

            DrawText(context, labels[i], brush, x, y, "Segoe UI", 10);
        }
    }

    private static string FormatPercent(double value) =>
        value >= 10 ? $"{value:N0}%" : $"{value:N1}%";

    private static void DrawTimeLabels(DrawingContext context, double left, double width, double y)
    {
        string[] labels = ["10m", "8m", "6m", "4m", "2m", "Now"];
        var brush = new SolidColorBrush(MediaColor.FromRgb(165, 171, 178));
        for (int i = 0; i < labels.Length; i++)
        {
            var formatted = CreateText(labels[i], brush, "Segoe UI", 10);
            double x = left + i * width / (labels.Length - 1);
            if (i == labels.Length - 1)
            {
                x -= formatted.Width;
            }
            else if (i > 0)
            {
                x -= formatted.Width / 2;
            }

            context.DrawText(formatted, new WindowsPoint(x, y));
        }
    }

    private static void DrawText(DrawingContext context, string text, MediaBrush brush, double x, double y, string fontFamily, double size) =>
        context.DrawText(CreateText(text, brush, fontFamily, size), new WindowsPoint(x, y));

    private static FormattedText CreateText(string text, MediaBrush brush, string fontFamily, double size) =>
        new(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(fontFamily),
            size,
            brush,
            1.0);

    private static MediaPen CreateCurvePen(MediaBrush stroke) =>
        new(stroke, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
}
