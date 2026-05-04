using System.Windows;
using System.Windows.Media;
using XPSBatteryTray.Models;
using MediaBrush = System.Windows.Media.Brush;
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
        if (bounds.Width < 80 || bounds.Height < 80 || buckets.Length == 0)
        {
            DrawCollecting(context, bounds);
            return;
        }

        const double leftPadding = 8;
        const double rightPadding = 46;
        const double topPadding = 26;
        const double bottomPadding = 28;
        double plotWidth = Math.Max(1, ActualWidth - leftPadding - rightPadding);
        double plotHeight = Math.Max(1, ActualHeight - topPadding - bottomPadding);
        double bottom = topPadding + plotHeight;

        DrawMissingDataBands(context, buckets, leftPadding, plotWidth, topPadding, plotHeight);
        DrawChargingBands(context, buckets, leftPadding, plotWidth, topPadding, plotHeight);
        DrawGrid(context, leftPadding, plotWidth, topPadding, plotHeight);
        DrawBars(context, buckets, leftPadding, plotWidth, topPadding, plotHeight);
        DrawCurrentMarker(context, buckets, leftPadding, plotWidth, topPadding, plotHeight);
        DrawAxisLabels(context, leftPadding + plotWidth + 10, topPadding, plotHeight);
        DrawTimeLabels(context, leftPadding, plotWidth, bottom + 7);
    }

    private static void DrawMissingDataBands(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        var bandBrush = new SolidColorBrush(MediaColor.FromArgb(42, 36, 40, 45));
        foreach ((double start, double end) in Ranges(buckets, b => b.IsMissingData))
        {
            double x = left + start * slot;
            double w = Math.Max(2, (end - start) * slot);
            context.DrawRectangle(bandBrush, null, new Rect(x, top, w, height));
        }
    }

    private static void DrawChargingBands(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        var bandBrush = new SolidColorBrush(MediaColor.FromArgb(70, 58, 122, 51));
        var markerBrush = new SolidColorBrush(MediaColor.FromRgb(163, 232, 105));
        foreach ((double start, double end) in Ranges(buckets, b => b.HasData && b.IsCharging))
        {
            double x = left + start * slot;
            double w = Math.Max(2, (end - start) * slot);
            context.DrawRoundedRectangle(bandBrush, null, new Rect(x, top, w, height), 4, 4);

            var marker = FormatText("\u26A1", 20, markerBrush, "Segoe UI Symbol");
            context.DrawText(marker, new WindowsPoint(x + w / 2d - marker.Width / 2d, Math.Max(0, top - 24)));
        }
    }

    private static IEnumerable<(double Start, double End)> Ranges(IReadOnlyList<BatteryUsageBucket> buckets, Func<BatteryUsageBucket, bool> predicate)
    {
        bool inRange = false;
        int start = 0;
        for (int i = 0; i <= buckets.Count; i++)
        {
            bool active = i < buckets.Count && predicate(buckets[i]);
            if (active && !inRange)
            {
                start = i;
                inRange = true;
            }
            else if (!active && inRange)
            {
                inRange = false;
                yield return (start, i);
            }
        }
    }

    private static void DrawGrid(DrawingContext context, double left, double width, double top, double height)
    {
        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(74, 85, 91, 99)), 1);
        for (int i = 0; i <= 2; i++)
        {
            double y = top + i * height / 2d;
            context.DrawLine(gridPen, new WindowsPoint(left, y), new WindowsPoint(left + width, y));
        }
    }

    private static void DrawBars(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        double gap = Math.Clamp(slot * 0.32, 1.2, 4);
        double barWidth = Math.Max(2.5, slot - gap);
        for (int i = 0; i < buckets.Count; i++)
        {
            BatteryUsageBucket bucket = buckets[i];
            if (!bucket.HasData)
            {
                continue;
            }

            double normalized = Math.Clamp(bucket.BatteryPercent / 100d, 0, 1);
            double barHeight = Math.Max(3, normalized * height);
            double x = left + i * slot + (slot - barWidth) / 2d;
            double y = top + height - barHeight;
            context.DrawRoundedRectangle(GetBarBrush(bucket), null, new Rect(x, y, barWidth, barHeight), 2, 2);
        }
    }

    private static void DrawCurrentMarker(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        int index = -1;
        for (int i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].IsCurrent)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        double slot = width / buckets.Count;
        double x = left + index * slot + slot / 2d;
        var markerPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(105, 116, 182, 255)), 1);
        context.DrawLine(markerPen, new WindowsPoint(x, top), new WindowsPoint(x, top + height));
    }

    private static MediaBrush GetBarBrush(BatteryUsageBucket bucket)
    {
        if (bucket.IsCharging)
        {
            return new SolidColorBrush(MediaColor.FromRgb(154, 215, 108));
        }

        if (bucket.IsCritical)
        {
            return new SolidColorBrush(MediaColor.FromRgb(214, 92, 83));
        }

        if (bucket.IsPowerSave)
        {
            return new SolidColorBrush(MediaColor.FromRgb(237, 184, 72));
        }

        return new SolidColorBrush(MediaColor.FromRgb(142, 148, 154));
    }

    private static void DrawAxisLabels(DrawingContext context, double x, double top, double height)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(188, 193, 200));
        string[] labels = ["100%", "50%", "0%"];
        for (int i = 0; i < labels.Length; i++)
        {
            double y = top + i * height / 2d - 9;
            DrawText(context, labels[i], 12, brush, x, y);
        }
    }

    private static void DrawTimeLabels(DrawingContext context, double left, double width, double y)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(174, 180, 188));
        for (int hour = 0; hour <= 24; hour += 2)
        {
            string label = hour.ToString("00");
            double x = left + width * hour / 24d;
            var formatted = FormatText(label, 11, brush);
            if (hour == 24)
            {
                x -= formatted.Width;
            }
            else if (hour > 0)
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
