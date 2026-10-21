<#
.SYNOPSIS
  Instala (o desinstala) GxCallParms en el IDE de GeneXus.
.DESCRIPTION
  Copia GxCallParms.dll a la carpeta Packages de GeneXus y ejecuta "GeneXus.exe /install"
  para que el IDE vuelva a registrar los packages. GeneXus tiene que estar cerrado.

  Si no se indica -GxProgramDir, busca las instalaciones de GeneXus 18 U15 en las carpetas
  habituales (C:\GeneXus\*, Program Files). Sirve tanto desde el código fuente como desde el
  zip de distribución (usa el GxCallParms.dll que está al lado). Desde el código fuente cada vez
  sube la versión, compila y arma dist\GxCallParms-<versión>.zip (pack.ps1 -SubirVersion): para
  compartir alcanza con tomar el zip más nuevo, que es lo mismo que quedó instalado.
.EXAMPLE
  .\deploy.ps1
  .\deploy.ps1 -GxProgramDir 'D:\GeneXus\GX18U15'
  .\deploy.ps1 -Uninstall
#>
param(
    [string]$GxProgramDir,
    [switch]$Uninstall,
    # Instalar aunque el upgrade de GeneXus no sea el de compilación (U15)
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$TargetVersion = '18.0.15'   # GeneXus 18 Upgrade 15 (FileVersion de GeneXus.exe)

function Get-GxVersion([string]$dir) {
    $exe = Join-Path $dir 'GeneXus.exe'
    if (Test-Path $exe) { (Get-Item $exe).VersionInfo.FileVersion } else { $null }
}

if (-not $GxProgramDir) {
    $roots = 'C:\GeneXus', "${env:ProgramFiles(x86)}\GeneXus", "$env:ProgramFiles\GeneXus", 'D:\GeneXus'
    $found = foreach ($root in $roots) {
        if (Test-Path $root) {
            Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
                Where-Object { (Get-GxVersion $_.FullName) -like "$TargetVersion.*" } |
                ForEach-Object FullName
        }
    }
    $found = @($found | Select-Object -Unique)
    if ($found.Count -eq 0) {
        throw "No encontré GeneXus 18 U15 en las carpetas habituales. Indicá la carpeta con -GxProgramDir 'C:\ruta\a\GeneXus'."
    }
    if ($found.Count -gt 1) {
        throw "Encontré varias instalaciones de GeneXus 18 U15; elegí una con -GxProgramDir:`n  " + ($found -join "`n  ")
    }
    $GxProgramDir = $found[0]
}

$gxExe = Join-Path $GxProgramDir 'GeneXus.exe'
if (-not (Test-Path $gxExe)) { throw "No encuentro $gxExe" }
$version = Get-GxVersion $GxProgramDir
Write-Host "GeneXus: $GxProgramDir (versión $version)"
if (-not $Uninstall -and $version -notlike "$TargetVersion.*" -and -not $Force) {
    throw "GxCallParms está compilada para GeneXus 18 U15 ($TargetVersion.x) y esta instalación es $version. " +
          "Puede no cargar o fallar; si querés probar igual, agregá -Force."
}

$packages = Join-Path $GxProgramDir 'Packages'
$running = Get-Process GeneXus -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $gxExe }
if ($running) { throw "GeneXus está abierto (PID $($running.Id -join ', ')). Cerralo y volvé a correr el script." }

try {
    if ($Uninstall) {
        'GxCallParms.dll', 'GxCallParms.pdb' | ForEach-Object { Join-Path $packages $_ } |
            Where-Object { Test-Path $_ } | Remove-Item -Force
        Write-Host 'GxCallParms quitado de Packages.'
    }
    else {
        # Zip de distribución: el DLL viene al lado del script
        $source = $PSScriptRoot
        if (-not (Test-Path (Join-Path $source 'GxCallParms.dll'))) {
            # Código fuente: versión nueva, compilar y armar su zip en dist\, así lo que se instala
            # es siempre lo último y el zip más nuevo es justo eso
            & (Join-Path $PSScriptRoot 'pack.ps1') -GxProgramDir $GxProgramDir -SubirVersion
            $source = Join-Path $PSScriptRoot 'bin\Release\net471'
        }
        $files = Get-ChildItem $source -Filter 'GxCallParms.*' | Where-Object { $_.Extension -in '.dll', '.pdb' }
        $files | Copy-Item -Destination $packages -Force
        # Si el zip vino por mail o descargado, Windows marca los archivos como "de internet" y
        # .NET puede negarse a cargar el DLL: se quita esa marca.
        $files | ForEach-Object { Unblock-File -LiteralPath (Join-Path $packages $_.Name) }
        Write-Host "GxCallParms copiado a $packages"
    }
}
catch [System.UnauthorizedAccessException] {
    throw "Sin permiso para escribir en $packages. Abrí PowerShell como administrador y volvé a correr el script."
}

Write-Host 'Registrando packages (GeneXus.exe /install)...'
Start-Process $gxExe -ArgumentList '/install' -Wait
Write-Host 'Listo. Abrí GeneXus normalmente. Los atajos se configuran en Tools > Options > GxCallParms.'
