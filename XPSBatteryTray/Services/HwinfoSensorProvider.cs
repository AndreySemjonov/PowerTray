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
    private static DateTimeOffset _lastDiagnosticLog = DateTimeOffset.MinValue;

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
        if (accessor.Capacity < 44)
        {
            return Unavailable("HWiNFO shared memory header was too small");
        }

        string signature = ReadString(accessor, 0, 4);
        if (!signature.Equals("HWiS", StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(signature.Equals("DEAD", StringComparison.OrdinalIgnoreCase)
                ? "HWiNFO shared memory is inactive"
                : "HWiNFO shared memory signature was not recognized");
        }

        // Packed HWiNFO_SENSORS_SHARED_MEM2:
        // DWORD signature, DWORD version, DWORD revision, __time64_t poll_time,
        // then the section descriptors. __time64_t is 8 bytes, so descriptors start at byte 20.
        uint sensorOffset = accessor.ReadUInt32(20);
        uint sensorElementSize = accessor.ReadUInt32(24);
        uint sensorCount = accessor.ReadUInt32(28);
        uint readingOffset = accessor.ReadUInt32(32);
        uint readingElementSize = accessor.ReadUInt32(36);
        uint readingCount = accessor.ReadUInt32(40);

        if (readingOffset == 0 || readingElementSize < 64 || readingCount == 0 || readingOffset + readingElementSize > accessor.Capacity)
        {
            LogService.Info($"HWiNFO descriptors invalid. sensorOffset={sensorOffset}, sensorElementSize={sensorElementSize}, sensorCount={sensorCount}, readingOffset={readingOffset}, readingElementSize={readingElementSize}, readingCount={readingCount}, capacity={accessor.Capacity}.");
            return Unavailable("HWiNFO sensors detected but no readings were exposed");
        }

        Dictionary<uint, SensorInfo> sensors = ReadSensors(accessor, sensorOffset, sensorElementSize, sensorCount);

        double? cpuTemp = null;
        double? cpuPower = null;
        double? batteryPower = null;
        var fans = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

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
            sensors.TryGetValue(reading.SensorIndex, out SensorInfo sensor);
            string sensorName = sensor.DisplayName;
            string haystack = $"{sensorName} {name} {reading.Unit}";
            diagnostics.Add($"{reading.Type}: {sensorName} / {name} = {reading.Value:N2} {reading.Unit}");

            if (cpuTemp is null && reading.Type == ReadingType.Temperature &&
                (ContainsAny(haystack, "CPU Package", "Core Max", "Core Temperatures") ||
                 ContainsAny(sensorName, "CPU", "Intel Core", "Core Ultra", "Processor")))
            {
                cpuTemp = reading.Value;
            }
            else if (cpuPower is null && reading.Type == ReadingType.Power &&
                     (ContainsAny(haystack, "CPU Package Power", "Package Power", "IA Cores Power", "Processor Power") ||
                      (ContainsAny(sensorName, "CPU", "Intel Core", "Core Ultra", "Processor") && ContainsAny(haystack, "Power"))))
            {
                cpuPower = Math.Abs(reading.Value);
            }
            else if (batteryPower is null && reading.Type == ReadingType.Power &&
                     (ContainsAny(haystack, "Charge Rate", "Discharge Rate", "Battery Power", "Charge Power") ||
                      ContainsAny(sensorName, "Battery", "Smart Battery")))
            {
                batteryPower = reading.Value;
            }
            else if (reading.Type == ReadingType.Fan || ContainsAny(haystack, "Fan", "RPM"))
            {
                fans[name] = reading.Value;
            }
        }

        LogDiagnosticsIfNeeded(cpuTemp, cpuPower, batteryPower, fans.Count, diagnostics);

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
        // Packed HWiNFO_SENSORS_READING_ELEMENT:
        // DWORD type, DWORD sensor index, DWORD reading id, 128 label orig, 128 label user, 16 unit, 4 doubles.
        var type = elementSize >= 4 ? (ReadingType)accessor.ReadInt32(baseOffset) : ReadingType.None;
        uint sensorIndex = elementSize >= 8 ? accessor.ReadUInt32(baseOffset + 4) : 0;
        string original = elementSize >= 140 ? ReadString(accessor, baseOffset + 12, 128) : string.Empty;
        string user = elementSize >= 268 ? ReadString(accessor, baseOffset + 140, 128) : string.Empty;
        string unit = elementSize >= 284 ? ReadString(accessor, baseOffset + 268, 16) : string.Empty;
        double value = elementSize >= 292 ? accessor.ReadDouble(baseOffset + 284) : double.NaN;
        string name = string.IsNullOrWhiteSpace(user) ? original : user;
        return new Reading(sensorIndex, type, name.Trim(), unit.Trim(), value);
    }

    private static Dictionary<uint, SensorInfo> ReadSensors(MemoryMappedViewAccessor accessor, uint sensorOffset, uint sensorElementSize, uint sensorCount)
    {
        var sensors = new Dictionary<uint, SensorInfo>();
        if (sensorOffset == 0 || sensorElementSize < 264 || sensorCount == 0)
        {
            return sensors;
        }

        for (uint i = 0; i < sensorCount; i++)
        {
            long baseOffset = sensorOffset + (long)i * sensorElementSize;
            if (baseOffset < 0 || baseOffset + sensorElementSize > accessor.Capacity)
            {
                break;
            }

            string original = ReadString(accessor, baseOffset + 8, 128);
            string user = ReadString(accessor, baseOffset + 136, 128);
            string name = string.IsNullOrWhiteSpace(user) ? original : user;
            sensors[i] = new SensorInfo(name.Trim());
        }

        return sensors;
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

        return Encoding.Latin1.GetString(buffer, 0, length).Trim();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static void LogDiagnosticsIfNeeded(double? cpuTemp, double? cpuPower, double? batteryPower, int fanCount, IReadOnlyList<string> diagnostics)
    {
        if (cpuTemp is not null && cpuPower is not null && batteryPower is not null && fanCount > 0)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        if (now - _lastDiagnosticLog < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastDiagnosticLog = now;
        string sample = string.Join(Environment.NewLine, diagnostics.Take(80));
        LogService.Info($"HWiNFO detected but some expected sensors were not matched. CPU temp={cpuTemp?.ToString("N1") ?? "none"}, CPU power={cpuPower?.ToString("N1") ?? "none"}, battery power={batteryPower?.ToString("N1") ?? "none"}, fans={fanCount}.{Environment.NewLine}{sample}");
    }

    private static SensorReadings Unavailable(string status) => new() { IsAvailable = false, Status = status };

    private readonly record struct SensorInfo(string DisplayName);

    private readonly record struct Reading(uint SensorIndex, ReadingType Type, string Name, string Unit, double Value);

    private enum ReadingType
    {
        None = 0,
        Temperature = 1,
        Voltage = 2,
        Fan = 3,
        Current = 4,
        Power = 5,
        Clock = 6,
        Usage = 7,
        Other = 8
    }
}
