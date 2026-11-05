# GxModuleCache

Extensión para el IDE de GeneXus 18 (U15): agrega un botón **Limpiar caché** a la pantalla **Manage Module
References** (Knowledge Manager > Manage Module References).

GeneXus baja los módulos con NuGet, que guarda en caché las listas y los paquetes. Si se entra a la
pantalla antes de que un módulo termine de subirse al servidor, la versión nueva no aparece hasta que la
caché vence sola. El botón hace lo mismo que correr a mano:

```
"%USERPROFILE%\.gxmodules\.tools\nuget" locals all -clear
```

y después vuelve a cargar la lista de módulos del servidor elegido, con el filtro que estaba puesto.

| En Manage Module References | Qué hace |
|---|---|
| **Limpiar caché** (arriba a la derecha, en la barra *Module Information*) | Borra la caché local de NuGet y recarga los módulos |

- Usa el `nuget.exe` que baja el Module Manager de GeneXus en `%USERPROFILE%\.gxmodules\.tools`; si no
  está, el que trae la instalación de GeneXus.
- `locals all` borra todas las cachés de NuGet del usuario (`http-cache`, `global-packages`, `temp` y
  `plugins-cache`), no solo las de GeneXus: la próxima compilación de un proyecto .NET vuelve a bajar sus
  paquetes.
- Si falla (por ejemplo, porque otro programa tiene abierto un paquete de la caché), el detalle queda en
  la ventana **Output** de GeneXus.

En **Tools > Options > GxModuleCache** se puede apagar. Configuración en
`%APPDATA%\GxModuleCache\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxModuleCache\log.txt` registra la carga del package, el resultado de cada
limpieza y los errores (se apaga en las opciones).

## Instalar

Requisitos: **GeneXus 18 Upgrade 15** (está compilada para ese upgrade). No hace falta .NET SDK.

1. Cerrar GeneXus.
2. Descomprimir el zip en cualquier carpeta.
3. Doble clic en **Instalar.cmd** (si Windows avisa que el archivo viene de internet: *Más información* >
   *Ejecutar de todas formas*). O, en esa carpeta, desde PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
```

```powershell
.\deploy.ps1 -GxProgramDir 'D:\GeneXus\GX18U15'   # si no la encuentra o hay varias
.\deploy.ps1 -Uninstall                           # desinstalar
.\deploy.ps1 -Force                               # instalar en otro upgrade (bajo tu riesgo)
```

## Compilar y empaquetar

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxModuleCache-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- La pantalla es `ManageModuleReferencesToolWindow` (package `Artech.Packages.GenexusUI`, clase internal),
  un tool window con Guid `8E0BA1B6-1CB7-4E2C-A175-5C4A47AA3B71`. GeneXus no avisa cuando abre un tool
  window: `ToolWindowWatcher` lo busca con `UIServices.ToolWindows.TryGet` en `Application.Idle` y, cuando
  aparece una instancia nueva, `ModuleCacheButton` agrega el botón a su barra `toolStrip3` (*Module
  Information*). La barra de *Servers: Add* es angosta y el botón quedaría en el desborde.
- `NuGetCache` corre `nuget locals all -clear` en segundo plano (sin ventana, con un límite de 2 minutos).
  Al terminar, se llama por reflexión a `StartLoading(FilterPattern)` de la pantalla, lo mismo que hace
  GeneXus al cambiar de servidor.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
