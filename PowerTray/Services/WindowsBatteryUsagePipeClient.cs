using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using PowerTray.Models;

namespace PowerTray.Services;

public sealed class WindowsBatteryUsagePipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WindowsBatteryUsageSnapshot?> TryGetSnapshotAsync(DateTimeOffset? rangeStart, DateTimeOffset? rangeEnd)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", WindowsBatteryUsageIpc.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectTimeout = new CancellationTokenSource(WindowsBatteryUsageIpc.ConnectTimeout);
            await pipe.ConnectAsync(connectTimeout.Token);

            using var responseTimeout = new CancellationTokenSource(WindowsBatteryUsageIpc.ResponseTimeout);
            var request = new WindowsBatteryUsageRequest { RangeStart = rangeStart, RangeEnd = rangeEnd };
            await WriteMessageAsync(pipe, request, responseTimeout.Token);
            pipe.WaitForPipeDrain();

            return await ReadMessageAsync<WindowsBatteryUsageSnapshot>(pipe, responseTimeout.Token);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            LogService.Info($"Windows battery usage helper unavailable: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to query Windows battery usage helper.");
            return null;
        }
    }

    private static async Task WriteMessageAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBuffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
        int length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > WindowsBatteryUsageIpc.MaxMessageBytes)
        {
            throw new IOException($"Invalid helper response length: {length}.");
        }

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }
}
