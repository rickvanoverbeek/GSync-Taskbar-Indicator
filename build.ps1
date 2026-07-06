<#
.SYNOPSIS
    Build the G-Sync Taskbar Indicator: publish a self-contained single-file exe
    and (if Inno Setup is installed) compile the Windows installer.

.DESCRIPTION
    Outputs:
      dist\GSyncIndicator.exe                     (portable, self-contained, no .NET needed)
      dist\GSyncIndicator-Setup-<version>.exe     (installer, if Inno Setup is present)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$project    = Join-Path $root 'GSyncIndicator\GSyncIndicator.csproj'
$publishDir = Join-Path $root 'GSyncIndicator\bin\Release\net8.0-windows\win-x64\publish'
$dist       = Join-Path $root 'dist'

Write-Host '==> Checking for the .NET SDK...' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

Write-Host '==> Publishing self-contained single-file exe...' -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $publishDir 'GSyncIndicator.exe') (Join-Path $dist 'GSyncIndicator.exe') -Force
Write-Host "    Portable exe -> $(Join-Path $dist 'GSyncIndicator.exe')" -ForegroundColor Green

if ($SkipInstaller) { Write-Host '==> Skipping installer (per -SkipInstaller).'; return }

Write-Host '==> Looking for Inno Setup (ISCC.exe)...' -ForegroundColor Cyan
$iscc = $null
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { $iscc = $c; break } }
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Write-Warning 'Inno Setup 6 was not found, so the installer was not built.'
    Write-Host    'Install it from https://jrsoftware.org/isdl.php (or: winget install JRSoftware.InnoSetup),'
    Write-Host    'then re-run this script. The portable exe above is ready to use as-is.'
    return
}

Write-Host "==> Compiling installer with $iscc ..." -ForegroundColor Cyan
& $iscc (Join-Path $root 'installer\GSyncIndicator.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)." }

Write-Host '==> Done. Artifacts in the dist\ folder:' -ForegroundColor Green
Get-ChildItem $dist | Format-Table Name, @{n='Size(MB)';e={[math]::Round($_.Length/1MB,1)}} -AutoSize
