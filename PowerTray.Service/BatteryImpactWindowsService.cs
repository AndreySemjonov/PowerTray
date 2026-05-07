using System.ServiceProcess;
using PowerTray.Services;

namespace PowerTray.Service;

public sealed class BatteryImpactWindowsService : ServiceBase
{
    private readonly WindowsBatteryUsagePipeServer _server = new();
    private CancellationTokenSource? _stop;
    private Task? _runTask;

    public BatteryImpactWindowsService()
    {
        ServiceName = Program.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        _stop = new CancellationTokenSource();
        _runTask = _server.RunAsync(_stop.Token);
        LogService.Info("PowerTray battery impact helper service started.");
    }

    protected override void OnStop()
    {
        StopServer();
        LogService.Info("PowerTray battery impact helper service stopped.");
    }

    protected override void OnShutdown() => StopServer();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopServer();
            _server.Dispose();
        }

        base.Dispose(disposing);
    }

    private void StopServer()
    {
        _stop?.Cancel();
        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Service shutdown should not hang on a client request.
        }

        _stop?.Dispose();
        _stop = null;
        _runTask = null;
    }
}
