using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsSize = System.Windows.Size;
using WindowsPoint = System.Windows.Point;

namespace XPSBatteryTray.Controls;

public sealed class RingGaugeControl : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGaugeControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(MediaBrush), typeof(RingGaugeControl),
            new FrameworkPropertyMetadata(MediaBrushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public MediaBrush Accent
    {
        get => (MediaBrush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20)
        {
            return;
        }

        var center = new WindowsPoint(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - 8;
        double thickness = Math.Max(7, size * 0.09);
        drawingContext.DrawEllipse(null, new MediaPen(new SolidColorBrush(MediaColor.FromRgb(58, 60, 64)), thickness), center, radius, radius);

        double percent = Math.Clamp(Value, 0, 100);
        if (percent <= 0.01)
        {
            return;
        }

        double startAngle = -90;
        double endAngle = startAngle + (percent / 100d * 360d);
        WindowsPoint start = PointOnCircle(center, radius, startAngle);
        WindowsPoint end = PointOnCircle(center, radius, endAngle);
        bool isLargeArc = percent > 50;

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new WindowsSize(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        drawingContext.DrawGeometry(null, new MediaPen(Accent, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }

    private static WindowsPoint PointOnCircle(WindowsPoint center, double radius, double angleDegrees)
    {
        double angle = angleDegrees * Math.PI / 180d;
        return new WindowsPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
}
