# Extensiones para GeneXus 18

Un conjunto de extensiones para el IDE de **GeneXus 18 Upgrade 15** que agilizan el día a día con el
código: completar parámetros, declarar variables sin salir del editor, abrir cualquier objeto al instante,
atajos al estilo VS Code y más.

[Descargar el último release](https://github.com/BruCascardo/gx-extensiones/releases/latest){ .md-button .md-button--primary }
[Cómo instalar](instalacion.md){ .md-button }

## Qué incluye

| Extensión | Qué hace | Atajo |
|---|---|---|
| [GxCallParms](extensiones/GxCallParms.md) | Al escribir `Objeto.call(` u `Objeto.udp(` completa los parámetros y declara las variables con el tipo del objeto llamado. | `(` / Alt+P |
| [GxCodeKeys](extensiones/GxCodeKeys.md) | Mover y duplicar líneas, y múltiples cursores en el editor de código. | Alt+↑↓, Alt+Shift+↑↓, Ctrl+Alt+↑↓ |
| [GxOpenWeb](extensiones/GxOpenWeb.md) | Clic derecho en la pestaña de un Web Panel > Abrir > Local, CI o Preprod lo abre en el navegador. | — |
| [GxQuickOpen](extensiones/GxQuickOpen.md) | Búsqueda instantánea de objetos con el módulo de cada uno a la vista. | Ctrl+O |
| [GxQuickVar](extensiones/GxQuickVar.md) | Declara la `&variable` bajo el cursor eligiendo el tipo de un desplegable. | Alt+V |
| [GxSdtExpand](extensiones/GxSdtExpand.md) | Sobre una `&variable` SDT o Business Component escribe `New()` y una asignación por miembro con los `=` alineados. | Alt+S / Alt+Shift+S |
| [GxSdtJson](extensiones/GxSdtJson.md) | Copia la estructura de un SDT como JSON y la importa en otro. | — |

Los atajos se cambian en **Tools > Options** de cada extensión.

## Correcciones e ideas

El código está en [GitHub](https://github.com/BruCascardo/gx-extensiones). Si algo no funciona o se te ocurre
una mejora, [reportá un error](https://github.com/BruCascardo/gx-extensiones/issues/new?template=error.yml) o
[proponé una idea](https://github.com/BruCascardo/gx-extensiones/issues/new?template=idea.yml). Para mandar
cambios de código, ver [Contribuir](contribuir.md).
