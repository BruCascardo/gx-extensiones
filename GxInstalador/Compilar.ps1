<#
.SYNOPSIS
  Arma InstalarPackages.exe con InstalarPackages.ps1 adentro. Hay que volver a correrlo cada vez
  que cambia InstalarPackages.ps1. Usa el csc.exe de .NET Framework 4 (viene con Windows).
.EXAMPLE
  .\Compilar.ps1
#>
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "No encuentro csc.exe de .NET Framework 4" }

$exe = Join-Path $PSScriptRoot 'InstalarPackages.exe'
& $csc /nologo /optimize+ /target:exe /platform:anycpu /utf8output /codepage:65001 `
    "/out:$exe" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" `
    "/resource:$(Join-Path $PSScriptRoot 'InstalarPackages.ps1'),InstalarPackages.ps1" `
    (Join-Path $PSScriptRoot 'InstalarPackages.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación' }
Write-Host "Listo: $exe" -ForegroundColor Green
