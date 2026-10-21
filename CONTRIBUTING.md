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

### Requisitos

- **GeneXus 18 Upgrade 15** instalado: se compila contra sus DLL. Por defecto se busca en
  `C:\GeneXus\GeneXus18u15b188745`; si lo tenés en otro lado, pasá
  `-p:GX_PROGRAM_DIR="D:\ruta\a\GeneXus"` a `dotnet build` (o `-GxProgramDir` a los scripts).
- **.NET SDK 8** o superior. El resto de las referencias de GeneXus se bajan del feed NuGet oficial
  (`nuget.config` de cada proyecto).

### Compilar y probar

Cada extensión es un proyecto independiente en su carpeta `Gx<Nombre>`:

```powershell
cd GxQuickVar
dotnet build -c Release
.\Instalar.cmd          # sube la versión, compila, arma dist\<Nombre>-<versión>.zip e instala
```

GeneXus tiene que estar cerrado al instalar. Para desinstalar: `.\Instalar.cmd -Uninstall`.

### Pull requests

1. Hacé un fork y una rama para el cambio.
2. Mantené el estilo del código que tocás y un cambio por PR.
3. Si cambia el comportamiento, actualizá el `README.md` de la extensión: es también su página en el
   sitio de documentación.
4. Contá en el PR cómo lo probaste en el IDE.

### Una extensión nueva

Todas comparten la misma plantilla; lo más simple es copiar la más parecida (normalmente `GxCallParms`):

- `.csproj` con `GeneXus.Package.UI.Sdk` (net471, x86) y `Directory.Build.props` con la ruta de GeneXus.
- `deploy.ps1`, `pack.ps1` e `Instalar.cmd` para instalar y empaquetar.
- Opciones en *Tools > Options* (`OptionsPage`), guardadas en `%APPDATA%\<Nombre>\settings.txt`
  (`Settings`), atajos configurables con un `IMessageFilter` (`KeyFilter`) y `Log` para diagnóstico.

Al copiarla: generá un **GUID nuevo** para el package (`.package` y la clase `*Package`), elegí un atajo
que no choque con los de las otras extensiones (tabla del [README](README.md)) y agregala a
`GxInstalador/LEEME.txt` y a la tabla del `README.md`. La página del sitio se genera sola desde su README.

## Documentación

El sitio se arma con [MkDocs Material](https://squidfunk.github.io/mkdocs-material/) a partir de
`docs/` y de los `README.md` de cada extensión, y se publica solo al hacer push a `main`. Para verlo
localmente:

```powershell
pip install -r docs/requirements.txt
mkdocs serve
```
