using System.Windows;
using System.Windows.Media;
using PowerTray.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace PowerTray.Controls;

public sealed class UsageDetailGraphControl : FrameworkElement
{
    private static class Layout
    {
        public const double PeakLabelHeadroomMultiplier = 1.08;
        public const double LeftPadding = 4;
        public const double TopPadding = 8;
        public const double RightAxisWidth = 45;
        public const double BottomLabelHeight = 28;
        public const double RightAxisLabelGap = 7;
        public const double TimeLabelTopGap = 8;
        public const double PeakMarkerRadius = 3.7;
        public const double PeakLabelHorizontalPadding = 10;
        public const double PeakLabelVerticalPadding = 5;
        public const double PeakLabelGap = 8;
        public const double PeakLabelMinWidth = 70;
        public const double PeakLabelMaxWidth = 150;
        public const double PeakLabelRightReserve = 48;
    }

    private static class Typography
    {
        public const double PeakLabelSize = 9;
        public const double AxisLabelSize = 10;
        public const double TimeLabelSize = 10;
    }

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

    public static readonly DependencyProperty AxisUnitProperty =
        DependencyProperty.Register(nameof(AxisUnit), typeof(string), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisFormatProperty =
        DependencyProperty.Register(nameof(AxisFormat), typeof(string), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata("N0", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSignedProperty =
        DependencyProperty.Register(nameof(IsSigned), typeof(bool), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SmoothLineProperty =
        DependencyProperty.Register(nameof(SmoothLine), typeof(bool), typeof(UsageDetailGraphControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

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

    public string AxisUnit
    {
        get => (string)GetValue(AxisUnitProperty);
        set => SetValue(AxisUnitProperty, value);
    }

    public string AxisFormat
    {
        get => (string)GetValue(AxisFormatProperty);
        set => SetValue(AxisFormatProperty, value);
    }

    public bool IsSigned
    {
        get => (bool)GetValue(IsSignedProperty);
        set => SetValue(IsSignedProperty, value);
    }

    public bool SmoothLine
    {
        get => (bool)GetValue(SmoothLineProperty);
        set => SetValue(SmoothLineProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        double plotWidth = Math.Max(1, bounds.Width - Layout.LeftPadding - Layout.RightAxisWidth);
        double plotHeight = Math.Max(1, bounds.Height - Layout.TopPadding - Layout.BottomLabelHeight);
        double plotBottom = Layout.TopPadding + plotHeight;

        int maxPoints = Math.Max(2, MaxPoints);
        double?[] raw = Values?.TakeLast(maxPoints).ToArray() ?? [];
        int originalCount = Values?.Count() ?? raw.Length;
        int firstVisibleIndex = Math.Max(0, originalCount - raw.Length);
        GraphScale scale = CalculateScale(raw, IsSigned);
        double zeroY = MapValueToY(0, scale, Layout.TopPadding, plotHeight);
        double?[] displayRaw = SmoothLine ? SmoothValues(raw) : raw;

        DrawGrid(drawingContext, Layout.LeftPadding, Layout.TopPadding, plotWidth, plotHeight);
        DrawAxisLabels(drawingContext, Layout.LeftPadding + plotWidth + Layout.RightAxisLabelGap, Layout.TopPadding, plotHeight, scale, AxisUnit, AxisFormat);
        DrawTimeLabels(drawingContext, Layout.LeftPadding, plotWidth, plotBottom + Layout.TimeLabelTopGap);

        var points = BuildPoints(displayRaw, firstVisibleIndex, maxPoints, scale, Layout.LeftPadding, Layout.TopPadding, plotWidth, plotHeight);

        if (points.Count < 2)
        {
            var emptyPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
            drawingContext.DrawLine(emptyPen, new WindowsPoint(Layout.LeftPadding, zeroY), new WindowsPoint(Layout.LeftPadding + plotWidth, zeroY));
            return;
        }

        DrawArea(drawingContext, points, zeroY, CreateAreaBrush(Fill, points, zeroY));
        DrawLine(drawingContext, points);
        DrawPeaks(drawingContext, points, bounds);
    }

    private void DrawArea(DrawingContext context, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points, double baselineY, MediaBrush? fill)
    {
        if (fill is null)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using StreamGeometryContext stream = geometry.Open();
        stream.BeginFigure(new WindowsPoint(points[0].Point.X, baselineY), true, true);
        stream.LineTo(points[0].Point, true, false);
        AppendLineSegments(stream, points);

        stream.LineTo(new WindowsPoint(points[^1].Point.X, baselineY), true, false);
        geometry.Freeze();
        context.DrawGeometry(fill, null, geometry);
    }

    private MediaBrush? CreateAreaBrush(MediaBrush? fill, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points, double baselineY)
    {
        if (fill is null)
        {
            return null;
        }

        MediaColor color = ResolveBrushColor(fill, MediaColor.FromArgb(84, 245, 170, 45));
        byte strongAlpha = color.A > 0 ? color.A : (byte)84;
        if (!IsSigned)
        {
            strongAlpha = (byte)Math.Clamp(Math.Max((int)strongAlpha, 92) * 1.3, 0, 190);
        }

        byte midAlpha = (byte)Math.Clamp(strongAlpha * 0.45, 0, 255);
        byte weakAlpha = (byte)Math.Max(0, strongAlpha * 0.01);
        double top = Math.Min(baselineY, points.Min(point => point.Point.Y));
        double bottom = Math.Max(baselineY, points.Max(point => point.Point.Y));
        if (Math.Abs(bottom - top) < 1)
        {
            bottom = top + 1;
        }

        double baselineOffset = Math.Clamp((baselineY - top) / (bottom - top), 0, 1);

        var brush = new LinearGradientBrush
        {
            StartPoint = new WindowsPoint(0, top),
            EndPoint = new WindowsPoint(0, bottom),
            MappingMode = BrushMappingMode.Absolute
        };

        if (IsSigned)
        {
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(strongAlpha, color.R, color.G, color.B), 0));
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(weakAlpha, color.R, color.G, color.B), baselineOffset));
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(strongAlpha, color.R, color.G, color.B), 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(strongAlpha, color.R, color.G, color.B), 0));
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(midAlpha, color.R, color.G, color.B), 0.45));
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(weakAlpha, color.R, color.G, color.B), 1));
        }

        brush.Freeze();
        return brush;
    }

    private static MediaColor ResolveBrushColor(MediaBrush brush, MediaColor fallback)
    {
        if (brush is SolidColorBrush solid)
        {
            return solid.Color;
        }

        if (brush is LinearGradientBrush gradient && gradient.GradientStops.Count > 0)
        {
            return gradient.GradientStops
                .OrderByDescending(stop => stop.Color.A)
                .First()
                .Color;
        }

        return fallback;
    }

    private void DrawLine(DrawingContext context, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext stream = geometry.Open();
        stream.BeginFigure(points[0].Point, false, false);
        AppendLineSegments(stream, points);

        geometry.Freeze();
        context.DrawGeometry(null, CreateCurvePen(Stroke), geometry);
    }

    private void AppendLineSegments(StreamGeometryContext stream, IReadOnlyList<(int Index, WindowsPoint Point, double Value)> points)
    {
        if (!SmoothLine || points.Count < 4)
        {
            for (int i = 1; i < points.Count; i++)
            {
                stream.LineTo(points[i].Point, true, false);
            }

            return;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            WindowsPoint p0 = i == 0 ? points[i].Point : points[i - 1].Point;
            WindowsPoint p1 = points[i].Point;
            WindowsPoint p2 = points[i + 1].Point;
            WindowsPoint p3 = i + 2 < points.Count ? points[i + 2].Point : p2;

            WindowsPoint control1 = new(p1.X + (p2.X - p0.X) / 6d, p1.Y + (p2.Y - p0.Y) / 6d);
            WindowsPoint control2 = new(p2.X - (p3.X - p1.X) / 6d, p2.Y - (p3.Y - p1.Y) / 6d);
            stream.BezierTo(control1, control2, p2, true, false);
        }
    }

    private static double?[] SmoothValues(IReadOnlyList<double?> values)
    {
        if (values.Count < 4)
        {
            return values.ToArray();
        }

        int[] weights = [1, 2, 4, 2, 1];
        var smoothed = new double?[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            if (!values[i].HasValue)
            {
                continue;
            }

            double sum = 0;
            int weightSum = 0;
            for (int offset = -2; offset <= 2; offset++)
            {
                int index = i + offset;
                if (index < 0 || index >= values.Count || values[index] is not { } value)
                {
                    continue;
                }

                int weight = weights[offset + 2];
                sum += value * weight;
                weightSum += weight;
            }

            smoothed[i] = weightSum > 0 ? sum / weightSum : values[i];
        }

        return smoothed;
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

            context.DrawEllipse(new SolidColorBrush(MediaColor.FromArgb(210, 26, 31, 36)), new MediaPen(Stroke, 1.4), point, Layout.PeakMarkerRadius, Layout.PeakMarkerRadius);
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
            Typography.PeakLabelSize,
            new SolidColorBrush(MediaColor.FromRgb(240, 244, 248)),
            1.0)
        {
            MaxTextWidth = Math.Min(Layout.PeakLabelMaxWidth, Math.Max(Layout.PeakLabelMinWidth, bounds.Width - Layout.PeakLabelMinWidth)),
            Trimming = TextTrimming.CharacterEllipsis
        };

        double width = formatted.Width + Layout.PeakLabelHorizontalPadding;
        double height = formatted.Height + Layout.PeakLabelVerticalPadding;
        double x = Math.Clamp(point.X - width / 2d, 6, Math.Max(6, bounds.Width - width - Layout.PeakLabelRightReserve));
        double y = point.Y - height - Layout.PeakLabelGap;
        if (y < 4)
        {
            y = point.Y + Layout.PeakLabelGap;
        }

        var labelRect = new Rect(x, y, width, height);
        int attempts = 0;
        while (usedLabelRects.Any(rect => rect.IntersectsWith(labelRect)) && attempts < 5)
        {
            y += height + 4;
            if (y + height > bounds.Height - 28)
            {
                y = point.Y - height - Layout.PeakLabelGap - (attempts + 1) * (height + 4);
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
        GraphScale scale,
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

            value = Math.Clamp(value, scale.Minimum, scale.Maximum);
            int slot = firstSlot + i;
            double x = left + slot * (width - 1) / (slotCount - 1);
            double y = MapValueToY(value, scale, top, height);
            points.Add((firstVisibleIndex + i, new WindowsPoint(x, y), value));
        }

        return points;
    }

    private static GraphScale CalculateScale(IEnumerable<double?> values, bool signed)
    {
        double[] visible = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (signed)
        {
            double positiveMaximum = visible.Where(value => value > 0).DefaultIfEmpty(0).Max();
            double negativeMinimum = visible.Where(value => value < 0).DefaultIfEmpty(0).Min();
            double signedMaximum = positiveMaximum > 0.05
                ? RoundScaleMaximum(Math.Clamp(positiveMaximum * Layout.PeakLabelHeadroomMultiplier, 5, 100))
                : 0;
            double signedMinimum = negativeMinimum < -0.05
                ? -RoundScaleMaximum(Math.Clamp(Math.Abs(negativeMinimum) * Layout.PeakLabelHeadroomMultiplier, 5, 100))
                : 0;

            if (Math.Abs(signedMaximum - signedMinimum) < 1)
            {
                signedMaximum = 10;
            }

            return new GraphScale(signedMinimum, signedMaximum);
        }

        double maximum = visible.Select(v => Math.Clamp(v, 0, 100)).DefaultIfEmpty(0).Max();
        if (maximum <= 0.05)
        {
            return new GraphScale(0, 2);
        }

        return new GraphScale(0, RoundScaleMaximum(Math.Clamp(maximum * Layout.PeakLabelHeadroomMultiplier, 2, 100)));
    }

    private static double RoundScaleMaximum(double target)
    {
        double step = target switch
        {
            <= 12 => 1,
            <= 20 => 2.5,
            <= 60 => 2.5,
            _ => 5
        };

        return Math.Clamp(Math.Ceiling(target / step) * step, 1, 100);
    }

    private static double MapValueToY(double value, GraphScale scale, double top, double height)
    {
        double range = Math.Max(1, scale.Maximum - scale.Minimum);
        return top + height - Math.Clamp((value - scale.Minimum) / range, 0, 1) * (height - 4);
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

    private static void DrawAxisLabels(DrawingContext context, double x, double top, double height, GraphScale scale, string unit, string format)
    {
        string[] labels =
        [
            FormatAxisValue(scale.Maximum, unit, format),
            FormatAxisValue(scale.Minimum + (scale.Maximum - scale.Minimum) * 0.75, unit, format),
            FormatAxisValue(scale.Minimum + (scale.Maximum - scale.Minimum) * 0.5, unit, format),
            FormatAxisValue(scale.Minimum + (scale.Maximum - scale.Minimum) * 0.25, unit, format),
            FormatAxisValue(scale.Minimum, unit, format)
        ];
        var brush = new SolidColorBrush(MediaColor.FromRgb(183, 188, 196));
        for (int i = 0; i < labels.Length; i++)
        {
            double y = top + i * height / 4d - 7;
            if (i == labels.Length - 1)
            {
                y -= 1;
            }

            DrawText(context, labels[i], brush, x, y, "Segoe UI", Typography.AxisLabelSize);
        }
    }

    private static string FormatAxisValue(double value, string unit, string format)
    {
        string effectiveFormat = value >= 10 && format == "N1" ? "N0" : format;
        return $"{value.ToString(effectiveFormat)}{unit}";
    }

    private static void DrawTimeLabels(DrawingContext context, double left, double width, double y)
    {
        string[] labels = ["10m", "8m", "6m", "4m", "2m", "Now"];
        var brush = new SolidColorBrush(MediaColor.FromRgb(165, 171, 178));
        for (int i = 0; i < labels.Length; i++)
        {
            var formatted = CreateText(labels[i], brush, "Segoe UI", Typography.TimeLabelSize);
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

    private readonly record struct GraphScale(double Minimum, double Maximum);
}
