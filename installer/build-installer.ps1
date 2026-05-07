$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishRoot = Join-Path $repoRoot ".private\installer-publish"
$appPublish = Join-Path $publishRoot "PowerTray"
$servicePublish = Join-Path $publishRoot "PowerTray.Service"
$issPath = Join-Path $PSScriptRoot "PowerTray.iss"

function Get-Net8RuntimeVersion {
    $runtime = dotnet --list-runtimes |
        Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' } |
        Select-Object -First 1
    if ($runtime -match '^Microsoft\.NETCore\.App (?<version>\S+)') {
        return $Matches.version
    }

    throw "Microsoft.NETCore.App 8.x runtime was not found."
}

function Get-AppHostTemplate {
    $runtimeVersion = Get-Net8RuntimeVersion
    $packRoot = Join-Path $env:ProgramFiles "dotnet\packs\Microsoft.NETCore.App.Host.win-x64"
    $localTemplate = Join-Path $packRoot "$runtimeVersion\runtimes\win-x64\native\apphost.exe"
    if (Test-Path $localTemplate) {
        return $localTemplate
    }

    $cacheRoot = Join-Path $repoRoot ".private\apphost-cache\$runtimeVersion"
    $cachedTemplate = Join-Path $cacheRoot "runtimes\win-x64\native\apphost.exe"
    if (Test-Path $cachedTemplate) {
        return $cachedTemplate
    }

    New-Item -ItemType Directory -Force $cacheRoot | Out-Null
    $packagePath = Join-Path $cacheRoot "Microsoft.NETCore.App.Host.win-x64.$runtimeVersion.nupkg"
    $zipPath = Join-Path $cacheRoot "Microsoft.NETCore.App.Host.win-x64.$runtimeVersion.zip"
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.NETCore.App.Host.win-x64/$runtimeVersion" -OutFile $packagePath
    Copy-Item $packagePath $zipPath -Force
    Expand-Archive -LiteralPath $zipPath -DestinationPath $cacheRoot -Force
    if (-not (Test-Path $cachedTemplate)) {
        throw "Could not find apphost.exe in the downloaded Microsoft.NETCore.App.Host.win-x64 $runtimeVersion package."
    }

    return $cachedTemplate
}

