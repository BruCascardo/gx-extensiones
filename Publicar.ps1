<#
.SYNOPSIS
  Publica un release en GitHub con el último zip de cada extensión y el instalador.
.DESCRIPTION
  1. Toma el zip más nuevo de cada Gx*\dist (los arma Instalar.cmd / pack.ps1 de cada extensión).
  2. Recompila GxInstalador\InstalarPackages.exe.
  3. Arma GxExtensiones-<tag>.zip con el instalador, LEEME.txt y todos los zip, y crea el release
     <tag> con ese zip y cada zip suelto. Requiere el CLI de GitHub (gh) logueado.
.EXAMPLE
  .\Publicar.ps1                      # tag vAAAA.MM.DD
  .\Publicar.ps1 -Tag v2026.10.21b
  .\Publicar.ps1 -Prueba              # arma el zip pero no publica
#>
param(
	[string]$Tag = ('v' + (Get-Date -Format 'yyyy.MM.dd')),
	[switch]$Prueba
)
$ErrorActionPreference = 'Stop'

$Patron   = '^(?<nombre>.+)-(?<version>\d+(\.\d+){1,3})\.zip$'
$Salida   = Join-Path $PSScriptRoot "dist\$Tag"
$Paquetes = Join-Path $Salida 'GxExtensiones'

if (-not $Prueba) {
	if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'No encuentro gh (CLI de GitHub): winget install GitHub.cli' }
	if (git -C $PSScriptRoot status --porcelain) { throw 'Hay cambios sin commitear: el release tiene que salir de lo que está en GitHub.' }
}

Remove-Item $Salida -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Paquetes | Out-Null

# Último zip de cada extensión
$Notas = @("Instalación: bajar GxExtensiones-$Tag.zip, descomprimirlo, cerrar GeneXus y ejecutar InstalarPackages.exe.", '', '| Extensión | Versión |', '|---|---|')
foreach ($Proyecto in Get-ChildItem $PSScriptRoot -Directory -Filter 'Gx*') {
	$Dist = Join-Path $Proyecto.FullName 'dist'
	if (-not (Test-Path $Dist)) { continue }
	$Ultimo = Get-ChildItem $Dist -Filter '*.zip' -File |
		Where-Object { $_.Name -match $Patron } |
		Sort-Object { [version]($_.Name -replace $Patron, '${version}') } -Descending |
		Select-Object -First 1
	if (-not $Ultimo) { continue }
	Copy-Item $Ultimo.FullName $Paquetes
	$Notas += "| $($Proyecto.Name) | $($Ultimo.Name -replace $Patron, '${version}') |"
	Write-Host "  $($Ultimo.Name)"
}

# Instalador
& (Join-Path $PSScriptRoot 'GxInstalador\Compilar.ps1')
Copy-Item (Join-Path $PSScriptRoot 'GxInstalador\InstalarPackages.exe'), (Join-Path $PSScriptRoot 'GxInstalador\LEEME.txt') $Paquetes

$Bundle = Join-Path $Salida "GxExtensiones-$Tag.zip"
Compress-Archive -Path (Join-Path $Paquetes '*') -DestinationPath $Bundle
Write-Host "Armado: $Bundle" -ForegroundColor Green

if ($Prueba) { Write-Host 'MODO PRUEBA: no se publica.' -ForegroundColor Yellow; return }

$Archivos = @($Bundle) + @(Get-ChildItem $Paquetes -Filter '*.zip' | ForEach-Object FullName)
gh release create $Tag @Archivos --title "Extensiones $Tag" --notes ($Notas -join "`n")
if ($LASTEXITCODE -ne 0) { throw 'Falló gh release create' }
Write-Host "Publicado: $Tag" -ForegroundColor Green
