using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace XPSBatteryTray.Controls;

public sealed class SparklineControl : FrameworkElement
{
    private const double SmoothingTension = 0.42;

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double?>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(Array.Empty<double?>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(MediaBrush), typeof(SparklineControl),
            new FrameworkPropertyMetadata(MediaBrushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxPointsProperty =
        DependencyProperty.Register(nameof(MaxPoints), typeof(int), typeof(SparklineControl),
            new FrameworkPropertyMetadata(120, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(MediaBrush), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double?> Values
    {
        get => (IEnumerable<double?>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public MediaBrush Stroke
    {
        get => (MediaBrush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int MaxPoints
    {
        get => (int)GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    public MediaBrush? Fill
    {
        get => (MediaBrush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        const double leftPadding = 10;
        const double rightAxisWidth = 44;
        const double topPadding = 4;
        const double bottomLabelHeight = 34;
        double plotHeight = Math.Max(1, ActualHeight - topPadding - bottomLabelHeight);
        double plotWidth = Math.Max(1, ActualWidth - leftPadding - rightAxisWidth);
        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(80, 88, 92, 98)), 1);
        for (int i = 1; i <= 3; i++)
        {
            double y = topPadding + i * plotHeight / 4d;
            drawingContext.DrawLine(gridPen, new WindowsPoint(leftPadding, y), new WindowsPoint(leftPadding + plotWidth, y));
        }

        int maxPoints = Math.Max(2, MaxPoints);
        double[] values = Values?.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(maxPoints).ToArray() ?? [];
        DrawTimeLabels(drawingContext, leftPadding, plotWidth, topPadding + plotHeight + 7);
        if (values.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            var pen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
            drawingContext.DrawLine(pen, new WindowsPoint(leftPadding, topPadding + plotHeight / 2), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight / 2));
            return;
        }

        double[] displayValues = SmoothValues(values);
        double min = displayValues.Min();
        double max = displayValues.Max();
        bool isFlat = Math.Abs(max - min) < 0.001;
        if (isFlat)
        {
            max = min + 1;
        }

        var points = new List<(WindowsPoint Point, double Value)>(displayValues.Length);
        for (int i = 0; i < displayValues.Length; i++)
        {
            int slot = maxPoints - displayValues.Length + i;
            double x = leftPadding + slot * (plotWidth - 1) / (maxPoints - 1);
            double y = topPadding + plotHeight - ((displayValues[i] - min) / (max - min) * (plotHeight - 4));
            points.Add((new WindowsPoint(x, y), displayValues[i]));
        }

        StreamGeometry geometry = BuildCurveGeometry(points.Select(p => p.Point).ToArray(), closeToBottom: false, topPadding + plotHeight);
        StreamGeometry areaGeometry = BuildCurveGeometry(points.Select(p => p.Point).ToArray(), closeToBottom: true, topPadding + plotHeight);
        geometry.Freeze();
        areaGeometry.Freeze();
        bool flatZero = values.All(v => Math.Abs(v) < 0.05);
        if (Fill is not null && !flatZero)
        {
            drawingContext.DrawGeometry(Fill, null, areaGeometry);
        }

        var axisPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(47, 54, 64)), 1);
        drawingContext.DrawLine(axisPen, new WindowsPoint(leftPadding, topPadding + plotHeight), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight));
        drawingContext.DrawLine(axisPen, new WindowsPoint(leftPadding + plotWidth, topPadding), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight));
        drawingContext.DrawGeometry(null, CreateCurvePen(Stroke), geometry);

        var textBrush = new SolidColorBrush(MediaColor.FromRgb(175, 178, 184));
        DrawLabel(drawingContext, max, textBrush, leftPadding + plotWidth + 7, topPadding - 1);
        DrawLabel(drawingContext, (max + min) / 2d, textBrush, leftPadding + plotWidth + 7, topPadding + plotHeight / 2d - 7);
        DrawLabel(drawingContext, min, textBrush, leftPadding + plotWidth + 7, topPadding + plotHeight - 14);

        if (points.Count > 2 && !isFlat)
        {
            int maxIndex = 0;
            int minIndex = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].Value > points[maxIndex].Value)
                {
                    maxIndex = i;
                }

                if (points[i].Value < points[minIndex].Value)
                {
                    minIndex = i;
                }
            }

            DrawPeakLabel(drawingContext, points[maxIndex].Point, points[maxIndex].Value, Stroke, rect, preferAbove: true);
            DrawPeakLabel(drawingContext, points[minIndex].Point, points[minIndex].Value, Stroke, rect, preferAbove: false);
        }
    }

