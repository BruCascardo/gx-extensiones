<#
.SYNOPSIS
  Revisa que el repositorio esté coherente: lo que CONTRIBUTING pide al agregar o cambiar una extensión.
.DESCRIPTION
  Para cada extensión de src\ comprueba que:
  - esté en la tabla del README.md, en docs\index.md, en installer\LEEME.txt y en GxExtensiones.sln;
  - tenga README.md y su .csproj tenga <Description> (sale en el menú del instalador) y <Version>x.y.z</Version>;
  - el id de su .package sea el PackageGuid de su clase *Package y no lo use otra extensión;
  - deploy.ps1, pack.ps1 e Instalar.cmd sean los de la plantilla (GxCallParms) cambiando solo el
    nombre. Se ignoran los comentarios y el mensaje final de deploy.ps1, que puede ser propio.
  En todo el repositorio comprueba que no se nombren extensiones que ya no están en src\ y que los .ps1
  con acentos tengan BOM: sin BOM, Windows PowerShell 5.1 los lee mal.
  Lo corre el workflow CI en cada push y pull request.
.EXAMPLE
  .\scripts\Verificar.ps1
#>
$ErrorActionPreference = 'Stop'

$Raiz      = Split-Path $PSScriptRoot
$Plantilla = 'GxCallParms'
$Copias    = 'deploy.ps1', 'pack.ps1', 'Instalar.cmd'
$Fallas    = New-Object System.Collections.Generic.List[object]

function Add-Falla([string]$Archivo, [string]$Mensaje) {
	$Fallas.Add([pscustomobject]@{ Archivo = $Archivo; Mensaje = $Mensaje })
}

function Read-Texto([string]$Ruta) {
	[IO.File]::ReadAllText((Join-Path $Raiz $Ruta)) -replace "`r`n", "`n"
}

# Líneas de un script de la plantilla sin comentarios, líneas vacías ni el mensaje final, con el
# nombre de la extensión cambiado por un marcador para poder comparar extensiones entre sí
function Get-LineasPlantilla([string]$Extension, [string]$Archivo) {
	(Read-Texto "src\$Extension\$Archivo") -split "`n" |
		Where-Object { $_.Trim() -ne '' -and $_.Trim() -notmatch "^(#|rem\s|Write-Host 'Listo\.)" } |
		ForEach-Object { $_.Replace($Extension, '<Extension>').TrimEnd() }
}

# Extensiones nombradas en un archivo, según el patrón de su enlace o entrada
function Test-Lista([string]$Archivo, [string]$Patron, [string[]]$Extensiones) {
	$Nombradas = @([regex]::Matches((Read-Texto $Archivo), $Patron) | ForEach-Object { $_.Groups[1].Value })
	foreach ($Extension in $Extensiones | Where-Object { $_ -notin $Nombradas }) {
		Add-Falla $Archivo "falta $Extension"
	}
	foreach ($Nombrada in $Nombradas | Where-Object { $_ -notin $Extensiones } | Select-Object -Unique) {
		Add-Falla $Archivo "nombra a $Nombrada, que no está en src\"
	}
}

$Extensiones = @(Get-ChildItem (Join-Path $Raiz 'src') -Directory | Sort-Object Name | ForEach-Object Name)

Test-Lista 'README.md'           '\]\(src/([^/)]+)/README\.md\)'      $Extensiones
Test-Lista 'docs\index.md'       '\]\(extensiones/([^/)]+)\.md\)'     $Extensiones
Test-Lista 'installer\LEEME.txt' '(?m)^- (Gx\w+):'                   $Extensiones
Test-Lista 'GxExtensiones.sln'   '"src\\([^\\"]+)\\[^\\"]+\.csproj"' $Extensiones

