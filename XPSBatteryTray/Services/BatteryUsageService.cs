using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class BatteryUsageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _historyPath = Path.Combine(LogService.AppDataRoot, "battery-usage-history.json");
    private BatteryUsageState _state = new();
    private bool _loaded;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastRecordTime;
    private bool? _lastPluggedIn;

    public BatteryUsageSnapshot Record(BatteryStatus battery)
    {
        EnsureLoaded();
        DateTimeOffset now = DateTimeOffset.Now;

        if (_state.SessionStart == default || (!_lastPluggedIn.GetValueOrDefault(battery.IsPluggedIn) && battery.IsPluggedIn))
        {
            StartSession(now);
        }

        if (_lastPluggedIn == true && !battery.IsPluggedIn)
        {
            StartSession(now);
        }

        AddActivityTime(now);
        AddOrUpdateSample(now, battery);
        _lastPluggedIn = battery.IsPluggedIn;
        SaveIfNeeded(now, force: false);
        return BuildSnapshot(now);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (File.Exists(_historyPath))
            {
                _state = JsonSerializer.Deserialize<BatteryUsageState>(File.ReadAllText(_historyPath), JsonOptions) ?? new BatteryUsageState();
                DateTimeOffset cutoff = DateTimeOffset.Now.AddDays(-7);
                _state.Samples = _state.Samples.Where(s => s.Timestamp >= cutoff).ToList();
                _lastPluggedIn = _state.Samples.LastOrDefault()?.IsPluggedIn;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to load battery usage history; starting fresh.");
            _state = new BatteryUsageState();
        }
    }

    private void StartSession(DateTimeOffset now)
    {
        _state = new BatteryUsageState
        {
            SessionStart = now,
            ActiveSeconds = 0,
            IdleSeconds = 0
        };
        _lastRecordTime = null;
        SaveIfNeeded(now, force: true);
    }

    private void AddActivityTime(DateTimeOffset now)
    {
        if (_lastRecordTime is not { } lastRecordTime)
        {
            _lastRecordTime = now;
            return;
        }

        double elapsedSeconds = Math.Clamp((now - lastRecordTime).TotalSeconds, 0, 300);
        if (elapsedSeconds <= 0)
        {
            return;
        }

        if (GetIdleTime() < TimeSpan.FromMinutes(2))
        {
            _state.ActiveSeconds += elapsedSeconds;
        }
        else
        {
            _state.IdleSeconds += elapsedSeconds;
        }

        _lastRecordTime = now;
    }

    private void AddOrUpdateSample(DateTimeOffset now, BatteryStatus battery)
    {
        var sample = new BatteryUsageSample
        {
            Timestamp = now,
            BatteryPercent = Math.Clamp(battery.Percentage, 0, 100),
            IsPluggedIn = battery.IsPluggedIn,
            BatteryWatts = battery.ChargeRateWatts
        };

        BatteryUsageSample? last = _state.Samples.LastOrDefault();
        if (last is not null && now - last.Timestamp < TimeSpan.FromSeconds(10))
        {
            sample.Timestamp = last.Timestamp;
            _state.Samples[^1] = sample;
        }
        else
        {
            _state.Samples.Add(sample);
        }

        DateTimeOffset cutoff = now.AddDays(-7);
        _state.Samples.RemoveAll(s => s.Timestamp < cutoff);
    }

    private BatteryUsageSnapshot BuildSnapshot(DateTimeOffset now)
    {
        IReadOnlyList<BatteryUsageBucket> buckets = BuildBuckets(now);
        TimeSpan age = now - _state.SessionStart;
        string sessionText = FormatDuration(age);
        return new BatteryUsageSnapshot
        {
            Title = $"Battery Usage Since Charge ({sessionText})",
            SessionText = sessionText,
            ActiveText = FormatDuration(TimeSpan.FromSeconds(_state.ActiveSeconds)),
            IdleText = FormatDuration(TimeSpan.FromSeconds(_state.IdleSeconds)),
            EstimatedDrainText = FormatMilliWattHours(EstimateDrainMilliWattHours()),
            Buckets = buckets
        };
    }

    private IReadOnlyList<BatteryUsageBucket> BuildBuckets(DateTimeOffset now)
    {
        DateTimeOffset start = _state.SessionStart == default ? now.AddHours(-1) : _state.SessionStart;
        TimeSpan age = now - start;
        TimeSpan visibleWindow = TimeSpan.FromTicks(Math.Min(TimeSpan.FromHours(2).Ticks, Math.Max(TimeSpan.FromMinutes(10).Ticks, age.Ticks)));
        const int bucketCount = 24;
        TimeSpan bucketSize = TimeSpan.FromTicks(visibleWindow.Ticks / bucketCount);
        DateTimeOffset chartStart = now - visibleWindow;

        var buckets = new List<BatteryUsageBucket>(bucketCount);
        int firstSessionPercent = _state.Samples
            .Where(s => s.Timestamp >= start)
            .OrderBy(s => s.Timestamp)
            .FirstOrDefault()?.BatteryPercent ?? 0;
        int lastKnownPercent = _state.Samples
            .Where(s => s.Timestamp < chartStart)
            .OrderBy(s => s.Timestamp)
            .LastOrDefault()?.BatteryPercent ?? firstSessionPercent;
        for (int i = 0; i < bucketCount; i++)
        {
            DateTimeOffset bucketStart = chartStart + TimeSpan.FromTicks(bucketSize.Ticks * i);
            DateTimeOffset bucketEnd = bucketStart + bucketSize;
            BatteryUsageSample[] samples = _state.Samples
                .Where(s => s.Timestamp >= bucketStart && s.Timestamp < bucketEnd)
                .OrderBy(s => s.Timestamp)
                .ToArray();

            if (samples.Length == 0)
            {
                buckets.Add(new BatteryUsageBucket
                {
                    Start = bucketStart,
                    Label = BuildBucketLabel(bucketStart, now),
                    BatteryPercent = lastKnownPercent,
                    IsCurrent = i == bucketCount - 1
                });
                continue;
            }

            int firstPercent = samples.First().BatteryPercent;
            int lastPercent = samples.Last().BatteryPercent;
            lastKnownPercent = lastPercent;
            double delta = lastPercent - firstPercent;
            double averageWatts = samples.Where(s => s.BatteryWatts.HasValue).Select(s => s.BatteryWatts!.Value).DefaultIfEmpty(0).Average();
            bool charging = samples.Count(s => s.IsPluggedIn || s.BatteryWatts is > 0.5) > samples.Length / 2;
            double drainPercent = Math.Max(0, -delta);
            double chargePercent = Math.Max(0, delta);
            if (drainPercent < 0.05 && averageWatts < -0.05)
            {
                drainPercent = Math.Clamp(Math.Abs(averageWatts) / 4d, 0.4, 8);
            }
            else if (chargePercent < 0.05 && averageWatts > 0.05)
            {
                chargePercent = Math.Clamp(averageWatts / 10d, 0.4, 8);
            }

            buckets.Add(new BatteryUsageBucket
            {
                Start = bucketStart,
                Label = BuildBucketLabel(bucketStart, now),
                BatteryPercent = lastPercent,
                DrainPercent = drainPercent,
                ChargePercent = chargePercent,
                AverageWatts = averageWatts,
                IsCharging = charging,
                IsCurrent = i == bucketCount - 1,
                HasData = true
            });
        }

        return buckets;
    }

    private void SaveIfNeeded(DateTimeOffset now, bool force)
    {
        if (!force && now - _lastSave < TimeSpan.FromSeconds(30))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogService.AppDataRoot);
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(_state, JsonOptions));
            _lastSave = now;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to save battery usage history.");
        }
    }

    private static string BuildBucketLabel(DateTimeOffset bucketStart, DateTimeOffset now)
    {
        TimeSpan age = now - bucketStart;
        if (age.TotalSeconds < 45)
        {
            return "Now";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))}m";
        }

        if (age.TotalHours < 6)
        {
            return $"{Math.Round(age.TotalHours, 1):0.#}h";
        }

        return bucketStart.ToString("HH");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(0, (int)Math.Round(duration.TotalMinutes))}m";
    }

    private double EstimateDrainMilliWattHours()
    {
        BatteryUsageSample[] samples = _state.Samples
            .Where(s => s.Timestamp >= _state.SessionStart)
            .OrderBy(s => s.Timestamp)
            .ToArray();
        if (samples.Length < 2)
        {
            return 0;
        }

        double total = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1].BatteryWatts is not { } previousWatts || samples[i].BatteryWatts is not { } currentWatts)
            {
                continue;
            }

            double averageWatts = (previousWatts + currentWatts) / 2d;
            if (averageWatts >= -0.05)
            {
                continue;
            }

            double hours = Math.Clamp((samples[i].Timestamp - samples[i - 1].Timestamp).TotalHours, 0, 5d / 60d);
            total += Math.Abs(averageWatts) * hours * 1000d;
        }

        return total;
    }

    private static string FormatMilliWattHours(double value)
    {
        if (value >= 1000)
        {
            return $"{value / 1000d:N1} Wh";
        }

        return $"{value:N1} mWh";
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        uint tickCount = GetTickCount();
        uint idleMilliseconds = tickCount - info.DwTime;
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    private sealed class BatteryUsageState
    {
        public DateTimeOffset SessionStart { get; set; }
        public double ActiveSeconds { get; set; }
        public double IdleSeconds { get; set; }
        public List<BatteryUsageSample> Samples { get; set; } = [];
    }

    private sealed class BatteryUsageSample
    {
        public DateTimeOffset Timestamp { get; set; }
        public int BatteryPercent { get; set; }
        public bool IsPluggedIn { get; set; }
        public double? BatteryWatts { get; set; }
    }
}
