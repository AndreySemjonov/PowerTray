using System.IO.MemoryMappedFiles;
using System.IO;
using System.Text;
using System.Threading;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class HwinfoSensorProvider : ISensorProvider
{
    private const string MappingName = @"Global\HWiNFO_SENS_SM2";
    private const string MutexName = @"Global\HWiNFO_SM2_MUTEX";

    public string Name => "HWiNFO";

    public SensorReadings Read()
    {
        try
        {
            using MemoryMappedFile mappedFile = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
            using MemoryMappedViewAccessor accessor = mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            bool hasMutex = Mutex.TryOpenExisting(MutexName, out Mutex? mutex);
            bool lockTaken = false;
            try
            {
                if (hasMutex && mutex is not null)
                {
                    lockTaken = mutex.WaitOne(100);
                }

                return ReadFromAccessor(accessor);
            }
            finally
            {
                if (lockTaken)
                {
                    mutex?.ReleaseMutex();
                }

                mutex?.Dispose();
            }
        }
        catch (FileNotFoundException)
        {
            return Unavailable("HWiNFO shared memory not available");
        }
        catch (UnauthorizedAccessException ex)
        {
            LogService.Error(ex, "HWiNFO shared memory access denied.");
            return Unavailable("HWiNFO shared memory access denied");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read HWiNFO shared memory.");
            return Unavailable("HWiNFO sensors detected but could not be parsed");
        }
    }

    private static SensorReadings ReadFromAccessor(MemoryMappedViewAccessor accessor)
    {
        if (accessor.Capacity < 40)
        {
            return Unavailable("HWiNFO shared memory header was too small");
        }

        uint sensorOffset = accessor.ReadUInt32(16);
        uint sensorElementSize = accessor.ReadUInt32(20);
        uint sensorCount = accessor.ReadUInt32(24);
        uint readingOffset = accessor.ReadUInt32(28);
        uint readingElementSize = accessor.ReadUInt32(32);
        uint readingCount = accessor.ReadUInt32(36);

        if (readingOffset == 0 || readingElementSize < 64 || readingCount == 0 || readingOffset + readingElementSize > accessor.Capacity)
        {
            return Unavailable("HWiNFO sensors detected but no readings were exposed");
        }

        _ = sensorOffset;
        _ = sensorElementSize;
        _ = sensorCount;

        double? cpuTemp = null;
        double? cpuPower = null;
        double? batteryPower = null;
        var fans = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        for (uint i = 0; i < readingCount; i++)
        {
            long baseOffset = readingOffset + (long)i * readingElementSize;
            if (baseOffset < 0 || baseOffset + readingElementSize > accessor.Capacity)
            {
                break;
            }

            Reading reading = TryReadReading(accessor, baseOffset, readingElementSize);
            if (string.IsNullOrWhiteSpace(reading.Name) || double.IsNaN(reading.Value) || double.IsInfinity(reading.Value))
            {
                continue;
            }

            string name = reading.Name;
            string haystack = $"{name} {reading.Unit}";

            if (cpuTemp is null && ContainsAny(haystack, "CPU Package", "Core Max", "Core Temperatures") && IsTemperature(reading.Unit))
            {
                cpuTemp = reading.Value;
            }
            else if (cpuPower is null && ContainsAny(haystack, "CPU Package Power", "Package Power") && IsPower(reading.Unit))
            {
                cpuPower = Math.Abs(reading.Value);
            }
            else if (batteryPower is null && ContainsAny(haystack, "Charge Rate", "Discharge Rate", "Battery Power") && IsPower(reading.Unit))
            {
                batteryPower = reading.Value;
            }
            else if (ContainsAny(haystack, "Fan") && ContainsAny(haystack, "RPM"))
            {
                fans[name] = reading.Value;
            }
        }

        return new SensorReadings
        {
            IsAvailable = true,
            Status = "HWiNFO sensors detected",
            CpuTemperatureCelsius = cpuTemp,
            CpuPackagePowerWatts = cpuPower,
            BatteryPowerWatts = batteryPower,
            FanRpm = fans
        };
    }

    private static Reading TryReadReading(MemoryMappedViewAccessor accessor, long baseOffset, uint elementSize)
    {
        // HWiNFO has used fixed-size reading records. The offsets below are intentionally guarded
        // so layout changes degrade to "no matching sensor" instead of a crash.
        string original = ReadString(accessor, baseOffset + 16, Math.Min(128, (int)Math.Max(0, elementSize - 16)));
        string user = elementSize >= 272 ? ReadString(accessor, baseOffset + 144, 128) : string.Empty;
        string unit = elementSize >= 288 ? ReadString(accessor, baseOffset + 272, 16) : string.Empty;
        double value = elementSize >= 296 ? accessor.ReadDouble(baseOffset + 288) : double.NaN;
        string name = string.IsNullOrWhiteSpace(user) ? original : user;
        return new Reading(name.Trim(), unit.Trim(), value);
    }

    private static string ReadString(MemoryMappedViewAccessor accessor, long offset, int maxLength)
    {
        if (maxLength <= 0 || offset < 0 || offset >= accessor.Capacity)
        {
            return string.Empty;
        }

        int length = (int)Math.Min(maxLength, accessor.Capacity - offset);
        byte[] buffer = new byte[length];
        accessor.ReadArray(offset, buffer, 0, length);
        int terminator = Array.IndexOf(buffer, (byte)0);
        if (terminator >= 0)
        {
            length = terminator;
        }

        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsTemperature(string unit) =>
        unit.Contains("C", StringComparison.OrdinalIgnoreCase) || unit.Contains("°", StringComparison.OrdinalIgnoreCase);

    private static bool IsPower(string unit) =>
        unit.Contains("W", StringComparison.OrdinalIgnoreCase);

    private static SensorReadings Unavailable(string status) => new() { IsAvailable = false, Status = status };

    private readonly record struct Reading(string Name, string Unit, double Value);
}
