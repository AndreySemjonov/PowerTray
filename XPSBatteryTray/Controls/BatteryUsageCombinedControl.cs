using System.Windows;
using System.Windows.Media;
using XPSBatteryTray.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace XPSBatteryTray.Controls;

public sealed class BatteryUsageCombinedControl : FrameworkElement
{
    public static readonly DependencyProperty BucketsProperty =
        DependencyProperty.Register(nameof(Buckets), typeof(IEnumerable<BatteryUsageBucket>), typeof(BatteryUsageCombinedControl),
            new FrameworkPropertyMetadata(Array.Empty<BatteryUsageBucket>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<BatteryUsageBucket> Buckets
    {
        get => (IEnumerable<BatteryUsageBucket>)GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        BatteryUsageBucket[] buckets = Buckets?.ToArray() ?? [];
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 40 || bounds.Height < 40 || buckets.Count(b => b.HasData) < 2)
        {
            DrawCollecting(context, bounds);
            return;
        }

        const double leftPadding = 34;
        const double rightPadding = 34;
        const double topPadding = 18;
        const double bottomPadding = 22;
        double plotWidth = Math.Max(1, ActualWidth - leftPadding - rightPadding);
        double plotHeight = Math.Max(1, ActualHeight - topPadding - bottomPadding);
        double bottom = topPadding + plotHeight;
        double activityBand = plotHeight * 0.28;

        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(70, 92, 98, 106)), 1);
        for (int i = 0; i <= 2; i++)
        {
            double y = topPadding + i * plotHeight / 2d;
            context.DrawLine(gridPen, new WindowsPoint(leftPadding, y), new WindowsPoint(leftPadding + plotWidth, y));
        }

