# Screen dimmer regression checks

Run on an unlocked Windows desktop with the displays being tested connected:

```powershell
dotnet run --project tests/PowerTray.DimmerChecks -c Release
dotnet run --project tests/PowerTray.DimmerChecks -c Release -- --per-monitor
```

The harness uses the production dimmer service and briefly shows 1%/5% overlays.
It does not load/save user settings, register brightness hotkeys, or change display
configuration. It checks physical window rectangles against all attached monitors,
simulates a stale overlay size before the real display-change and maintenance
handlers, verifies opacity and click-through styles, and verifies dimmer-off cleanup.
The second command also exercises per-monitor DPI transitions on mixed-scale displays.

Read-only inspection of an already running PowerTray instance is also available:

```powershell
dotnet run --project tests/PowerTray.DimmerChecks -c Release -- --inspect-process <PID>
```

Exit code 0 means coverage passed, 1 means a mismatch, and 2 means that no visible
dimming overlays were found (for example, the dim level is zero).
Physical unplug/replug validation is separate from the simulated stale-size check.
