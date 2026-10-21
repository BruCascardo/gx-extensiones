# GxCodeKeys

Extensión para el IDE de GeneXus 18 (U15) que agrega atajos típicos de Visual Studio Code al editor de
código (Events, Source, Rules, Conditions): mover y duplicar líneas, y edición con múltiples cursores.

Los atajos se cambian en **Tools > Options > GxCodeKeys** (también en *Tools > Opciones de GxCodeKeys...*).
Los comandos de líneas y cursores también están en el menú **Edit**.

## Atajos

| Atajo | Qué hace |
|---|---|
| Alt+↑ / Alt+↓ | mueve la línea (o las líneas seleccionadas) una posición arriba o abajo |
| Alt+Shift+↑ / Alt+Shift+↓ | duplica la línea (o el bloque) arriba o abajo y deja el cursor en la copia |
| Ctrl+Alt+↑ / Ctrl+Alt+↓ | agrega un cursor en la línea de arriba o de abajo |
| Alt+Click | agrega un cursor donde hacés clic (o lo quita si ya había uno) |
| Esc o un click normal | vuelve a un solo cursor |

### Mover y duplicar líneas

- Con selección, actúa sobre todas las líneas que toca. Si la selección termina justo al principio de
  una línea (por ejemplo, después de seleccionar líneas enteras con Shift+↓), esa línea no cuenta.
- Mantiene la selección, su sentido y la posición del cursor dentro de las líneas movidas.
- En la primera o la última línea del documento no hace nada.
- La indentación viaja con cada línea tal cual. Si el documento termina sin salto de línea, sigue
  terminando sin salto.
- Cada operación es una sola acción de deshacer: un Ctrl+Z la revierte completa.

### Múltiples cursores

- Con varios cursores, en todos a la vez: escribir, Retroceso, Supr, Enter (copia la indentación de la
  línea), Tab, ← → ↑ ↓, Inicio (primero al comienzo del código y después a la columna 0), Fin,
  Ctrl+← / → (por palabra), Ctrl+Retroceso / Ctrl+Supr (borra por palabra), Ctrl+C, Ctrl+X y Ctrl+V.
- Con Shift (Shift+flechas, Shift+Inicio/Fin, Ctrl+Shift+← / →) cada cursor extiende su propia
  selección, y lo que se escribe después reemplaza todas las selecciones. Si dos selecciones se pisan
  (por ejemplo, Shift+Fin con dos cursores en la misma línea), quedan en una sola, como en VS Code.
- Ctrl+C (o Ctrl+Insert) copia las selecciones de todos los cursores, una por línea; Ctrl+X (o
  Shift+Supr) además las borra. Sin ninguna selección hacen lo de siempre del editor.
- Ctrl+V (o Shift+Insert): si el portapapeles tiene tantas líneas como cursores, pega una línea en cada
  uno (como VS Code). Si no, pega todo el texto en cada cursor. Por ejemplo, con un cursor en cada una de
  estas líneas:

  ```
  &inRecibir.EntRecId =
  &inRecibir.Mensaje =
  &inRecibir.Parametros =
  ```

  Ctrl+Shift+→ hasta seleccionar `EntRecId`, `Mensaje` y `Parametros`, Ctrl+C, Fin, escribir `&` y
  Ctrl+V deja `&inRecibir.EntRecId = &EntRecId`, `&inRecibir.Mensaje = &Mensaje`, etc.
- **Autocompletado**: como en VS Code, la lista se abre en el cursor principal y lo que elegís se escribe
  en todos. Escribir `&` abre la lista de variables (y `.` la de miembros, etc., igual que con un solo
  cursor); Ctrl+Espacio también funciona. Con la lista abierta, lo que escribís para filtrarla se escribe
  en todos los cursores; ↑ ↓ RePág AvPág, Enter, Tab y Esc son de la lista, y ← → Inicio Fin, Supr o Ctrl+V
  la cierran y siguen en todos los cursores.
- Cada tecla es una sola acción de deshacer, aplicada en todos los cursores.
- Ctrl+Alt+↑/↓ mantiene la columna aunque pase por líneas más cortas, y cuenta los tabs como columnas
  visuales. El cursor nuevo pasa a ser el principal, así la vista lo sigue.
- Las demás teclas (Ctrl+Z, Ctrl+↑ / ↓, Ctrl+Inicio/Fin, RePág/AvPág, Shift+Tab...) vuelven a un solo
  cursor y hacen lo de siempre.