$Guids = @{}
foreach ($Extension in $Extensiones) {
	$Carpeta = "src\$Extension"
	$Csproj  = "$Carpeta\$Extension.csproj"
	if (-not (Test-Path (Join-Path $Raiz $Csproj))) { Add-Falla $Carpeta "falta $Extension.csproj"; continue }
	if (-not (Test-Path (Join-Path $Raiz "$Carpeta\README.md"))) { Add-Falla $Carpeta 'falta README.md' }

	$Proyecto = Read-Texto $Csproj
	if ($Proyecto -notmatch '<Description>\s*\S') { Add-Falla $Csproj 'falta <Description>: es lo que muestra el menú del instalador' }
	if ($Proyecto -notmatch '<Version>\d+\.\d+\.\d+</Version>') { Add-Falla $Csproj 'falta <Version>x.y.z</Version>: la usa pack.ps1' }

	# GUID del package: el del .package, el de la clase y distinto al de las demás extensiones
	$Package = "$Carpeta\$Extension.package"
	if (-not (Test-Path (Join-Path $Raiz $Package))) { Add-Falla $Carpeta "falta $Extension.package" }
	else {
		$Id    = ([xml](Read-Texto $Package)).Package.id
		$Clase = Get-ChildItem (Join-Path $Raiz $Carpeta) -Filter '*Package.cs' | Select-Object -First 1
		if (-not $Clase -or (Read-Texto "$Carpeta\$($Clase.Name)") -notmatch 'PackageGuid\s*=\s*"([0-9A-Fa-f-]{36})"') {
			Add-Falla $Carpeta 'no encuentro PackageGuid en la clase *Package'
		}
		elseif ($Matches[1] -ne $Id) {
			Add-Falla $Package "el id ($Id) no es el PackageGuid de $($Clase.Name) ($($Matches[1]))"
		}
		if ($Guids.ContainsKey($Id)) { Add-Falla $Package "el id $Id ya lo usa $($Guids[$Id]): generá un GUID nuevo" }
		else { $Guids[$Id] = $Extension }
	}

	if ($Extension -eq $Plantilla) { continue }
	foreach ($Archivo in $Copias) {
		if (-not (Test-Path (Join-Path $Raiz "$Carpeta\$Archivo"))) { Add-Falla $Carpeta "falta $Archivo"; continue }
		$Esperadas = @(Get-LineasPlantilla $Plantilla $Archivo)
		$Reales    = @(Get-LineasPlantilla $Extension $Archivo)
		for ($i = 0; $i -lt [Math]::Max($Esperadas.Count, $Reales.Count); $i++) {
			if ($Esperadas[$i] -cne $Reales[$i]) {
				Add-Falla "$Carpeta\$Archivo" ("difiere de src\$Plantilla\$Archivo. Tiene: '{0}'. La plantilla: '{1}'" -f $Reales[$i], $Esperadas[$i])
				break
			}
		}
	}
}

# Windows PowerShell 5.1 lee como ANSI los .ps1 sin BOM y rompe los acentos
foreach ($Script in git -C $Raiz ls-files '*.ps1') {
	$Bytes    = [IO.File]::ReadAllBytes((Join-Path $Raiz $Script))
	$TieneBom = $Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF
	if (-not $TieneBom -and ($Bytes | Where-Object { $_ -gt 127 } | Select-Object -First 1)) {
		Add-Falla $Script 'tiene acentos y no tiene BOM: guardalo como UTF-8 con BOM'
	}
}

if ($Fallas.Count -eq 0) {
	Write-Host "OK: $($Extensiones.Count) extensiones coherentes." -ForegroundColor Green
	exit 0
}
if ($env:GITHUB_ACTIONS -eq 'true') { [Console]::OutputEncoding = [Text.Encoding]::UTF8 }
foreach ($Falla in $Fallas) {
	if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::error file=$($Falla.Archivo -replace '\\', '/')::$($Falla.Mensaje)" }
	else { Write-Host "  $($Falla.Archivo): $($Falla.Mensaje)" -ForegroundColor Red }
}
Write-Host "$($Fallas.Count) problema(s)." -ForegroundColor Red
exit 1
