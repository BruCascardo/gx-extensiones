<#
.SYNOPSIS
  Instala los packages de GeneXus que están en la carpeta compartida, eligiendo cuáles.
.DESCRIPTION
  1. Toma los zip de la carpeta compartida (por defecto, la carpeta donde está este script) con el
     formato <Package>-<versión>.zip. Si hay varias versiones de un mismo package, usa la más nueva.
  2. Muestra un menú con cada package, qué hace (la descripción de su DLL) y si ya está instalado.
     Se elige: all = todos, un número (2) o varios separados por coma (1,3,5).
  3. Copia los elegidos a C:\Packages (la crea si no existe) y los descomprime en
     C:\Packages\<Package>-<versión>. Si el zip ya está y es idéntico, no lo copia.
  4. Copia los DLL a la carpeta Packages de GeneXus, salvo los que ya están instalados iguales
     (o en una versión más nueva), y ejecuta "GeneXus.exe /install" una sola vez al final.
  GeneXus tiene que estar cerrado. InstalarPackages.exe trae este script adentro y lo ejecuta con
  -Origen en la carpeta del exe (ver Compilar.ps1).
.EXAMPLE
  .\InstalarPackages.ps1                          # muestra el menú
  .\InstalarPackages.ps1 -Paquetes all            # todos, sin menú
  .\InstalarPackages.ps1 -Paquetes 'GxQuickVar,GxSdtJson'
  .\InstalarPackages.ps1 -Origen '\\servidor\compartido\GeneXus' -GxProgramDir 'D:\GeneXus\GX18U15'
  .\InstalarPackages.ps1 -Reinstalar              # vuelve a instalar aunque ya estén al día
#>
param(
    # Carpeta con los zip (la compartida). Por defecto, la carpeta del script.
    [string]$Origen = $PSScriptRoot,
    # Carpeta local donde se guardan y descomprimen los zip
    [string]$Destino = 'C:\Packages',
    # Instalación de GeneXus; si no se indica, se busca GeneXus 18 U15 en las carpetas habituales
    [string]$GxProgramDir,
    # Qué instalar sin mostrar el menú: all, números del menú o nombres separados por coma
    [string]$Paquetes,
    # Instalar también los que ya están al día o tienen una versión más nueva instalada
    [switch]$Reinstalar,
    # Instalar aunque el upgrade de GeneXus no sea U15
    [switch]$Force,
    # Solo copiar los DLL, sin ejecutar "GeneXus.exe /install" (para pruebas)
    [switch]$NoRegistrar
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$TargetVersion = '18.0.15'   # GeneXus 18 Upgrade 15 (FileVersion de GeneXus.exe)
$TodosLosPaquetes = 'all'

function Get-GxVersion([string]$dir) {
    $exe = Join-Path $dir 'GeneXus.exe'
    if (Test-Path $exe) { (Get-Item $exe).VersionInfo.FileVersion } else { $null }
}

function Get-DllVersion([string]$path) {
    $text = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
    $parsed = $null
    if ($text -and [version]::TryParse(($text -replace '[^\d\.].*$', ''), [ref]$parsed)) { $parsed } else { [version]'0.0' }
}

function Test-SameFile([string]$a, [string]$b) {
    (Test-Path -LiteralPath $a) -and (Test-Path -LiteralPath $b) -and
        (Get-FileHash -LiteralPath $a).Hash -eq (Get-FileHash -LiteralPath $b).Hash
}

function Write-Paso([string]$texto) { Write-Host "`n== $texto" -ForegroundColor Cyan }

# Saca el DLL del zip a $carpeta, sin descomprimir todo, para leer versión y descripción antes de elegir
function Expand-PackageDll($zip, [string]$carpeta) {
    $archive = [IO.Compression.ZipFile]::OpenRead($zip.File.FullName)
    try {
        $entry = $archive.Entries | Where-Object FullName -eq "$($zip.Name).dll" | Select-Object -First 1
        if (-not $entry) { return $null }
        $dll = Join-Path $carpeta $entry.Name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dll, $true)
        $dll
    }
    finally { $archive.Dispose() }
}