- **Alt+arrastrar** sigue haciendo la selección en bloque (por columnas) del editor, como antes.

## Conflictos con otros atajos

Revisé los atajos de los menús de todos los packages de GeneXus 18 U15, la tabla de teclas del editor
de código (ActiproSoftware SyntaxEditor 4.0) y los atajos de GxQuickVar.

| Atajo | Choca con | Solución |
|---|---|---|
| Alt+Shift+↑ / ↓ | la **selección en bloque** por teclado del editor | Dentro del editor de código gana GxCodeKeys. La selección en bloque sigue disponible con Alt+arrastrar y con Alt+Shift+← / →. Si la necesitás vertical por teclado, cambiá el atajo de duplicar en las opciones (por ejemplo a Ctrl+Shift+Alt+↑ / ↓). |
| Alt+Click | el inicio de la **selección en bloque** con el mouse | Un clic sin mover agrega un cursor; si arrastrás, se hace la selección en bloque como siempre. Se puede apagar en las opciones y Alt+Click vuelve a ser el de GeneXus. |
| Alt+↑ / ↓, Ctrl+Alt+↑ / ↓ | nada en GeneXus ni en GxQuickVar (Alt+V, Alt+Shift+V) | En algunas PCs, el driver de video Intel usa **Ctrl+Alt+flecha para rotar la pantalla**. Si te pasa, desactivá esas teclas en el panel de Intel o cambiá el atajo en las opciones. |
| Esc | contraer la selección del editor | Solo se usa mientras hay cursores extra. |

La página de opciones avisa si un atajo choca con GeneXus, con el editor o con GxQuickVar (leyendo su
configuración), o si dos comandos de GxCodeKeys tienen el mismo atajo.

Todo esto aplica solo dentro de los editores de código de GeneXus. En el resto del IDE (grillas,
propiedades, form, otros editores) las teclas hacen lo de siempre, y en los editores de solo lectura no
se modifica nada.

## Limitaciones

El editor de GeneXus no soporta múltiples cursores de forma nativa. GxCodeKeys guarda las posiciones de
los cursores extra, replica cada edición en todas y los dibuja encima del editor. Por eso:

- **Las selecciones de los cursores extra se dibujan encima del texto**, con el color de selección del
  editor semitransparente (el texto no cambia de color como en la selección real).
- **Un subconjunto de teclas**: las teclas que no están en la lista de arriba vuelven a un solo cursor.
  La ayuda de parámetros se muestra solo para el cursor principal.
- **Ctrl+Z vuelve a un solo cursor**: deshace la última edición completa (en todos los cursores),
  pero no restaura los cursores extra. Con la lista de autocompletado abierta, cada tecla (y el ítem
  elegido) se deshace en dos pasos: primero lo de los cursores extra y después lo del principal, que lo
  escribe el editor por su cuenta.
- Si el texto cambia por otra vía (deshacer desde el menú, otro package), los cursores extra se
  corren junto con el texto. Si se recarga el objeto entero, se descartan.
- Los cursores extra se dibujan solo en la vista activa si el editor está dividido. Dentro de una región
  colapsada no se ven.
- Mover o duplicar con varios cursores primero descarta los extra y actúa sobre el principal.
- Mover líneas no salta las regiones colapsadas como una unidad: intercambia con la línea de al lado.
- Alt+arrastrar no desplaza la vista si salís del editor arrastrando.
- Tope de 5.000 cursores.

Si algo falla, el error se informa en la ventana **Output** de GeneXus y el editor sigue funcionando.
Cada operación es una sola modificación o un solo grupo de deshacer, así que un Ctrl+Z la revierte
completa.

