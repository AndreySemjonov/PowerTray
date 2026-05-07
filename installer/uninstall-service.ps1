$serviceName = "PowerTrayBatteryImpact"

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) {
    return
}

if ($service.Status -ne "Stopped") {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    try {
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(15))
    }
    catch {
        # Continue with service deletion during uninstall.
    }
}

& sc.exe delete $serviceName | Out-Null
