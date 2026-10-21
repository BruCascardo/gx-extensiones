# GxOpenWeb

Extensión para el IDE de GeneXus 18 (U15): clic derecho en la **pestaña de un Web Panel** > **Abrir** >
**Local**, **CI** o **Preprod** lo abre en el navegador predeterminado.

La URL es la base del ambiente seguida del *Java package name* del environment Java y del nombre del objeto
con su módulo, todo en minúsculas:

```
Local:    http://localhost:8080/MiKBJavaEnvironment/com.miempresa.mikb.ventas.clientewp
CI:       https://ci.miempresa.com/mikb/com.miempresa.mikb.ventas.clientewp
Preprod:  https://preprod.miempresa.com/mikb/com.miempresa.mikb.ventas.clientewp
```

- El submenú aparece solo en Web Panels de tipo página web (no en Web Components ni Master Pages).
- **Local**: por defecto usa el *Web root* del environment Java de la KB (el por defecto si es Java web; si
  no, el primero Java web).
- **CI** y **Preprod**: vienen vacías; se configuran una vez en las opciones, por ejemplo
  `https://ci.miempresa.com/{kb}/`, donde `{kb}` se reemplaza por el nombre de la KB en minúsculas.

En **Tools > Options > GxOpenWeb** se cambian las tres URLs base (Local vacía = Web root). Configuración en
`%APPDATA%\GxOpenWeb\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxOpenWeb\log.txt` registra cada objeto y la URL que abrió (se apaga en las
opciones).

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
.\pack.ps1        # arma dist\GxOpenWeb-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- `GxOpenWeb.package`: agrega el submenú *Abrir* al menú contextual de la pestaña de un objeto
  (`MainWindowKBObjectContext` del package Core). Los comandos son `context="selection"`, así el menú de la
  pestaña les pasa el objeto de esa pestaña.
- `WebOpener`: decide si se muestra (oculta el submenú entero si no es un Web Panel página web), arma la URL
  con el `WebRoot` y el `PACKAGE` del environment Java y `QualifiedName` del objeto, y la abre con
  `Process.Start`.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
