<#
.SYNOPSIS
  Arma el zip para compartir GxQuickOpen: dist\GxQuickOpen-<versión>.zip con el DLL,
  el instalador (deploy.ps1) y el README. Quien lo recibe no necesita el .NET SDK.
.EXAMPLE
  .\pack.ps1                 # con la versión del .csproj (pisa el zip si ya existe)
  .\pack.ps1 -SubirVersion   # versión nueva (x.y.z+1): lo usa Instalar.cmd desde el código fuente
#>
param(
    # Instalación de GeneXus contra la que se compila (por defecto, la de Directory.Build.props)
    [string]$GxProgramDir,
    # Sube el último número de la versión antes de compilar, así cada zip es una versión nueva
    [switch]$SubirVersion
)
$ErrorActionPreference = 'Stop'

$csproj = Join-Path $PSScriptRoot 'GxQuickOpen.csproj'
$dist = Join-Path $PSScriptRoot 'dist'
$originalProject = $null

if ($SubirVersion) {
    $bytes = [IO.File]::ReadAllBytes($csproj)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $originalProject = [IO.File]::ReadAllText($csproj)
    $match = [regex]::Match($originalProject, '<Version>(\d+\.\d+\.\d+)</Version>')
    if (-not $match.Success) { throw "No encuentro <Version>x.y.z</Version> en $csproj" }

    # La más alta entre el .csproj y los zips de dist\, más uno en el último número
    $current = [version]$match.Groups[1].Value
    $zipVersions = Get-ChildItem $dist -Filter 'GxQuickOpen-*.zip' -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.BaseName -match '^GxQuickOpen-(\d+\.\d+\.\d+)$') { [version]$Matches[1] } }
    $highest = @(@($current) + @($zipVersions) | Sort-Object -Descending)[0]
    $newVersion = '{0}.{1}.{2}' -f $highest.Major, $highest.Minor, ($highest.Build + 1)

    $project = $originalProject.Substring(0, $match.Index) + "<Version>$newVersion</Version>" +
        $originalProject.Substring($match.Index + $match.Length)
    [IO.File]::WriteAllText($csproj, $project, (New-Object System.Text.UTF8Encoding($hasBom)))
    Write-Host "Versión nueva: $current -> $newVersion" -ForegroundColor Cyan
}

$buildArgs = @($csproj, '-c', 'Release')
if ($GxProgramDir) { $buildArgs += "-p:GX_PROGRAM_DIR=$GxProgramDir" }
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) {
    # Sin compilar no hay zip: la versión vuelve a la de antes para no saltear números
    if ($originalProject) { [IO.File]::WriteAllText($csproj, $originalProject, (New-Object System.Text.UTF8Encoding($hasBom))) }
    throw 'Falló la compilación'
}

$bin = Join-Path $PSScriptRoot 'bin\Release\net471'
$version = (Get-Item (Join-Path $bin 'GxQuickOpen.dll')).VersionInfo.ProductVersion -replace '\+.*$', ''
$staging = Join-Path $dist "GxQuickOpen-$version"

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item (Join-Path $bin 'GxQuickOpen.dll'), (Join-Path $bin 'GxQuickOpen.pdb'), `
          (Join-Path $PSScriptRoot 'deploy.ps1'), (Join-Path $PSScriptRoot 'Instalar.cmd'), (Join-Path $PSScriptRoot 'README.md') $staging

$zip = "$staging.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force
Write-Host "Listo: $zip" -ForegroundColor Green
