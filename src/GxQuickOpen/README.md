# GxQuickOpen

Extensión para el IDE de GeneXus 18 (U15) que reemplaza el diálogo **Ctrl+O** ("Open Object") por
una búsqueda instantánea: se escribe y la lista filtra al momento, **cada resultado muestra su módulo**
y arriba hay un chip por módulo para filtrar con el teclado.

## Buscar

| Escribís | Encuentra |
|---|---|
| `inget` | nombres que empiezan con / contienen "inget" (también letras en orden: `igc` → `inGetCliente`) |
| `inget ventas` | cada palabra tiene que estar en el nombre, el módulo o el tipo. Si una palabra es parte del nombre de un módulo (`ventas`, `ven`), **primero los inGet de ese módulo y sus submódulos**; el orden de las palabras no importa |
| `inget Interfases.Registros` | el módulo también puede ir calificado: los inGet de Interfases.Registros y sus submódulos (vale abreviado: `inter.reg`) |
| `inget, ventas` | una coma seguida de espacio separa palabras igual que el espacio |
| `inget ventas,compras` | con coma pegada (sin espacio), cualquiera de las dos: los inGet de Ventas o de Compras |
| `@ventas` | solo objetos de módulos que contienen "ventas" (`@ven,com` = de cualquiera de los dos; `@/` = la raíz) |
| `prc inget ventas` | la abreviatura del tipo, suelta, filtra por tipo: los procedimientos inGet de Ventas (ver tabla de abajo) |
| `wp,wc ventas` | con coma, cualquiera de los dos tipos |
| `#proc` | solo ese tipo, escribiendo el comienzo de cualquier nombre del tipo (`#web` = todos los Web Panels) |
| `ven.inget` | módulo y nombre juntos, como el nombre calificado (`Ventas.Clientes.inGetCliente`) |

| Abreviatura | Tipo |
|---|---|
| `prc` / `proc` | Procedure |
| `dp` | Data Provider |
| `wp` | Web Panel (página) |
| `wc` | Web Component (Web Panel con Type = Component) |
| `mp` | Master Page (Web Panel con Type = Master Page) |
| `sdt` | Structured Data Type |
| `trn` | Transaction |
| `dom` | Domain |
| `eo` | External Object |
| `ds` | Data Selector |
| `att` | Attribute |
| `tbl` | Table |
| `img` | Image |
| `rpt` | Report |
| `sdp` | SD Panel |
| `ww` | Work With |

Los Web Components y Master Pages se reconocen por la propiedad *Type* del Web Panel y se muestran con
ese tipo en la lista. Si GeneXus no deja consultarla, `wc` y `mp` traen todos los Web Panels.

- Cada fila: ícono del tipo, nombre (con la coincidencia resaltada), tipo, **módulo** (también resaltado
  cuando una palabra coincidió con él) y descripción.
- Una palabra que es el nombre de un módulo no busca "letras en orden" en el nombre (`ventas` no trae
  `inGetValoresEntradas`).
- **Chips de módulo**: los módulos de los resultados con su cantidad ("Ventas.Clientes 12").
  **Tab / Shift+Tab** pasa de uno a otro; también con clic.
- **Recientes**: sin texto muestra los últimos objetos abiertos (de cualquier forma: KB Explorer, F12...),
  y con texto los recientes y los del módulo que estás editando aparecen primero.

| Tecla | Qué hace |
|---|---|
| Enter | abre el seleccionado (o los marcados) |
| Ctrl+Enter / Ctrl+clic | marca para abrir varios |
| Tab / Shift+Tab | siguiente / anterior módulo |
| ↑ ↓ RePág AvPág | mueven la selección |
| Ctrl+O | abre el diálogo clásico de GeneXus con lo escrito |
| Ctrl+Retroceso | borra la palabra anterior |
| Esc | cierra (igual que la cruz de arriba a la derecha o el botón **Cancelar** de abajo a la derecha) |

## Atajo

Por defecto es **Ctrl+O**, y gana sobre el de GeneXus. Si en **Tools > Options > GxQuickOpen** se
configura otro atajo, Ctrl+O vuelve a abrir el diálogo clásico de siempre. También está en
*File > Abrir objeto (búsqueda rápida)*. En las opciones se elige además el tema (automático según el
editor de código, claro u oscuro) y si se recuerda la última búsqueda.

Configuración y recientes por usuario en `%APPDATA%\GxQuickOpen\`.

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

Si GeneXus está en Program Files y da error de permisos, abrir PowerShell como administrador.

## Compilar y empaquetar

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxQuickOpen-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar: para compartir alcanza con tomar el zip más nuevo de `dist\`, que es
lo mismo que quedó instalado. `.\pack.ps1 -SubirVersion` hace lo mismo sin instalar.

## Cómo funciona

- `QuickOpenPackage`: package UI (comandos, menú, página de opciones, hook de teclado, eventos de la KB).
- `KeyboardHook`: hook de teclado del hilo de la interfaz. Hace falta porque la barra de menús de GeneXus
  procesa Ctrl+O antes que cualquier `IMessageFilter`; el hook ve la tecla primero y la descarta solo si
  es el atajo configurado y la ventana principal está activa sin diálogos abiertos.
- `ObjectIndex`: los objetos abribles, de la misma caché que usa el Ctrl+O de GeneXus
  (`UIServices.Objects.Cache`); se arma en segundo plano y se rehace al cambiar la caché o grabar/borrar.
- `Matcher`: búsqueda y orden (sin dependencias de GeneXus). `QuickOpenPopup` / `Theme`: la ventana.
- `Recents` / `Settings` / `OptionsPage`: recientes, configuración y opciones.
