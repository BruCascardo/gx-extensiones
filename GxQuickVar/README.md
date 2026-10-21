# GxQuickVar

Extensión para el IDE de GeneXus 18 (U15): con el cursor sobre una `&variable` en Events,
Source, Rules o Conditions, **Alt+V** abre un desplegable junto al cursor para escribir el tipo,
y la declara sin pasar por "Add variable" ni por las properties.
**Alt+Shift+V** recorre todas las variables del editor que todavía no están declaradas.

Los atajos se cambian en **Tools > Options > GxQuickVar** (también en *Tools > Opciones de GxQuickVar...*).

## El desplegable

| Tecla | Qué hace |
|---|---|
| Escribir | filtra la lista (tipos básicos, dominios, SDTs, BCs, EOs) resaltando la coincidencia (ver *Buscar*) |
| Tab | completa con el tipo seleccionado; si lleva parámetros pone los de por defecto seleccionados (`VarChar(40)`) |
| Enter | acepta; si el tipo lleva parámetros y no los escribiste, primero completa |
| ↑ ↓ RePág AvPág | mueven la selección |
| Esc | cancela (en "declarar todas": saltea esa variable) |
| Shift+Esc | termina "declarar todas" |

### Buscar

| Escribís | Encuentra |
|---|---|
| `sdtcli` | nombres que empiezan con / contienen "sdtcli"; también letras en orden (`sdtcli` → `sdtCliente`) |
| `cliente ventas` | cada palabra en el nombre, el módulo o la categoría, en cualquier orden; si una es parte de un módulo, **primero los de ese módulo** (y sus submódulos) |
| `sdtreg Interfases.Registros` | el módulo también puede ir calificado, entero o abreviado (`inter.reg`) |
| `sdtcli ventas,compras` | con coma pegada (sin espacio), cualquiera de los dos |
| `sdt ventas`, `bc cli`, `trn cli`, `dom imp`, `eo` | la abreviatura suelta filtra por categoría: SDT, Business Component (`bc` o `trn`), dominio, objeto externo |
| `ven.sdtcli` / `sdtCli, Ventas` | módulo y nombre juntos, como el nombre calificado o como lo escribe GeneXus |
| `@ventas`, `#sdt` | también siguen andando los filtros estrictos de módulo (`@/` = la raíz) y de categoría (`#bc`, `#dom`, `#num`...) |

Es la misma búsqueda que el Ctrl+O de GxQuickOpen, y en cada fila se resalta lo que coincidió del nombre y del módulo.
Una palabra que es el nombre de un módulo no busca "letras en orden" en el nombre (`ventas` no trae `sdtValoresEntradas`).

Enter o Tab completan con el tipo seleccionado, así que alcanza con `sdt ventas` + Enter.

- **Recientes** arriba de la lista: los últimos tipos que usaste.
- **SDTs del módulo**: después de los recientes, los SDTs del módulo del objeto y de sus módulos
  superiores (primero los más cercanos, la raíz al final).
- **Autodefinidas**: Alt+Shift+V también recorre las variables que GeneXus definió solo (por nombre de
  atributo o dominio). El desplegable abre con el tipo que asume; Enter la declara así o escribís otro.
  Si todavía no grabaste y la variable se llama igual que un atributo, abre igual con `att <Atributo>`.
- **Sugerencias**: propone el tipo que usaste la última vez para el mismo nombre de variable
  (`&Mensaje` → `VarChar(200)`), o un SDT/BC/dominio que se llame igual (`&sdtOutput` → `sdtOutput`).
  Se pueden apagar en las opciones.
- Abajo muestra el detalle del tipo elegido: base del dominio, descripción del SDT/BC/EO.
- **Basada en atributo**: `att ` + el nombre (también `att:RecId` o `Attribute:RecId`). Busca los atributos
  igual que los tipos: letras en orden y varias palabras (`att clinom`, `att cli nom` → `ClienteNombre`),
  con su tipo y descripción. Queda "Based on" ese atributo, igual que elegirlo en las properties.
- Sufijo ` collection` o `[]` para colecciones: `SDTCliente collection`, `SDTCliente[]`.
- Los tipos de un módulo se muestran como los escribe GeneXus (`sdtOutput, Sistema.Global`), pero se
  pueden buscar y escribir también como `Sistema.Global.sdtOutput` o solo `sdtOutput` (si no es ambiguo).
- Si la variable ya existe, abre con su tipo actual para cambiarlo.
- Las variables estándar (`&Pgmname`, `&Today`…) se declaran directo con su tipo fijo.
- Toma el tema claro u oscuro según el fondo del editor.

Configuración e historial por usuario en `%APPDATA%\GxQuickVar\`.

## Instalar

Requisitos: **GeneXus 18 Upgrade 15** (está compilada para ese upgrade). No hace falta .NET SDK.

1. Cerrar GeneXus.
2. Descomprimir el zip en cualquier carpeta.
3. Doble clic en **Instalar.cmd** (si Windows avisa que el archivo viene de internet: *Más información* >
   *Ejecutar de todas formas*). O, en esa carpeta, desde PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
```

El script busca solo la instalación de GeneXus 18 U15 (en `C:\GeneXus\*` o Program Files), copia
`GxQuickVar.dll` a `<GeneXus>\Packages`, le quita la marca de "descargado de internet" y ejecuta
`GeneXus.exe /install`. Después se abre GeneXus normalmente.

```powershell
.\deploy.ps1 -GxProgramDir 'D:\GeneXus\GX18U15'   # si no la encuentra o hay varias
.\deploy.ps1 -Uninstall                           # desinstalar
.\deploy.ps1 -Force                               # instalar en otro upgrade (bajo tu riesgo)
```

Si GeneXus está en Program Files y da error de permisos, abrir PowerShell como administrador.
Para actualizar a una versión nueva, se corre el mismo script con el zip nuevo.

## Compilar y empaquetar

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxQuickVar-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar: para compartir alcanza con tomar el zip más nuevo de `dist\`, que es
lo mismo que quedó instalado. `.\pack.ps1 -SubirVersion` hace lo mismo sin instalar.

## Cómo funciona

- `QuickVarPackage`: package UI (comandos, menús, página de opciones, filtro de teclado, invalidación del catálogo).
- `KeyBindingFilter`: atajos configurables. Los del manifiesto son fijos y el enum `Shortcut` de WinForms
  no admite Alt+letra, así que se interceptan los mensajes de teclado solo cuando el foco está en un editor de código.
- `VariableDeclarer`: replica `GxTextEditor.ExecuteAddVariable` de GeneXus; el tipo se asigna con el
  `DataTypeCaps` del catálogo (como el combo de la grilla de variables) o con `DataType.ParseInto` para `VarChar(100)` y similares.
- `TypeMatcher`: búsqueda y puntaje (las mismas reglas que GxQuickOpen, sin dependencias de GeneXus).
- `TypeCatalog`: misma fuente que el combo de tipos de GeneXus (`GenexusUIServices.DataTypes.GetSortedTypes`);
  los atributos salen de la caché de objetos del Ctrl+O (`UIServices.Objects.Cache`) y su tipo se lee al mostrarlos.
- `TypePopup` / `Theme`: el desplegable. `OptionsPage` / `Settings` / `History`: configuración e historial.
