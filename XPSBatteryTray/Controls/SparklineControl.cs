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

        double min = values.Min();
        double max = values.Max();
        bool isFlat = Math.Abs(max - min) < 0.001;
        if (isFlat)
        {
            max = min + 1;
        }

        var geometry = new StreamGeometry();
        var areaGeometry = new StreamGeometry();
        var points = new List<(WindowsPoint Point, double Value)>(values.Length);
        using (StreamGeometryContext context = geometry.Open())
        using (StreamGeometryContext area = areaGeometry.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                int slot = maxPoints - values.Length + i;
                double x = leftPadding + slot * (plotWidth - 1) / (maxPoints - 1);
                double y = topPadding + plotHeight - ((values[i] - min) / (max - min) * (plotHeight - 4));
                var point = new WindowsPoint(x, y);
                points.Add((point, values[i]));
                if (i == 0)
                {
                    context.BeginFigure(point, false, false);
                    area.BeginFigure(new WindowsPoint(x, topPadding + plotHeight), true, true);
                    area.LineTo(point, true, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                    area.LineTo(point, true, false);
                }
            }

            int lastSlot = maxPoints - 1;
            double endX = leftPadding + lastSlot * (plotWidth - 1) / (maxPoints - 1);
            area.LineTo(new WindowsPoint(endX, topPadding + plotHeight), true, false);
        }

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
        drawingContext.DrawGeometry(null, new MediaPen(Stroke, 1.8), geometry);

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
