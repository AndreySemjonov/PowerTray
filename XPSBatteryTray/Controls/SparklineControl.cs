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
        drawingContext.DrawRectangle(new SolidColorBrush(MediaColor.FromRgb(19, 22, 26)), null, rect);
        double axisWidth = 36;
        double plotWidth = Math.Max(1, ActualWidth - axisWidth);
        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(80, 88, 92, 98)), 1);
        for (int i = 1; i <= 3; i++)
        {
            double y = i * ActualHeight / 4d;
            drawingContext.DrawLine(gridPen, new WindowsPoint(0, y), new WindowsPoint(plotWidth, y));
        }

        int maxPoints = Math.Max(2, MaxPoints);
        double[] values = Values?.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(maxPoints).ToArray() ?? [];
        if (values.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            var pen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
            drawingContext.DrawLine(pen, new WindowsPoint(0, rect.Height / 2), new WindowsPoint(plotWidth, rect.Height / 2));
            return;
        }

        double min = values.Min();
        double max = values.Max();
        if (Math.Abs(max - min) < 0.001)
        {
            max = min + 1;
        }

        var geometry = new StreamGeometry();
        var areaGeometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        using (StreamGeometryContext area = areaGeometry.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                int slot = maxPoints - values.Length + i;
                double x = slot * (plotWidth - 1) / (maxPoints - 1);
                double y = ActualHeight - 3 - ((values[i] - min) / (max - min) * (ActualHeight - 6));
                if (i == 0)
                {
                    context.BeginFigure(new WindowsPoint(x, y), false, false);
                    area.BeginFigure(new WindowsPoint(x, ActualHeight - 2), true, true);
                    area.LineTo(new WindowsPoint(x, y), true, false);
                }
                else
                {
                    context.LineTo(new WindowsPoint(x, y), true, false);
                    area.LineTo(new WindowsPoint(x, y), true, false);
                }
            }

            int lastSlot = maxPoints - 1;
            double endX = lastSlot * (plotWidth - 1) / (maxPoints - 1);
            area.LineTo(new WindowsPoint(endX, ActualHeight - 2), true, false);
        }

        geometry.Freeze();
        areaGeometry.Freeze();
        if (Fill is not null)
        {
            drawingContext.DrawGeometry(Fill, null, areaGeometry);
        }

        var axisPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(47, 54, 64)), 1);
        drawingContext.DrawLine(axisPen, new WindowsPoint(0, ActualHeight - 1), new WindowsPoint(plotWidth, ActualHeight - 1));
        drawingContext.DrawLine(axisPen, new WindowsPoint(plotWidth, 0), new WindowsPoint(plotWidth, ActualHeight));
        drawingContext.DrawGeometry(null, new MediaPen(Stroke, 1.8), geometry);

        var textBrush = new SolidColorBrush(MediaColor.FromRgb(175, 178, 184));
        DrawLabel(drawingContext, max, textBrush, plotWidth + 5, 0);
        DrawLabel(drawingContext, (max + min) / 2d, textBrush, plotWidth + 5, ActualHeight / 2d - 8);
        DrawLabel(drawingContext, min, textBrush, plotWidth + 5, ActualHeight - 17);
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
}
