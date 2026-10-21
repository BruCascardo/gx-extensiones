# Extensiones para GeneXus 18

Extensiones para el IDE de **GeneXus 18 Upgrade 15** que agilizan la edición de código: completar
parámetros, declarar variables, abrir objetos al instante, atajos al estilo VS Code y más.

**Documentación:** https://brucascardo.github.io/gx-extensiones/
**Descargas:** [último release](https://github.com/BruCascardo/gx-extensiones/releases/latest)

| Extensión | Qué hace | Atajo |
|---|---|---|
| [GxCallParms](src/GxCallParms/README.md) | Al escribir `Objeto.call(` u `Objeto.udp(` completa los parámetros y declara las variables con el tipo del objeto llamado. | `(` / Alt+P |
| [GxCodeKeys](src/GxCodeKeys/README.md) | Mover y duplicar líneas, y múltiples cursores en el editor de código. | Alt+↑↓, Alt+Shift+↑↓, Ctrl+Alt+↑↓ |
| [GxOpenWeb](src/GxOpenWeb/README.md) | Clic derecho en la pestaña de un Web Panel > Abrir > Local, CI o Preprod lo abre en el navegador. | — |
| [GxQuickOpen](src/GxQuickOpen/README.md) | Búsqueda instantánea de objetos con el módulo de cada uno a la vista. | Ctrl+O |
| [GxQuickVar](src/GxQuickVar/README.md) | Declara la `&variable` bajo el cursor eligiendo el tipo de un desplegable. | Alt+V |
| [GxSdtExpand](src/GxSdtExpand/README.md) | Sobre una `&variable` SDT o Business Component escribe `New()` y una asignación por miembro con los `=` alineados. | Alt+S / Alt+Shift+S |
| [GxSdtJson](src/GxSdtJson/README.md) | Copia la estructura de un SDT como JSON y la importa en otro. | — |

Los atajos se cambian en **Tools > Options** de cada extensión.

## Instalar

1. Bajar `GxExtensiones-<versión>.zip` del [último release](https://github.com/BruCascardo/gx-extensiones/releases/latest)
   y descomprimirlo.
2. Cerrar GeneXus.
3. Doble clic en **InstalarPackages.exe** y elegir qué instalar (`all` para todas).

Detalle y solución de problemas en [installer/LEEME.txt](installer/LEEME.txt). También se puede
instalar una sola extensión con el zip suelto del release y su `Instalar.cmd`.

## Correcciones e ideas

- ¿Encontraste un error? [Abrí un issue](https://github.com/BruCascardo/gx-extensiones/issues/new?template=error.yml).
- ¿Tenés una idea o una extensión nueva en mente? [Proponela](https://github.com/BruCascardo/gx-extensiones/issues/new?template=idea.yml).
- Para mandar cambios de código, ver [CONTRIBUTING.md](CONTRIBUTING.md).

## Código

| Carpeta | Contenido |
|---|---|
| [`src/`](src) | Una carpeta por extensión, con su código, su README y sus scripts de instalación. |
| [`tests/`](tests) | Pruebas que corren sin abrir GeneXus. |
| [`installer/`](installer) | `InstalarPackages.exe`, el instalador de todas las extensiones. |
| [`docs/`](docs) | Sitio de documentación. |
| [`scripts/`](scripts) | Publicación de releases y armado del sitio. |

Todo compila junto con `dotnet build GxExtensiones.sln`. El detalle de cada carpeta y cómo compilar
está en [CONTRIBUTING.md](CONTRIBUTING.md#estructura-del-repositorio).

## Licencia

[MIT](LICENSE).
