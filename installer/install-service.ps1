param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"
$serviceName = "PowerTrayBatteryImpact"
$displayName = "PowerTray Battery Impact Helper"
$description = "Reads Windows battery impact data for PowerTray."
$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$serviceDll = Join-Path $InstallDir "PowerTray.Service.dll"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "Administrator access is required to install the PowerTray helper service."
}

if (-not (Test-Path $dotnet)) {
    throw ".NET runtime host was not found at $dotnet."
}

if (-not (Test-Path $serviceDll)) {
    throw "PowerTray service file was not found at $serviceDll."
}

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        $existingService.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(15))
    }

    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to delete existing $serviceName service. sc.exe exit code: $LASTEXITCODE."
    }

    Start-Sleep -Milliseconds 900
}

$binPath = "`"$dotnet`" `"$serviceDll`""
New-Service -Name $serviceName `
    -BinaryPathName $binPath `
    -DisplayName $displayName `
    -Description $description `
    -StartupType Automatic | Out-Null

Start-Service -Name $serviceName -ErrorAction Stop
$service = Get-Service -Name $serviceName -ErrorAction Stop
$service.WaitForStatus("Running", [TimeSpan]::FromSeconds(20))
