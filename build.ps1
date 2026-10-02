<#
.SYNOPSIS
    Builds DnsToolbox95.exe with the in-box .NET Framework compiler.
.DESCRIPTION
    Uses csc.exe from %WINDIR%\Microsoft.NET\Framework64\v4.0.30319, so it needs
    no Visual Studio, no .NET SDK and no NuGet packages - only .NET Framework 4.x,
    which ships with every supported version of Windows.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($OutputPath)) { $OutputPath = Join-Path $root 'build' }

function Get-CscPath {
    $windir = $env:WINDIR
    if ([string]::IsNullOrEmpty($windir)) { $windir = 'C:\Windows' }
    $candidates = @(
        (Join-Path $windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $onPath = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'No C# compiler (csc.exe) was found. Install .NET Framework 4.x or Visual Studio Build Tools.'
}

$sources = @()
$sources += Get-ChildItem -Path (Join-Path $root 'src') -Filter '*.cs' -Recurse -File |
            Sort-Object FullName | Select-Object -ExpandProperty FullName
if ($sources.Count -eq 0) { throw "No .cs files were found under $root\src" }

$icon = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $icon)) {
    Write-Host 'Icon not found - generating assets\app.ico ...'
    & (Join-Path $root 'tools\New-Icon.ps1') -OutputPath $icon | Out-Null
}

if (-not (Test-Path $OutputPath)) { New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null }

$csc = Get-CscPath
$exe = Join-Path $OutputPath 'DnsToolbox95.exe'
$optimize = if ($Configuration -eq 'Release') { '/optimize+' } else { '/optimize-' }
$define = if ($Configuration -eq 'Release') { 'RELEASE' } else { 'DEBUG' }

Write-Host 'DNS Toolbox 95 - build'
Write-Host "  Compiler : $csc"
Write-Host "  Config   : $Configuration"
Write-Host "  Sources  : $($sources.Count) file(s)"
Write-Host "  Output   : $exe"
Write-Host ''

$argList = @('/nologo', '/target:winexe', '/platform:anycpu', $optimize, "/define:$define",
             '/warn:4', '/utf8output', "/out:$exe")

if (Test-Path $icon) { $argList += "/win32icon:$icon" }

$argList += @('/reference:System.dll', '/reference:System.Core.dll',
              '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$argList += $sources

& $csc $argList
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }

$size = (Get-Item $exe).Length
Write-Host ''
Write-Host "Build succeeded: $exe ($([math]::Round($size / 1KB, 1)) KB)"
$ver = (Get-Item $exe).VersionInfo.FileVersion
if ($ver) { Write-Host "  FileVersion: $ver" }