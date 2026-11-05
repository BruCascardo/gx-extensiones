# GxKbRefs

Extensión para el IDE de GeneXus 18 (U15): amplía la ventana **References** (Ctrl+F12) para ver quién
referencia al objeto en las **demás KBs**, no solo en la que está abierta. Útil para los objetos públicos de
un módulo que se usa desde otras KBs: antes de cambiar sus parámetros se ve a quién afecta.

La ventana queda igual que siempre; solo se agrega el checkbox **Otras KBs**, al lado de *Group References
by Type*. Viene destildado, así la ventana carga tan rápido como siempre.

| En la ventana References | Qué hace |
|---|---|
| **Otras KBs** tildado | Debajo de *Is Referenced By* aparece un recuadro por cada KB donde el objeto se usa, con un árbol de quién lo referencia en esa KB |
| Flecha de un objeto | Igual que en el árbol de siempre: trae quién referencia a ese objeto en la misma KB |
| *Group References by Type* | También agrupa por tipo dentro de cada KB |
| Clic derecho > **Copiar nombre** | Copia el nombre calificado (`Modulo.Objeto`) de los nodos elegidos |

- El título de cada recuadro es la KB y la cantidad de objetos que referencian al analizado. Cada recuadro
  tiene justo el alto de sus nodos visibles y crece o se achica al expandir o contraer; si no entran, el área
  tiene scroll (también con la rueda sobre los árboles) y se puede agrandar arrastrando su borde superior.
- Las KBs de **fixes** (las que tienen *Fixes* en el nombre) no se revisan salvo que se tilde **Incluir
  Fixes**, arriba a la derecha del área.
- Como en el árbol original, las referencias débiles se muestran en cursiva y un objeto no se vuelve a
  expandir si ya está más arriba en la misma rama.
- También aparecen las llamadas **por servicio**, que GeneXus no registra como referencia: el objeto que
  llama usa un valor de un dominio enumerado cuya descripción es la ruta del objeto llamado (por ejemplo
  `Sistema.ServicioTipo.COTIZACION_COTIZACION_GET`, con descripción `Servicios/WS_Cotizacion/Cotizacion/Get`).
  Esos nodos llevan al final *(servicio ServicioTipo.COTIZACION_COTIZACION_GET)*. Sirve para los objetos que
  ni siquiera existen en la otra KB, como los procedimientos de Servicios.
- Se muestran solo los objetos **propios** de cada KB. Las copias que una KB tiene de un módulo
  referenciado (por ejemplo, los objetos de *Sistema* dentro de otra KB) se descartan: sus llamadas aparecen
  en la KB dueña del módulo.
- Al cambiar de objeto en la ventana se vuelve a buscar. Debajo de *Is Referenced By* queda un resumen
  (*184 referencias en 3 de 9 KBs. Sin las 1 de fixes.*) o qué KB no se pudo leer.
- Solo se amplía *Is Referenced By*. *Has References To* sería igual en todas las KBs, porque el objeto
  es el mismo.

## Qué KBs revisa

- Las KBs de la **carpeta que contiene a la KB abierta** (por ejemplo, todas las de `C:\KBGXSERVER\CORE`), o
  de las carpetas que se configuren en las opciones: las que tienen `knowledgebase.connection`, hasta dos
  niveles abajo.
- Las bases `GX_KB_*` adjuntas al **mismo servidor SQL** que la KB abierta (por ejemplo,
  `(LocalDB)\MSSQLLocalDB`), aunque estén en otra carpeta.

No abre esas KBs en GeneXus: lee directo las tablas de su base (`ModelCrossReference`, la misma información
que usa la ventana References), con los datos de conexión de la KB abierta. Una KB de la carpeta que nunca
se abrió en esta máquina no está adjunta al servidor: se adjunta con el `.mdf` de su carpeta, lo mismo que
hace GeneXus al abrirla (queda adjunta, como las demás).

- El objeto se busca por su GUID, que es el mismo en la KB dueña del módulo y en las que lo referencian.
- Las referencias son las de la última vez que se guardó o se importó en cada KB, igual que en la ventana
  original.
- Las llamadas por servicio se buscan en el código guardado (eventos, reglas, código del procedimiento,
  condiciones) de los objetos que usan el dominio, no en las variables. No se detecta el valor escrito como
  texto sin el dominio (por ejemplo `SerId = !"COTIZACION_COTIZACION_GET"`), porque esos objetos no tienen
  referencia al dominio. Se usa la descripción del valor, no la tabla de servicios de la aplicación.
- En **Tools > Options > GxKbRefs** se pueden cambiar las carpetas de KBs, excluir KBs (nombre de la KB o
  de su base, separados por coma, por ejemplo las de capacitación), cambiar el texto que marca una KB de
  fixes y si *Incluir Fixes* arranca tildado, y apagar la extensión. Configuración en
  `%APPDATA%\GxKbRefs\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxKbRefs\log.txt` registra la carga del package y los errores al leer cada KB
(se apaga en las opciones).

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

Requiere .NET SDK 8+ (las referencias de GeneXus se bajan del feed NuGet oficial, ver `nuget.config`;
`Artech.Core.UI` y los ensamblados de Infragistics se toman de la instalación de GeneXus).

```powershell
dotnet build -c Release
.\pack.ps1        # arma dist\GxKbRefs-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- La ventana References es `CallBrowserToolWindow` (en `Artech.Core.UI`), con un `CallBrowserControl`
  adentro. GeneXus crea una por objeto, con un Guid distinto cada vez, y le da el foco: `ReferencesWatcher`
  sube desde el control con el foco en `Application.Idle` hasta encontrar el `CallBrowserControl` y le
  agrega `ReferencesExtender` una sola vez.
- `ReferencesExtender` pone el checkbox en el panel de arriba y, dentro del panel de *Is Referenced By*, un
  área acoplada abajo (con un `Splitter`) sin mover el árbol original. Se entera del objeto analizado por el
  label con su nombre, que `CallBrowserControl` actualiza cada vez, y lo lee de su campo `m_AnalyzedObject`.
- `KbDiscovery` arma la lista de KBs: las carpetas con `knowledgebase.connection` y las bases del servidor
  (`sys.databases`), con la conexión de `KnowledgeBase.ConnectionInfo`. A las que no están adjuntas les pone
  `AttachDBFilename` con su `.mdf`.
- `KbDatabase` busca en cada KB el objeto por GUID y sus referencias con el mismo filtro que
  `CallBrowserControl.LoadInTree` (vínculos 1 y 4 fuertes). Descarta los objetos con la
  propiedad `ObjectIsInterface` (copias de módulos referenciados); como los ids de propiedad cambian de una
  KB a otra, se buscan por nombre. Para las llamadas por servicio busca los dominios cuya propiedad
  `IDEnumDefinedValues` (XML de los valores) tiene un valor con la ruta del objeto como descripción, y en las
  partes de código de los objetos que referencian a ese dominio (gzip con un encabezado de GeneXus, tokens en
  XML) busca el nombre del valor como palabra entera. La búsqueda inicial corre en segundo plano y en paralelo por KB; al
  expandir un nodo se consulta en el momento.
- `KbGroup` es el recuadro (`GroupBox`) de cada KB y `KbTree` su árbol, un `GxUltraTree` como el original,
  sin scroll propio: se ajusta al alto de los nodos visibles y pasa la rueda del mouse a la lista. El alto
  sale de medir en el primer árbol dibujado la primera fila y el paso entre filas (`RowMetrics`). La lista
  (`KbList`) es un `FlowLayoutPanel` que no se desplaza solo al darle el foco a un árbol.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