# Devuelve los packages elegidos: all, números del menú o nombres, separados por coma
function Select-Paquetes($disponibles, [string]$seleccion) {
    $seleccion = $seleccion.Trim()
    if ($seleccion -eq $TodosLosPaquetes) { return $disponibles }
    $elegidos = foreach ($token in ($seleccion -split '[,; ]+' | Where-Object { $_ })) {
        $numero = 0
        $package = if ([int]::TryParse($token, [ref]$numero)) {
            if ($numero -ge 1 -and $numero -le $disponibles.Count) { $disponibles[$numero - 1] }
        }
        else { $disponibles | Where-Object Name -eq $token }
        if (-not $package) { throw "'$token' no es un package de la lista." }
        $package
    }
    @($elegidos | Sort-Object Name -Unique)
}

function Show-Menu($disponibles) {
    Write-Paso 'Packages disponibles'
    for ($i = 0; $i -lt $disponibles.Count; $i++) {
        $package = $disponibles[$i]
        $color = switch ($package.Accion) { 'al día' { 'Green' } 'instalar' { 'White' } default { 'Yellow' } }
        Write-Host ("`n  [{0}] {1} {2}" -f ($i + 1), $package.Name, $package.Version) -NoNewline
        Write-Host "  ($($package.Estado))" -ForegroundColor $color
        Write-Host "      $($package.Descripcion)" -ForegroundColor Gray
    }
    Write-Host "`n  Elegí qué instalar: $TodosLosPaquetes = todos, un número (2) o varios separados por coma (1,3,5)."
    Write-Host '  Enter sin escribir nada para salir.'
    while ($true) {
        $seleccion = Read-Host "`n  Opción"
        if (-not $seleccion.Trim()) { return @() }
        try { return Select-Paquetes $disponibles $seleccion }
        catch { Write-Host "  $($_.Exception.Message) Probá de nuevo." -ForegroundColor Red }
    }
}

# ---- GeneXus ----

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
$gxVersion = Get-GxVersion $GxProgramDir
Write-Host "GeneXus: $GxProgramDir (versión $gxVersion)"
if ($gxVersion -notlike "$TargetVersion.*" -and -not $Force) {
    throw "Los packages están compilados para GeneXus 18 U15 ($TargetVersion.x) y esta instalación es $gxVersion. " +
          "Pueden no cargar o fallar; si querés probar igual, agregá -Force."
}
$gxPackages = Join-Path $GxProgramDir 'Packages'

$running = Get-Process GeneXus -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $gxExe }
if ($running) { throw "GeneXus está abierto (PID $($running.Id -join ', ')). Cerralo y volvé a correr el instalador." }

# ---- Zips de la carpeta compartida: la versión más nueva de cada package ----

Write-Host "Packages en: $Origen"
if (-not (Test-Path -LiteralPath $Origen)) { throw "No encuentro la carpeta $Origen" }

$zips = foreach ($file in Get-ChildItem -LiteralPath $Origen -Filter '*.zip' -File) {
    if ($file.Name -match '^(?<name>.+?)-(?<version>\d+(\.\d+){1,3})\.zip$') {
        [pscustomobject]@{ Name = $Matches.name; Version = [version]$Matches.version; File = $file }
    }
    else {
        Write-Warning "$($file.Name): no tiene el formato <Package>-<versión>.zip, lo salteo."
    }
}
$latest = @($zips | Group-Object Name | ForEach-Object { $_.Group | Sort-Object Version -Descending | Select-Object -First 1 } | Sort-Object Name)
if ($latest.Count -eq 0) { throw "No hay zips de packages en $Origen" }

# ---- Qué hace cada uno y si ya está instalado ----

$temp = Join-Path ([IO.Path]::GetTempPath()) ('GxInstalador-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $disponibles = @(foreach ($zip in $latest) {
        $dll = Expand-PackageDll $zip $temp
        if (-not $dll) {
            Write-Warning "$($zip.File.Name) no tiene $($zip.Name).dll, lo salteo."
            continue
        }
        $dllVersion = Get-DllVersion $dll
        $descripcion = (Get-Item -LiteralPath $dll).VersionInfo.Comments
        if (-not $descripcion) { $descripcion = '(sin descripción)' }

        $installed = Join-Path $gxPackages "$($zip.Name).dll"
        $installedText = '-'
        $accion = 'instalar'
        $estado = 'no instalado'
        if (Test-Path -LiteralPath $installed) {
            $oldVersion = Get-DllVersion $installed
            # Como en el nombre del zip: 1.2.0 en vez de 1.2.0.0
            $installedText = "$oldVersion" -replace '\.0$', ''
            if (Test-SameFile $dll $installed) { $accion = 'al día'; $estado = 'instalado, al día' }
            elseif ($oldVersion -gt $dllVersion) { $accion = 'instalada una más nueva'; $estado = "instalada una más nueva: $installedText" }
            else { $accion = 'actualizar'; $estado = "instalado $installedText, se actualiza" }
            if ($Reinstalar -and $accion -ne 'actualizar') { $accion = 'reinstalar' }
        }
        [pscustomobject]@{
            Name = $zip.Name; Version = $zip.Version; Zip = $zip.File; Descripcion = $descripcion
            Instalado = $installedText; Accion = $accion; Estado = $estado
        }
    })
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
if ($disponibles.Count -eq 0) { throw "No hay zips de packages válidos en $Origen" }