    private static double[] SmoothValues(double[] values)
    {
        if (values.Length < 4)
        {
            return values;
        }

        var smoothed = new double[values.Length];
        smoothed[0] = values[0];
        smoothed[^1] = values[^1];
        for (int i = 1; i < values.Length - 1; i++)
        {
            smoothed[i] = values[i - 1] * 0.24 + values[i] * 0.52 + values[i + 1] * 0.24;
        }

        return smoothed;
    }

    private static StreamGeometry BuildCurveGeometry(IReadOnlyList<WindowsPoint> points, bool closeToBottom, double bottom)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext context = geometry.Open();
        if (points.Count == 0)
        {
            return geometry;
        }

        if (closeToBottom)
        {
            context.BeginFigure(new WindowsPoint(points[0].X, bottom), true, true);
            context.LineTo(points[0], true, false);
        }
        else
        {
            context.BeginFigure(points[0], false, false);
        }

        if (points.Count == 1)
        {
            if (closeToBottom)
            {
                context.LineTo(new WindowsPoint(points[0].X, bottom), true, false);
            }

            return geometry;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            WindowsPoint previous = i == 0 ? points[i] : points[i - 1];
            WindowsPoint current = points[i];
            WindowsPoint next = points[i + 1];
            WindowsPoint following = i + 2 < points.Count ? points[i + 2] : next;

            var control1 = new WindowsPoint(
                current.X + (next.X - previous.X) * SmoothingTension / 6d,
                current.Y + (next.Y - previous.Y) * SmoothingTension / 6d);
            var control2 = new WindowsPoint(
                next.X - (following.X - current.X) * SmoothingTension / 6d,
                next.Y - (following.Y - current.Y) * SmoothingTension / 6d);
            context.BezierTo(control1, control2, next, true, false);
        }

        if (closeToBottom)
        {
            context.LineTo(new WindowsPoint(points[^1].X, bottom), true, false);
        }

        return geometry;
    }

    private static MediaPen CreateCurvePen(MediaBrush stroke) =>
        new(stroke, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

    private static void DrawLabel(DrawingContext context, double value, MediaBrush brush, double x, double y)
    {
        string text = Math.Abs(value) >= 10 ? value.ToString("N0") : value.ToString("N1");
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            brush,
            1.0);
        context.DrawText(formatted, new WindowsPoint(x, y));
    }

    private static void DrawPeakLabel(DrawingContext context, WindowsPoint point, double value, MediaBrush accent, Rect bounds, bool preferAbove)
    {
        string text = Math.Abs(value) >= 10 ? value.ToString("N0") : value.ToString("N1");
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            10,
            new SolidColorBrush(MediaColor.FromRgb(235, 238, 242)),
            1.0);

        double width = formatted.Width + 10;
        double height = formatted.Height + 4;
        double x = Math.Clamp(point.X - width / 2d, 4, Math.Max(4, bounds.Width - width - 46));
        double y = preferAbove ? point.Y - height - 5 : point.Y + 5;
        if (y < 2)
        {
            y = point.Y + 5;
        }
        else if (y + height > bounds.Height - 16)
        {
            y = point.Y - height - 5;
        }

        var labelRect = new Rect(x, y, width, height);
        context.DrawRoundedRectangle(new SolidColorBrush(MediaColor.FromArgb(220, 24, 27, 31)), new MediaPen(accent, 1), labelRect, 4, 4);
        context.DrawText(formatted, new WindowsPoint(x + 5, y + 1));
    }

    private static void DrawTimeLabels(DrawingContext context, double left, double width, double y)
    {
        string[] labels = ["10m", "8m", "6m", "4m", "2m", "Now"];
        var brush = new SolidColorBrush(MediaColor.FromRgb(165, 171, 178));
        for (int i = 0; i < labels.Length; i++)
        {
            double x = left + i * width / (labels.Length - 1);
            var formatted = new FormattedText(
                labels[i],
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                brush,
                1.0);
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
}