        DrawAxisLabels(context, leftPadding, left: true, topPadding, plotHeight);
        DrawAxisLabels(context, leftPadding + plotWidth + 6, left: false, topPadding, plotHeight);
        DrawChargingBands(context, buckets, leftPadding, plotWidth, topPadding, plotHeight);
        DrawBatteryAreaAndLine(context, buckets, leftPadding, plotWidth, topPadding, plotHeight);
        DrawActivityBars(context, buckets, leftPadding, plotWidth, bottom, activityBand);
        DrawTimeLabels(context, buckets, leftPadding, plotWidth, bottom + 5);
    }

    private static void DrawChargingBands(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        var bandBrush = new SolidColorBrush(MediaColor.FromArgb(70, 60, 205, 92));
        var markerBrush = new SolidColorBrush(MediaColor.FromRgb(105, 225, 104));
        bool inBand = false;
        double bandStart = 0;
        for (int i = 0; i <= buckets.Count; i++)
        {
            bool charging = i < buckets.Count && buckets[i].HasData && (buckets[i].IsCharging || buckets[i].ChargePercent > buckets[i].DrainPercent);
            if (charging && !inBand)
            {
                inBand = true;
                bandStart = left + i * slot;
            }
            else if (!charging && inBand)
            {
                inBand = false;
                double bandEnd = left + i * slot;
                context.DrawRectangle(bandBrush, null, new Rect(bandStart, top, Math.Max(2, bandEnd - bandStart), height));
                DrawText(context, "⚡", 15, markerBrush, bandStart + (bandEnd - bandStart) / 2d - 6, top - 3, "Segoe UI Semibold");
            }
        }
    }

    private static void DrawBatteryAreaAndLine(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        var lineGeometry = new StreamGeometry();
        var areaGeometry = new StreamGeometry();
        using (StreamGeometryContext line = lineGeometry.Open())
        using (StreamGeometryContext area = areaGeometry.Open())
        {
            for (int i = 0; i < buckets.Count; i++)
            {
                WindowsPoint point = PointForBucket(buckets, i, left, width, top, height);
                if (i == 0)
                {
                    line.BeginFigure(point, false, false);
                    area.BeginFigure(new WindowsPoint(point.X, top + height), true, true);
                    area.LineTo(point, true, false);
                }
                else
                {
                    line.LineTo(point, true, false);
                    area.LineTo(point, true, false);
                }
            }

            WindowsPoint end = PointForBucket(buckets, buckets.Count - 1, left, width, top, height);
            area.LineTo(new WindowsPoint(end.X, top + height), true, false);
        }

        lineGeometry.Freeze();
        areaGeometry.Freeze();
        var fill = new LinearGradientBrush(MediaColor.FromArgb(80, 70, 150, 255), MediaColor.FromArgb(18, 70, 150, 255), 90);
        var stroke = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(103, 159, 255)), 2);
        context.DrawGeometry(fill, null, areaGeometry);
        context.DrawGeometry(null, stroke, lineGeometry);
    }

    private static void DrawActivityBars(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double bottom, double bandHeight)
    {
        double slot = width / buckets.Count;
        double barWidth = Math.Max(2, slot * 0.7);
        double maxActivity = Math.Max(1, buckets.Max(b => Math.Max(Math.Max(b.DrainPercent, b.ChargePercent), Math.Abs(b.AverageWatts) / 4d)));
        for (int i = 0; i < buckets.Count; i++)
        {
            BatteryUsageBucket bucket = buckets[i];
            if (!bucket.HasData)
            {
                continue;
            }

            double activity = Math.Max(Math.Max(bucket.DrainPercent, bucket.ChargePercent), Math.Abs(bucket.AverageWatts) / 4d);
            double barHeight = Math.Clamp(activity / maxActivity, 0.04, 1) * bandHeight;
            double x = left + i * slot + (slot - barWidth) / 2d;
            double y = bottom - barHeight;
            context.DrawRoundedRectangle(GetActivityBrush(bucket), null, new Rect(x, y, barWidth, barHeight), 1.5, 1.5);
        }
    }

    private static MediaBrush GetActivityBrush(BatteryUsageBucket bucket)
    {
        if (bucket.IsCharging || bucket.ChargePercent > bucket.DrainPercent)
        {
            return new SolidColorBrush(MediaColor.FromRgb(90, 220, 105));
        }

        double intensity = Math.Max(bucket.DrainPercent, Math.Abs(bucket.AverageWatts) / 4d);
        if (intensity >= 5)
        {
            return new SolidColorBrush(MediaColor.FromRgb(255, 91, 91));
        }

        if (intensity >= 2)
        {
            return new SolidColorBrush(MediaColor.FromRgb(245, 170, 45));
        }

        return new SolidColorBrush(MediaColor.FromRgb(86, 105, 126));
    }

    private static WindowsPoint PointForBucket(IReadOnlyList<BatteryUsageBucket> buckets, int index, double left, double width, double top, double height)
    {
        double x = left + index * width / Math.Max(1, buckets.Count - 1);
        double y = top + height - Math.Clamp(buckets[index].BatteryPercent, 0, 100) / 100d * height;
        return new WindowsPoint(x, y);
    }

    private static void DrawAxisLabels(DrawingContext context, double x, bool left, double top, double height)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(184, 190, 198));
        string[] labels = ["100%", "50%", "0%"];
        for (int i = 0; i < labels.Length; i++)
        {
            double y = top + i * height / 2d - 8;
            double labelX = left ? x - 34 : x;
            DrawText(context, labels[i], 10, brush, labelX, y);
        }
    }

    private static void DrawTimeLabels(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double y)
    {
        int[] positions = [0, buckets.Count / 4, buckets.Count / 2, buckets.Count * 3 / 4, buckets.Count - 1];
        var brush = new SolidColorBrush(MediaColor.FromRgb(170, 176, 184));
        for (int i = 0; i < positions.Length; i++)
        {
            int bucketIndex = Math.Clamp(positions[i], 0, buckets.Count - 1);
            string label = buckets[bucketIndex].Label;
            double x = left + i * width / (positions.Length - 1);
            var formatted = FormatText(label, 10, brush);
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

    private static void DrawCollecting(DrawingContext context, Rect bounds)
    {
        var pen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(80, 92, 98, 106)), 1);
        context.DrawLine(pen, new WindowsPoint(12, bounds.Height / 2d), new WindowsPoint(Math.Max(12, bounds.Width - 12), bounds.Height / 2d));
        var brush = new SolidColorBrush(MediaColor.FromRgb(175, 181, 190));
        var text = FormatText("Collecting battery usage data...", 12, brush);
        context.DrawText(text, new WindowsPoint(Math.Max(8, bounds.Width / 2d - text.Width / 2d), bounds.Height / 2d - text.Height - 8));
    }

    private static void DrawText(DrawingContext context, string text, double size, MediaBrush brush, double x, double y, string typeface = "Segoe UI")
    {
        context.DrawText(FormatText(text, size, brush, typeface), new WindowsPoint(x, y));
    }

    private static FormattedText FormatText(string text, double size, MediaBrush brush, string typeface = "Segoe UI") =>
        new(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(typeface),
            size,
            brush,
            1.0);
}
