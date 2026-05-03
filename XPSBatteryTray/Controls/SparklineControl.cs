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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(new SolidColorBrush(MediaColor.FromRgb(23, 27, 33)), null, rect);

        int maxPoints = Math.Max(2, MaxPoints);
        double[] values = Values?.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(maxPoints).ToArray() ?? [];
        if (values.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            var pen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
            drawingContext.DrawLine(pen, new WindowsPoint(0, rect.Height / 2), new WindowsPoint(rect.Width, rect.Height / 2));
            return;
        }

        double min = values.Min();
        double max = values.Max();
        if (Math.Abs(max - min) < 0.001)
        {
            max = min + 1;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                int slot = maxPoints - values.Length + i;
                double x = slot * (ActualWidth - 1) / (maxPoints - 1);
                double y = ActualHeight - 3 - ((values[i] - min) / (max - min) * (ActualHeight - 6));
                if (i == 0)
                {
                    context.BeginFigure(new WindowsPoint(x, y), false, false);
                }
                else
                {
                    context.LineTo(new WindowsPoint(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        drawingContext.DrawLine(new MediaPen(new SolidColorBrush(MediaColor.FromRgb(47, 54, 64)), 1), new WindowsPoint(0, ActualHeight - 1), new WindowsPoint(ActualWidth, ActualHeight - 1));
        drawingContext.DrawGeometry(null, new MediaPen(Stroke, 1.6), geometry);
    }
}