Configuración por usuario en `%APPDATA%\GxCodeKeys\`.

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
`GxCodeKeys.dll` a `<GeneXus>\Packages`, le quita la marca de "descargado de internet" y ejecuta
`GeneXus.exe /install`. Después se abre GeneXus normalmente.

```powershell
.\deploy.ps1 -GxProgramDir 'D:\GeneXus\GX18U15'   # si no la encuentra o hay varias
.\deploy.ps1 -Uninstall                           # desinstalar
.\deploy.ps1 -Force                               # instalar en otro upgrade (bajo tu riesgo)
```

Si GeneXus está en Program Files y da error de permisos, abrir PowerShell como administrador.
Se puede tener instalado junto con GxQuickVar: son packages independientes.

## Compilar y empaquetar

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxCodeKeys-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar: para compartir alcanza con tomar el zip más nuevo de `dist\`, que es
lo mismo que quedó instalado. `.\pack.ps1 -SubirVersion` hace lo mismo sin instalar.

El editor de código (ActiproSoftware SyntaxEditor) tiene licencia solo dentro de GeneXus: si se crea
fuera del IDE (por ejemplo, en un programa de pruebas), abre una y otra vez la ventana de compra de
licencia. Por eso las pruebas se hacen instalando el package en GeneXus.

## Cómo funciona

- `CodeKeysPackage`: package UI (comandos del menú Edit, filtro de teclado, página de opciones).
- `KeyboardFilter`: intercepta teclado y mouse antes que el editor, solo si el destino es un editor de
  código de GeneXus (`EditorLocator`: un `SyntaxEditor` dentro de un `GxTextEditor`). Los atajos no van
  en el manifiesto porque ahí son fijos y el enum `Shortcut` de WinForms no admite Ctrl+Alt+flecha.
- `AltKeyGuard`: que soltar Alt después de un Alt+Click o de un atajo con Alt no active la barra de
  menús (Infragistics). La barra marca "Alt pendiente" en cada pulsación de Alt, incluidas las
  repeticiones automáticas mientras se lo mantiene apretado, y si sigue marcado al soltarlo entra en modo
  menú (la L abría Layout). Durante el gesto se manda una tecla sin asignar (`0xE8`), que lo desmarca, y
  un hook de teclado del hilo descarta las repeticiones de Alt. El Alt soltado no se toca. El Alt+Click
  le sigue llegando a la barra (así sale del modo Alt); el editor lo ignora porque se cancela en
  `ViewMouseDown`. La posición del click se calcula con `LocationToOffset(BestFit)`, como el editor: el
  offset del hit-test vale -1 en una línea vacía.
- `LineCommands`: mover y duplicar, cada uno con un solo `ReplaceText`/`InsertText` dentro de un grupo de
  `UndoRedo`.
- `MultiCursor`: una sesión por editor mientras haya cursores extra. Replica cada edición de atrás para
  adelante (así no se corren los offsets) en un solo grupo de deshacer, sigue los cambios externos con
  `DocumentTextChanged` y dibuja los cursores después de cada `WM_PAINT` del editor. Cada cursor extra
  guarda ancla y posición (su selección); las selecciones se pintan solo dentro de la región que el
  editor repintó (`GetUpdateRgn`), porque son semitransparentes y pintarlas dos veces las oscurecería.
  Ctrl+← / → usa el `WordBreakFinder` del lenguaje del editor, así salta igual que con un solo cursor.
  Supr, Ctrl+X/C/V (y sus variantes con Insert/Supr) se toman con un hook de teclado del hilo
  (`KeyboardHook`), porque GeneXus los atiende como comandos del menú Edit antes que cualquier
  `IMessageFilter` y actúan solo en el cursor real.
- Autocompletado con varios cursores: la lista de GeneXus es la `IntelliPromptMemberList` de Actipro, que
  se cierra si el texto cambia fuera del tramo que completa. Por eso, mientras está abierta, sus teclas
  van al editor y `MultiCursor` anota lo que cambia en el cursor principal (`DocumentTextChanged`, más el
  cambio de tipo `AutoComplete` al confirmar) y, cuando el editor termina de procesar la tecla, lo aplica
  en cada cursor extra en la misma posición relativa. Para que la lista no se cierre con esas ediciones,
  durante ellas se la saca de la lista privada de popups del editor (el `ArrayList` que avisa los cambios
  del texto) y después se le corre el inicio del tramo (campo privado `l`), por reflexión. Si eso no se
  encuentra (otra versión de Actipro), se replica todo junto cuando la lista se cierra. Al escribir un carácter con varios cursores se disparan los `KeyPressTrigger`
  del lenguaje en el principal, como hace el editor al tipear (`OnTriggerActivated`, por reflexión porque
  no es público): así `&` abre la lista de variables.
- `DocumentText`: el editor guarda los saltos de línea como `\n` y los offsets cuentan sobre eso. Todo se
  lee con `LineTerminator.Newline` y se escribe con `\n`; al guardar, GeneXus usa el salto de siempre.
- `OptionsPage` / `Settings`: atajos configurables y detección de conflictos.
