# GxEnumValues

Extensión para el IDE de GeneXus 18 (U15): el diálogo de la propiedad **Enum Values** (dominios y
atributos enumerados) suma una **barra de búsqueda** y el **orden por columna**, útil cuando el dominio
tiene muchos valores.

| En el diálogo Enum Values | Qué hace |
|---|---|
| Escribir en la barra de arriba | Muestra solo los valores cuyo nombre, descripción o valor contienen el texto (sin distinguir mayúsculas ni tildes) |
| **Ctrl+F** | Lleva el foco a la barra de búsqueda |
| **Esc** en la barra | Borra la búsqueda (con la barra vacía, cierra el diálogo como siempre) |
| **Enter** o **↓** en la barra | Pasa a la lista |
| Clic en el encabezado Name, Description o Value | Ordena por esa columna: ascendente, descendente y de nuevo el orden original |

- **El orden que queda en la lista es el que se guarda** al dar OK, igual que con Move Up / Move Down
  (GeneXus guarda los valores en el orden de la lista). Sirve para ordenar un dominio alfabéticamente; si
  solo se quería mirar, el tercer clic en el encabezado vuelve al orden original (o Cancel).
- Los valores numéricos se ordenan como números (2 antes que 10).
- Con la búsqueda puesta se puede editar, agregar, borrar y mover como siempre. Al cerrar el diálogo (OK,
  Cancel o la X) los valores ocultos vuelven a la lista, así que no se pierde ninguno. Si alguno de los
  ocultos tiene un error (nombre repetido, valor inválido), OK no cierra y se ve la lista completa con la
  marca de error.

En **Tools > Options > GxEnumValues** se puede apagar. Configuración en
`%APPDATA%\GxEnumValues\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxEnumValues\log.txt` registra la carga del package y los errores (se apaga
en las opciones).

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

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`; la
de `Artech.Packages.GenexusUI` se toma de la instalación local de GeneXus).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxEnumValues-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- El diálogo es `EnumValuesEditorControl` (package `Artech.Packages.GenexusUI`), que abre el
  `EnumTypeEditor` de la propiedad. GeneXus no avisa cuando se abre: `DialogHook` instala un hook `WH_CBT`
  en el hilo de la interfaz y, cuando se activa una ventana de ese tipo, le agrega `EnumValuesEnhancer`.
- La lista es un `ListViewEx` de Artech. La barra de búsqueda se ubica arriba de la lista (la lista y los
  botones de al lado bajan para dejarle lugar). Ordenar reacomoda los ítems de la lista y muestra la flecha
  con `ListViewEx.DisplaySortIcon`.
- Filtrar saca de la lista los ítems que no coinciden y los guarda aparte, con el orden completo. Como
  el diálogo arma los valores a partir de la lista (`GridToValues`) y al cerrar la compara con los de antes
  para preguntar si guardar, antes de cerrar se vuelven a poner todos: después del clic en OK (y se llama de
  nuevo a `GridToValues`), en el clic en Cancel y al recibir `WM_CLOSE` (la X o Alt+F4).
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
