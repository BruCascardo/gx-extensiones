# Extensiones para GeneXus 18

Un conjunto de extensiones para el IDE de **GeneXus 18 Upgrade 15** que agilizan el día a día con el
código: completar parámetros, declarar variables sin salir del editor, abrir cualquier objeto al instante,
atajos al estilo VS Code y más.

[Descargar el último release](https://github.com/BruCascardo/gx-extensiones/releases/latest){ .md-button .md-button--primary }
[Cómo instalar](instalacion.md){ .md-button }

## Qué incluye

| Extensión | Qué hace | Atajo |
|---|---|---|
| [GxCallParms](extensiones/GxCallParms.md) | Al escribir `Objeto.call(` u `Objeto.udp(` sugiere qué pasar en cada parámetro, como el IntelliSense (variables y miembros de SDT del mismo tipo, valores del dominio), y declara las nuevas con el tipo del objeto llamado. | `(` / Alt+P |
| [GxCodeCheck](extensiones/GxCodeCheck.md) | Al guardar subraya en el código los errores de sintaxis (con paréntesis o comillas de más señalados donde están) y, con un chequeo rápido propio, los de tipos y parámetros (que después confirma la especificación) y lo que no sigue el estándar del equipo; al pasar el mouse explica qué pasa, se puede copiar y ofrece la corrección cuando la hay. También revisa un módulo o la KB entera sin build. | Ctrl+. (correcciones) |
| [GxCodeKeys](extensiones/GxCodeKeys.md) | Mover y duplicar líneas, y múltiples cursores en el editor de código. | Alt+↑↓, Alt+Shift+↑↓, Ctrl+Alt+↑↓ |
| [GxEnumValues](extensiones/GxEnumValues.md) | En el diálogo Enum Values de dominios y atributos, barra de búsqueda que filtra por nombre, descripción y valor, y clic en los encabezados para ordenar. | Ctrl+F (en el diálogo) |
| [GxKbRefs](extensiones/GxKbRefs.md) | En la ventana References, checkbox "Otras KBs" que agrega quién referencia al objeto en las demás KBs, agrupado por KB y con las flechas para seguir expandiendo. | — |
| [GxModuleCache](extensiones/GxModuleCache.md) | En Manage Module References, botón "Limpiar caché" que borra la caché local de NuGet y vuelve a cargar los módulos (para cuando se entró antes de que un módulo terminara de subirse). | — |
| [GxOpenWeb](extensiones/GxOpenWeb.md) | Clic derecho en la pestaña de un Web Panel > Abrir > Local, CI o Preprod lo abre en el navegador. | — |
| [GxQuickOpen](extensiones/GxQuickOpen.md) | Búsqueda instantánea de objetos con el módulo de cada uno a la vista. | Ctrl+O |
| [GxQuickVar](extensiones/GxQuickVar.md) | Declara la `&variable` bajo el cursor eligiendo el tipo de un desplegable; también en la pestaña Variables, en Properties y en los patterns (WorkWithPlus). | Alt+V |
| [GxSdtExpand](extensiones/GxSdtExpand.md) | Sobre una `&variable` SDT o Business Component escribe `New()` y una asignación por miembro con los `=` alineados. | Alt+S / Alt+Shift+S |
| [GxSdtJson](extensiones/GxSdtJson.md) | Copia la estructura de un SDT como JSON y la importa en otro. | — |
| [GxTabScroll](extensiones/GxTabScroll.md) | La rueda del mouse sobre las pestañas de objetos desplaza la barra cuando no entran todas, como en VS Code; Shift+rueda cambia de pestaña y Ctrl+W la cierra. | Rueda / Shift+rueda / Ctrl+W |

Los atajos se cambian en **Tools > Options** de cada extensión.

## Correcciones e ideas

El código está en [GitHub](https://github.com/BruCascardo/gx-extensiones). Si algo no funciona o se te ocurre
una mejora, [reportá un error](https://github.com/BruCascardo/gx-extensiones/issues/new?template=error.yml) o
[proponé una idea](https://github.com/BruCascardo/gx-extensiones/issues/new?template=idea.yml). Para mandar
cambios de código, ver [Contribuir](contribuir.md).
