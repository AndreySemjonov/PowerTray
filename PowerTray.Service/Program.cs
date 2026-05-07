using System.ServiceProcess;
using PowerTray.Services;

namespace PowerTray.Service;

internal static class Program
{
    public const string ServiceName = "PowerTrayBatteryImpact";

    public static async Task Main(string[] args)
    {
        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase) || Environment.UserInteractive)
        {
            using var server = new WindowsBatteryUsagePipeServer();
            Console.WriteLine("PowerTray battery impact helper is running. Press Ctrl+C to stop.");
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Cancel();
            };

            await server.RunAsync(stop.Token);
            return;
        }

        ServiceBase.Run(new BatteryImpactWindowsService());
    }
}
