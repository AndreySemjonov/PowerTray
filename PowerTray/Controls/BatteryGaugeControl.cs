using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;

namespace XPSBatteryTray.Controls;

public sealed class BatteryGaugeControl : FrameworkElement
{
    public static readonly DependencyProperty PercentageProperty =
        DependencyProperty.Register(nameof(Percentage), typeof(double), typeof(BatteryGaugeControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(MediaBrush), typeof(BatteryGaugeControl),
            new FrameworkPropertyMetadata(MediaBrushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percentage
    {
        get => (double)GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public MediaBrush Accent
    {
        get => (MediaBrush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 24 || height < 40)
        {
            return;
        }

        var outlinePen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(82, 86, 92)), 3);
        double nubWidth = width * 0.34;
        var nub = new Rect((width - nubWidth) / 2, 1.5, nubWidth, 12);
        var body = new Rect(5, 12, width - 10, height - 15);
        drawingContext.DrawRoundedRectangle(null, outlinePen, nub, 4, 4);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(MediaColor.FromRgb(26, 29, 33)), outlinePen, body, 8, 8);

        double fillRatio = Math.Clamp(Percentage, 0, 100) / 100d;
        var fill = new Rect(body.Left + 8, body.Bottom - 8 - ((body.Height - 16) * fillRatio), body.Width - 16, (body.Height - 16) * fillRatio);
        drawingContext.DrawRoundedRectangle(Accent, null, fill, 3, 3);
    }
}
