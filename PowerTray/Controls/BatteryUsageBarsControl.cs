using System.Windows;
using System.Windows.Media;
using PowerTray.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace PowerTray.Controls;

public sealed class BatteryUsageBarsControl : FrameworkElement
{
    public static readonly DependencyProperty BucketsProperty =
        DependencyProperty.Register(nameof(Buckets), typeof(IEnumerable<BatteryUsageBucket>), typeof(BatteryUsageBarsControl),
            new FrameworkPropertyMetadata(Array.Empty<BatteryUsageBucket>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(string), typeof(BatteryUsageBarsControl),
            new FrameworkPropertyMetadata("Level", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<BatteryUsageBucket> Buckets
    {
        get => (IEnumerable<BatteryUsageBucket>)GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    public string Mode
    {
        get => (string)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        BatteryUsageBucket[] buckets = Buckets?.ToArray() ?? [];
        if (buckets.Length == 0 || ActualWidth < 20 || ActualHeight < 20)
        {
            DrawEmptyLine(context, bounds);
            return;
        }

        bool activityMode = Mode.Equals("Activity", StringComparison.OrdinalIgnoreCase);
        double leftPadding = activityMode ? 6 : 32;
        const double rightPadding = 6;
        const double topPadding = 5;
        const double bottomLabelHeight = 20;
        double plotWidth = Math.Max(1, ActualWidth - leftPadding - rightPadding);
        double plotHeight = Math.Max(1, ActualHeight - topPadding - bottomLabelHeight);
        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(70, 88, 92, 98)), 1);
        if (!activityMode)
        {
            context.DrawLine(gridPen, new WindowsPoint(leftPadding, topPadding), new WindowsPoint(leftPadding + plotWidth, topPadding));
        }

        context.DrawLine(gridPen, new WindowsPoint(leftPadding, topPadding + plotHeight / 2), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight / 2));
        context.DrawLine(gridPen, new WindowsPoint(leftPadding, topPadding + plotHeight), new WindowsPoint(leftPadding + plotWidth, topPadding + plotHeight));

        double gap = Math.Clamp(plotWidth / buckets.Length * 0.22, 2, 6);
        double barWidth = Math.Max(3, plotWidth / buckets.Length - gap);
        double maxActivity = Math.Max(5, buckets.Max(b => Math.Max(b.DrainPercent, b.ChargePercent)));

        for (int i = 0; i < buckets.Length; i++)
        {
            BatteryUsageBucket bucket = buckets[i];
            double x = leftPadding + i * plotWidth / buckets.Length + gap / 2;
            double normalized = activityMode
                ? Math.Clamp(Math.Max(bucket.DrainPercent, bucket.ChargePercent) / maxActivity, 0, 1)
                : Math.Clamp(bucket.BatteryPercent / 100d, 0, 1);
            double height = Math.Max(bucket.BatteryPercent > 0 || activityMode ? 3 : 0, normalized * plotHeight);
            double y = topPadding + plotHeight - height;
            MediaBrush brush = GetBarBrush(bucket, activityMode);
            context.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, height), 2, 2);

            if (bucket.IsCurrent)
            {
                var markerPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(74, 168, 255)), 1);
                context.DrawLine(markerPen, new WindowsPoint(x + barWidth / 2, topPadding), new WindowsPoint(x + barWidth / 2, topPadding + plotHeight));
            }

        }

        if (!activityMode)
        {
            var axisBrush = new SolidColorBrush(MediaColor.FromRgb(185, 190, 197));
            DrawText(context, "100%", 10, axisBrush, 0, topPadding - 4);
            DrawText(context, "50%", 10, axisBrush, 6, topPadding + plotHeight / 2 - 7);
            DrawText(context, "0%", 10, axisBrush, 12, topPadding + plotHeight - 13);
        }

        DrawTimeLabels(context, buckets, leftPadding, plotWidth, topPadding + plotHeight + 5);
    }

    private static MediaBrush GetBarBrush(BatteryUsageBucket bucket, bool activityMode)
    {
        if (bucket.IsCharging || bucket.ChargePercent > bucket.DrainPercent)
        {
            return new SolidColorBrush(MediaColor.FromRgb(49, 214, 104));
        }

        if (!activityMode)
        {
            return bucket.IsCurrent
                ? new SolidColorBrush(MediaColor.FromRgb(74, 168, 255))
                : new SolidColorBrush(MediaColor.FromRgb(64, 150, 230));
        }

        if (bucket.DrainPercent >= 8 || bucket.AverageWatts < -18)
        {
            return new SolidColorBrush(MediaColor.FromRgb(255, 91, 91));
        }

        if (bucket.DrainPercent >= 3 || bucket.AverageWatts < -8)
        {
            return new SolidColorBrush(MediaColor.FromRgb(245, 170, 45));
        }

        return new SolidColorBrush(MediaColor.FromRgb(82, 88, 96));
    }

    private static void DrawEmptyLine(DrawingContext context, Rect bounds)
    {
        var pen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(62, 70, 82)), 1);
        context.DrawLine(pen, new WindowsPoint(6, bounds.Height / 2), new WindowsPoint(Math.Max(6, bounds.Width - 6), bounds.Height / 2));
    }

    private static void DrawText(DrawingContext context, string text, double size, MediaBrush brush, double x, double y)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            1.0);
        context.DrawText(formatted, new WindowsPoint(x, y));
    }

    private static void DrawTimeLabels(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double y)
    {
        int[] positions = [0, buckets.Count / 4, buckets.Count / 2, buckets.Count * 3 / 4, buckets.Count - 1];
        var brush = new SolidColorBrush(MediaColor.FromRgb(163, 169, 176));
        for (int i = 0; i < positions.Length; i++)
        {
            int bucketIndex = Math.Clamp(positions[i], 0, buckets.Count - 1);
            string label = buckets[bucketIndex].Label;
            double x = left + i * width / (positions.Length - 1);
            var formatted = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9,
                brush,
                1.0);
            if (i == positions.Length - 1)
            {
                x -= formatted.Width;
            }
            else if (i > 0)
            {
                x -= formatted.Width / 2d;
            }

            context.DrawText(formatted, new WindowsPoint(x, y));
        }
    }
}
