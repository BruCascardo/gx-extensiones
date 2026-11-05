# GxTabScroll

Extensión para el IDE de GeneXus 18 (U15): cuando hay más pestañas de objetos abiertas de las que entran
en la barra, la **rueda del mouse** sobre la barra la desplaza, sin tener que ir a las flechas `<` `>`.
Copia el comportamiento de las pestañas de VS Code:

| Sobre la barra de pestañas | Qué hace |
|---|---|
| Rueda hacia abajo / hacia arriba | Desplaza la barra a la derecha / a la izquierda, sin cambiar la pestaña activa |
| Rueda inclinada a la derecha / a la izquierda (mouse o touchpad con desplazamiento horizontal) | Igual |
| **Shift** + rueda | Activa la pestaña siguiente / anterior (no da la vuelta al llegar a un extremo) |

- Funciona solo con el mouse sobre la barra de pestañas; en el resto de la pantalla la rueda hace lo de
  siempre.
- Si todas las pestañas entran en la barra, desplazarla no hace nada (como las flechas).
- Con la pantalla dividida en grupos de pestañas, actúa sobre el grupo que está bajo el mouse.

Además, **Ctrl+W** con el foco dentro de una pestaña de objeto la cierra, como en Chrome o VS Code. Es lo
mismo que Ctrl+F4 (si hay cambios sin guardar, GeneXus pregunta como siempre). Con el foco en una ventana
acoplada (KB Explorer, Properties, Output...) o en un diálogo, Ctrl+W no hace nada.

En **Tools > Options > GxTabScroll** se puede:

- Apagarla.
- Invertir el comportamiento: la rueda cambia de pestaña y Shift+rueda desplaza la barra (lo mismo que
  `workbench.editor.scrollToSwitchTabs` en VS Code).
- Elegir cuántas pestañas se desplaza la barra por cada paso de la rueda (1 a 5, por defecto 1).
- Apagar Ctrl+W.

Configuración en `%APPDATA%\GxTabScroll\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxTabScroll\log.txt` registra la carga del package y los errores (se apaga en
las opciones).

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

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`; las
de Infragistics se toman de la instalación local de GeneXus).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxTabScroll-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- La barra de pestañas de GeneXus es un `UltraTabbedMdiManager` de Infragistics: cada grupo de pestañas
  (`MdiTabGroup`) se dibuja en un `MdiTabGroupControl` que es solo la barra; los objetos son ventanas aparte.
  Infragistics no hace nada con la rueda sobre esa barra.
- `WheelFilter` (un `IMessageFilter`) mira `WM_MOUSEWHEEL` y `WM_MOUSEHWHEEL`. Busca la ventana bajo el
  mouse (no la que tiene el foco, que suele ser el editor) y, si es un `MdiTabGroupControl`, se queda con el
  mensaje y llama a `MdiTabGroup.Scroll(Next|Previous)` (lo mismo que las flechas) o, con Shift,
  a `MdiTab.Activate()` de la pestaña de al lado.
- La rueda vertical se toma como horizontal, hacia abajo = a la derecha, igual que el `scrollYToX` de la
  barra de pestañas de VS Code. Los deltas parciales de los touchpads se acumulan hasta completar un paso.
- `KeyFilter` (otro `IMessageFilter`) atiende Ctrl+W: si el foco está dentro de un hijo MDI (una pestaña),
  se queda con la tecla (y con su `WM_CHAR`) y despacha el comando `Close` del package Core de GeneXus, el
  mismo de Ctrl+F4.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