function Set-WindowsGuiSubsystem {
    param([Parameter(Mandatory = $true)][string]$Path)

    [byte[]]$bytes = [IO.File]::ReadAllBytes($Path)
    $peHeaderOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $optionalHeaderOffset = $peHeaderOffset + 24
    $subsystemOffset = $optionalHeaderOffset + 0x44
    [byte[]]$subsystem = [BitConverter]::GetBytes([UInt16]2)
    [Array]::Copy($subsystem, 0, $bytes, $subsystemOffset, $subsystem.Length)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-PowerTrayAppHost {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$DllName
    )

    $template = Get-AppHostTemplate
    Copy-Item $template $OutputPath -Force

    $placeholder = [Text.Encoding]::UTF8.GetBytes("c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2")
    [byte[]]$bytes = [IO.File]::ReadAllBytes($OutputPath)
    $index = -1
    for ($i = 0; $i -le $bytes.Length - $placeholder.Length; $i++) {
        $matches = $true
        for ($j = 0; $j -lt $placeholder.Length; $j++) {
            if ($bytes[$i + $j] -ne $placeholder[$j]) {
                $matches = $false
                break
            }
        }

        if ($matches) {
            $index = $i
            break
        }
    }

    if ($index -lt 0) {
        throw "Could not find the apphost placeholder in $template."
    }

    [byte[]]$dllBytes = [Text.Encoding]::UTF8.GetBytes($DllName)
    if ($dllBytes.Length + 1 -gt $placeholder.Length) {
        throw "Apphost DLL name is too long: $DllName."
    }

    [Array]::Clear($bytes, $index, $placeholder.Length)
    [Array]::Copy($dllBytes, 0, $bytes, $index, $dllBytes.Length)
    [IO.File]::WriteAllBytes($OutputPath, $bytes)
    Set-WindowsGuiSubsystem -Path $OutputPath
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PowerTrayResourceUpdater {
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    public static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
}
"@

function Read-IcoImages {
    param([Parameter(Mandatory = $true)][string]$IconPath)

    [byte[]]$bytes = [IO.File]::ReadAllBytes($IconPath)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    $images = @()
    for ($i = 0; $i -lt $count; $i++) {
        $p = 6 + ($i * 16)
        $size = [BitConverter]::ToUInt32($bytes, $p + 8)
        $offset = [BitConverter]::ToUInt32($bytes, $p + 12)
        $image = New-Object byte[] $size
        [Array]::Copy($bytes, [int]$offset, $image, 0, [int]$size)
        $images += [pscustomobject]@{
            Width = $bytes[$p]
            Height = $bytes[$p + 1]
            ColorCount = $bytes[$p + 2]
            Planes = [BitConverter]::ToUInt16($bytes, $p + 4)
            BitCount = [BitConverter]::ToUInt16($bytes, $p + 6)
            Bytes = [byte[]]$image
            Id = [UInt16]($i + 1)
        }
    }

    return $images
}

function New-GroupIconData {
    param([Parameter(Mandatory = $true)]$Images)

    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$Images.Count)
    foreach ($image in $Images) {
        $writer.Write([byte]$image.Width)
        $writer.Write([byte]$image.Height)
        $writer.Write([byte]$image.ColorCount)
        $writer.Write([byte]0)
        $writer.Write([UInt16]$image.Planes)
        $writer.Write([UInt16]$image.BitCount)
        $writer.Write([UInt32]$image.Bytes.Length)
        $writer.Write([UInt16]$image.Id)
    }

    [byte[]]$data = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return ,$data
}

function Set-ExeIcon {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [Parameter(Mandatory = $true)][string]$IconPath
    )

    $images = Read-IcoImages -IconPath $IconPath
    [byte[]]$group = New-GroupIconData -Images $images
    $handle = [PowerTrayResourceUpdater]::BeginUpdateResource($ExePath, $false)
    if ($handle -eq [IntPtr]::Zero) {
        throw "BeginUpdateResource failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }

    try {
        foreach ($image in $images) {
            if (-not [PowerTrayResourceUpdater]::UpdateResource($handle, [IntPtr]3, [IntPtr]$image.Id, 0, $image.Bytes, $image.Bytes.Length)) {
                throw "Update RT_ICON failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
            }
        }

        foreach ($groupId in @(1, 32512)) {
            if (-not [PowerTrayResourceUpdater]::UpdateResource($handle, [IntPtr]14, [IntPtr]$groupId, 0, $group, $group.Length)) {
                throw "Update RT_GROUP_ICON failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
            }
        }
    }
    catch {
        [PowerTrayResourceUpdater]::EndUpdateResource($handle, $true) | Out-Null
        throw
    }

    if (-not [PowerTrayResourceUpdater]::EndUpdateResource($handle, $false)) {
        throw "EndUpdateResource failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
}

function Find-InnoCompiler {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6, then rerun this script."
}

if (Test-Path $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Force $appPublish, $servicePublish | Out-Null

dotnet publish (Join-Path $repoRoot "PowerTray\PowerTray.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:UseAppHost=false `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $appPublish

$appExe = Join-Path $appPublish "PowerTray.exe"
New-PowerTrayAppHost -OutputPath $appExe -DllName "PowerTray.dll"
Set-ExeIcon -ExePath $appExe -IconPath (Join-Path $repoRoot "PowerTray\Assets\AppIcon.ico")

dotnet publish (Join-Path $repoRoot "PowerTray.Service\PowerTray.Service.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:UseAppHost=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $servicePublish

$iscc = Find-InnoCompiler
& $iscc $issPath

Get-Item (Join-Path $repoRoot ".private\dist\PowerTraySetup.exe")
