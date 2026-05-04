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

    public static readonly DependencyProperty SecondaryValuesProperty =
        DependencyProperty.Register(nameof(SecondaryValues), typeof(IEnumerable<double?>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(Array.Empty<double?>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryStrokeProperty =
        DependencyProperty.Register(nameof(SecondaryStroke), typeof(MediaBrush), typeof(SparklineControl),
            new FrameworkPropertyMetadata(MediaBrushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxPointsProperty =
        DependencyProperty.Register(nameof(MaxPoints), typeof(int), typeof(SparklineControl),
            new FrameworkPropertyMetadata(120, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(MediaBrush), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double?), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double?), typeof(SparklineControl),
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

    public IEnumerable<double?> SecondaryValues
    {
        get => (IEnumerable<double?>)GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public MediaBrush SecondaryStroke
    {
        get => (MediaBrush)GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
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

    public double? Minimum
    {
        get => (double?)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double? Maximum
    {
        get => (double?)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
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
        double[] secondaryValues = SecondaryValues?.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(maxPoints).ToArray() ?? [];
        double[] scaleValues = values.Concat(secondaryValues).ToArray();
        DrawTimeLabels(drawingContext, leftPadding, plotWidth, topPadding + plotHeight + 7);
        if (scaleValues.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            var pen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
            drawingContext.DrawLine(pen, new WindowsPoint(leftPadding, topPadding + plotHeight / 2), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight / 2));
            return;
        }

        double[] displayValues = SmoothValues(values);
        double[] displaySecondaryValues = SmoothValues(secondaryValues);
        double[] displayScaleValues = displayValues.Concat(displaySecondaryValues).ToArray();
        double min = Minimum ?? displayScaleValues.Min();
        double max = Maximum ?? displayScaleValues.Max();
        bool isFlat = Math.Abs(max - min) < 0.001;
        if (isFlat)
        {
            max = min + 1;
        }

        List<(WindowsPoint Point, double Value)> points = BuildPoints(displayValues, min, max, maxPoints, leftPadding, plotWidth, topPadding, plotHeight);
        List<(WindowsPoint Point, double Value)> secondaryPoints = BuildPoints(displaySecondaryValues, min, max, maxPoints, leftPadding, plotWidth, topPadding, plotHeight);

        bool flatZero = values.All(v => Math.Abs(v) < 0.05);
        if (points.Count >= 2)
        {
            StreamGeometry geometry = BuildCurveGeometry(points.Select(p => p.Point).ToArray(), closeToBottom: false, topPadding + plotHeight);
            StreamGeometry areaGeometry = BuildCurveGeometry(points.Select(p => p.Point).ToArray(), closeToBottom: true, topPadding + plotHeight);
            geometry.Freeze();
            areaGeometry.Freeze();
            if (Fill is not null && !flatZero)
            {
                drawingContext.DrawGeometry(Fill, null, areaGeometry);
            }

            drawingContext.DrawGeometry(null, CreateCurvePen(Stroke), geometry);
        }

        if (secondaryPoints.Count >= 2)
        {
            StreamGeometry secondaryGeometry = BuildCurveGeometry(secondaryPoints.Select(p => p.Point).ToArray(), closeToBottom: false, topPadding + plotHeight);
            secondaryGeometry.Freeze();
            drawingContext.DrawGeometry(null, CreateCurvePen(SecondaryStroke), secondaryGeometry);
        }

        var axisPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(47, 54, 64)), 1);
        drawingContext.DrawLine(axisPen, new WindowsPoint(leftPadding, topPadding + plotHeight), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight));
        drawingContext.DrawLine(axisPen, new WindowsPoint(leftPadding + plotWidth, topPadding), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight));

        var textBrush = new SolidColorBrush(MediaColor.FromRgb(175, 178, 184));
        DrawLabel(drawingContext, max, textBrush, leftPadding + plotWidth + 7, topPadding - 1);
        DrawLabel(drawingContext, (max + min) / 2d, textBrush, leftPadding + plotWidth + 7, topPadding + plotHeight / 2d - 7);
        DrawLabel(drawingContext, min, textBrush, leftPadding + plotWidth + 7, topPadding + plotHeight - 14);

        if (secondaryPoints.Count > 2 && !isFlat)
        {
            DrawSeriesMaxLabel(drawingContext, points, Stroke, rect, preferAbove: true);
            DrawSeriesMaxLabel(drawingContext, secondaryPoints, SecondaryStroke, rect, preferAbove: false);
        }
        else if (points.Count > 2 && !isFlat)
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

    private static void DrawSeriesMaxLabel(DrawingContext context, IReadOnlyList<(WindowsPoint Point, double Value)> points, MediaBrush accent, Rect bounds, bool preferAbove)
    {
        if (points.Count == 0)
        {
            return;
        }

        int maxIndex = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].Value > points[maxIndex].Value)
            {
                maxIndex = i;
            }
        }

        DrawPeakLabel(context, points[maxIndex].Point, points[maxIndex].Value, accent, bounds, preferAbove);
    }

    private static List<(WindowsPoint Point, double Value)> BuildPoints(double[] values, double min, double max, int maxPoints, double leftPadding, double plotWidth, double topPadding, double plotHeight)
    {
        var points = new List<(WindowsPoint Point, double Value)>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            int slot = maxPoints - values.Length + i;
            double x = leftPadding + slot * (plotWidth - 1) / (maxPoints - 1);
            double normalized = Math.Clamp((values[i] - min) / (max - min), 0, 1);
            double y = topPadding + plotHeight - normalized * (plotHeight - 4);
            points.Add((new WindowsPoint(x, y), values[i]));
        }

        return points;
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
            8,
            new SolidColorBrush(MediaColor.FromRgb(235, 238, 242)),
            1.0);

        double width = formatted.Width + 8;
        double height = formatted.Height + 3;
        double x = Math.Clamp(point.X - width / 2d, 4, Math.Max(4, bounds.Width - width - 46));
        double y = preferAbove ? point.Y - height - 4 : point.Y + 4;
        if (y < 2)
        {
            y = point.Y + 4;
        }
        else if (y + height > bounds.Height - 16)
        {
            y = point.Y - height - 4;
        }

        var labelRect = new Rect(x, y, width, height);
        context.DrawRoundedRectangle(new SolidColorBrush(MediaColor.FromArgb(220, 24, 27, 31)), new MediaPen(accent, 1), labelRect, 3, 3);
        context.DrawText(formatted, new WindowsPoint(x + 4, y));
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
