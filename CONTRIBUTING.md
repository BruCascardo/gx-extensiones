# Contribuir

¡Gracias por querer mejorar las extensiones! Hay dos formas de aportar.

## Reportar errores o proponer ideas

Abrí un [issue](https://github.com/BruCascardo/gx-extensiones/issues/new/choose) con la plantilla que
corresponda. Para un error ayuda mucho:

- qué extensión y qué versión (está en el nombre del zip o en *Tools > Options*),
- qué hiciste, qué esperabas y qué pasó,
- el log de la extensión si tiene (`%APPDATA%\<Extensión>\log.txt`) y lo que haya quedado en la ventana
  *Output* de GeneXus.

## Mandar cambios de código

### Estructura del repositorio

```text
gx-extensiones/
├── src/                        Una carpeta por extensión (un package de GeneXus cada una)
│   └── GxQuickVar/
│       ├── GxQuickVar.csproj   Proyecto, versión y descripción del package
│       ├── GxQuickVar.package  Registro del package en el IDE (GUID)
│       ├── *.cs                Código
│       ├── README.md           Uso de la extensión (también es su página en el sitio)
│       ├── Instalar.cmd        Instala en GeneXus (llama a deploy.ps1)
│       ├── deploy.ps1          Instala / desinstala; viaja dentro del zip de la extensión
│       └── pack.ps1            Compila y arma dist\GxQuickVar-<versión>.zip
├── tests/                      Pruebas que corren sin abrir GeneXus (dotnet run)
├── installer/                  InstalarPackages.exe: instala varias extensiones desde un menú
├── docs/                       Sitio de documentación (MkDocs)
├── scripts/                    Verificar.ps1 (coherencia), Publicar.ps1 (release) y el hook del sitio
├── GxExtensiones.sln           Solución con todas las extensiones y las pruebas
├── Directory.Build.props       Configuración común: ruta de GeneXus, net471, x86
├── global.json                 Versiones de los SDK de GeneXus
└── nuget.config                Feeds de NuGet (nuget.org y el de GeneXus)
```

`Directory.Build.props`, `global.json` y `nuget.config` están en la raíz y valen para todos los
proyectos: MSBuild los busca subiendo de carpeta, así que una extensión no necesita copias propias.

### Requisitos

- **GeneXus 18 Upgrade 15** instalado: se compila contra sus DLL. Por defecto se busca en
  `C:\GeneXus\GeneXus18u15b188745`; si lo tenés en otro lado, pasá
  `-p:GX_PROGRAM_DIR="D:\ruta\a\GeneXus"` a `dotnet build` (o `-GxProgramDir` a los scripts).
- **.NET SDK 8** o superior. El resto de las referencias de GeneXus se bajan del feed NuGet oficial
  (`nuget.config` de la raíz).

### Compilar y probar

Todo junto, con la solución (o abriendo `GxExtensiones.sln` en Visual Studio):

```powershell
dotnet build GxExtensiones.sln -c Release
dotnet run --project tests/GxSdtJson.Tests
.\scripts\Verificar.ps1   # README, LEEME, solución, GUID y plantilla de cada extensión
```

Una sola extensión, desde su carpeta:

```powershell
cd src\GxQuickVar
dotnet build -c Release
.\Instalar.cmd          # sube la versión, compila, arma dist\<Nombre>-<versión>.zip e instala
```

GeneXus tiene que estar cerrado al instalar. Para desinstalar: `.\Instalar.cmd -Uninstall`.

### Integración continua

En cada push a `main` y en cada pull request, el workflow **CI** de GitHub Actions revisa lo que se
puede revisar sin GeneXus instalado:

- **Coherencia del repo** (`scripts/Verificar.ps1`): cada extensión de `src/` está en el README, en
  `docs/index.md`, en `installer/LEEME.txt` y en la solución; su GUID de package no se repite; sus
  `deploy.ps1`, `pack.ps1` e `Instalar.cmd` son los de la plantilla, y los `.ps1` con acentos tienen BOM.
- **Pruebas de GxSdtJson.**
- **Scripts e instalador**: que todos los `.ps1` se puedan leer en Windows PowerShell 5.1 (el que usa
  quien instala), PSScriptAnalyzer (las advertencias quedan anotadas sin frenar el PR) y que
  `InstalarPackages.exe` compile.
- **Documentación** (solo en pull requests): que el sitio se arme sin enlaces rotos.

Las extensiones no se compilan en CI: usan DLL de GeneXus que no se pueden subir al repositorio. Eso,
y probarlas en el IDE, queda del lado de quien manda el cambio. Dependabot propone una vez por mes
actualizar las acciones de los workflows y MkDocs.

### Publicar un release

```powershell
.\scripts\Publicar.ps1 -Prueba   # arma dist\<tag>\GxExtensiones-<tag>.zip sin publicar
.\scripts\Publicar.ps1           # crea el release en GitHub (requiere gh y el árbol sin cambios)
```

Toma el zip más nuevo de cada `src\<Extensión>\dist`, así que antes hay que correr `Instalar.cmd` (o
`pack.ps1`) en las extensiones que cambiaron.

### Pull requests

1. Hacé un fork y una rama para el cambio.
2. Mantené el estilo del código que tocás y un cambio por PR.
3. Si cambia el comportamiento, actualizá el `README.md` de la extensión: es también su página en el
   sitio de documentación.
4. Contá en el PR cómo lo probaste en el IDE.

### Una extensión nueva

Todas comparten la misma plantilla; lo más simple es copiar la carpeta de la más parecida dentro de
`src\` (normalmente `GxCallParms`):

- `.csproj` con `GeneXus.Package.UI.Sdk` (la ruta de GeneXus, net471 y x86 vienen del
  `Directory.Build.props` de la raíz).
- `deploy.ps1`, `pack.ps1` e `Instalar.cmd` para instalar y empaquetar.
- Opciones en *Tools > Options* (`OptionsPage`), guardadas en `%APPDATA%\<Nombre>\settings.txt`
  (`Settings`), atajos configurables con un `IMessageFilter` (`KeyFilter`) y `Log` para diagnóstico.

Al copiarla: generá un **GUID nuevo** para el package (`.package` y la clase `*Package`), elegí un atajo
que no choque con los de las otras extensiones (tabla del [README](README.md)), agregala a la solución
(`dotnet sln add src/<Nombre>/<Nombre>.csproj`), a `installer/LEEME.txt` y a la tabla del `README.md`.
La página del sitio se genera sola desde su README. `.\scripts\Verificar.ps1` avisa si falta algo de esto.

## Documentación

El sitio se arma con [MkDocs Material](https://squidfunk.github.io/mkdocs-material/) a partir de
`docs/` y de los `README.md` de cada extensión, y se publica solo al hacer push a `main`. Para verlo
localmente:

```powershell
pip install -r docs/requirements.txt
mkdocs serve
```