$elegidos = if ($Paquetes) { Select-Paquetes $disponibles $Paquetes } else { Show-Menu $disponibles }
if ($elegidos.Count -eq 0) {
    Write-Host "`nNo se instaló nada."
    return
}

# ---- Copiar los elegidos a C:\Packages y descomprimir ----

Write-Paso "Copiando a $Destino"
if (-not (Test-Path -LiteralPath $Destino)) {
    New-Item -ItemType Directory -Path $Destino | Out-Null
    Write-Host "  Carpeta $Destino creada"
}

$toInstall = New-Object System.Collections.Generic.List[object]
foreach ($package in $elegidos) {
    $localZip = Join-Path $Destino $package.Zip.Name
    $folder = Join-Path $Destino $package.Zip.BaseName
    $dll = Join-Path $folder "$($package.Name).dll"

    # El mismo zip ya copiado y descomprimido: no se vuelve a copiar
    $sameZip = Test-SameFile $package.Zip.FullName $localZip
    if ($sameZip -and (Test-Path -LiteralPath $dll)) {
        Write-Host "  $($package.Zip.Name): ya estaba en $Destino"
    }
    else {
        Copy-Item -LiteralPath $package.Zip.FullName -Destination $localZip -Force
        # Sin la marca de "descargado": si no, Windows la pasa a los DLL y .NET puede negarse a cargarlos
        Unblock-File -LiteralPath $localZip
        if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
        Expand-Archive -LiteralPath $localZip -DestinationPath $folder
        $what = if ($sameZip) { 'descomprimido de nuevo' } else { 'copiado y descomprimido' }
        Write-Host "  $($package.Zip.Name): $what"
    }

    if ($package.Accion -in 'instalar', 'actualizar', 'reinstalar') {
        $toInstall.Add([pscustomobject]@{ Name = $package.Name; Folder = $folder })
    }
}

# ---- Instalar en GeneXus ----

$summary = $elegidos | Select-Object @{ n = 'Package'; e = { $_.Name } }, @{ n = 'Zip'; e = { "$($_.Version)" } },
    Instalado, @{ n = 'Resultado'; e = { $_.Accion } }

Write-Paso 'Instalando en GeneXus'
if ($toInstall.Count -eq 0) {
    $summary | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host 'Los packages elegidos ya estaban instalados y al día. No hay nada que hacer (para forzarlo: -Reinstalar).' -ForegroundColor Green
    return
}

try {
    foreach ($package in $toInstall) {
        $files = Get-ChildItem -LiteralPath $package.Folder -File |
            Where-Object { $_.BaseName -eq $package.Name -and $_.Extension -in '.dll', '.pdb' }
        foreach ($file in $files) {
            $target = Join-Path $gxPackages $file.Name
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            Unblock-File -LiteralPath $target
        }
        Write-Host "  $($package.Name) copiado a $gxPackages"
    }
}
catch [System.UnauthorizedAccessException] {
    throw "Sin permiso para escribir en $gxPackages. Volvé a ejecutarlo como administrador (clic derecho > Ejecutar como administrador)."
}

if ($NoRegistrar) {
    Write-Host '  (-NoRegistrar: no se ejecutó GeneXus.exe /install)'
}
else {
    Write-Host '  Registrando packages (GeneXus.exe /install), puede tardar un poco...'
    Start-Process $gxExe -ArgumentList '/install' -Wait
}

$summary | Format-Table -AutoSize | Out-String | Write-Host
Write-Host 'Listo. Abrí GeneXus normalmente.' -ForegroundColor Green
