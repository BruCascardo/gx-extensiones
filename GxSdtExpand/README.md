# GxSdtExpand

Extensión para el IDE de GeneXus 18 (U15): con el cursor en una línea que tiene solo una `&variable` basada
en un **SDT** o en un **Business Component**, **Alt+S** (configurable) la expande en `New()` y una asignación
por cada miembro, con los `=` alineados.

```genexus
&inParserEntradaLinea               →   &inParserEntradaLinea = New()
                                        &inParserEntradaLinea.Interfase = 
                                        &inParserEntradaLinea.RegTipo   = 
                                        &inParserEntradaLinea.NroLinea  = 
                                        &inParserEntradaLinea.Linea     = 
```

- La variable tiene que estar **sola en la línea** (con la indentación que tenga). También sirve si ya
  escribiste `&Variable = New()`. Se respeta la indentación en todas las líneas generadas.
- El cursor queda al final de la primera asignación, listo para escribir el valor.
- **SDT**: los subniveles simples se recorren (`&sdtCliente.Direccion.Calle = `). Los miembros y
  subniveles que son colección se dejan afuera (se llenan con `Add`) y se avisan en la barra de estado.
- **Business Component**: los atributos del primer nivel de la transacción. Los inferidos y las fórmulas
  se dejan afuera porque no se pueden asignar; los niveles subordinados son colecciones y también se
  avisan. Una variable de un nivel del BC (`Cliente.Telefono`) expande los atributos de ese nivel.
- Si la variable es una colección (o un SDT colección), avisa que hay que usar la expansión completa.
- También sirve con una variable de un ítem de SDT (`sdtCliente.Telefono`).
- Todo el texto agregado se deshace con un solo Ctrl+Z.

## Expansión completa: Alt+Shift+S

Lo mismo, pero despliega **todo**: los miembros de tipo SDT o BC se recorren como subestructuras
(`&sdtPedido.Cliente.Nombre = `) y cada **colección** se expande en una variable del ítem, que se declara
con el tipo del ítem, y se agrega con `Add`:

```genexus
&sdtCliente = New()
&sdtCliente.Nombre          = 
&sdtCliente.Direccion.Calle = 
&sdtCliente.Tags.Add()

&Telefono = New()
&Telefono.Numero = 
&Telefono.Tipo   = 
&sdtCliente.Telefonos.Add(&Telefono)
```

- La variable del ítem se llama como el *Collection item name* del SDT (o como el nivel del BC). Si ya existe
  con ese tipo se reutiliza; si el nombre está tomado por otro tipo se numera (`&Telefono2`).
- Las colecciones dentro de colecciones se expanden igual, cada una con su `Add` a la variable del ítem
  de arriba. Una colección de un tipo simple (`VarChar` collection) queda como `&X.Tags.Add()`.
- Sobre una variable colección (o un SDT colección) expande el ítem y lo agrega a la variable:
  `&sdtCliente = New()` … `&sdtClientes.Add(&sdtCliente)`.
- Un SDT que se contiene a sí mismo no se expande en bucle: esa parte queda como asignación o se saltea
  (se avisa en la barra de estado).

Los dos comandos están en el menú contextual del editor y en *Edit*.

En **Tools > Options > GxSdtExpand** se cambian los atajos, se apaga la línea `= New()` y se elige alinear los
`=` con **tabulaciones** (según el tamaño de tab del editor) en vez de espacios. Configuración en
`%APPDATA%\GxSdtExpand\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxSdtExpand\log.txt` registra el tipo de la variable, cómo lo resolvió y qué
expandió (se apaga en las opciones). Si no expande algo, ahí está el porqué.

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
.\pack.ps1        # arma dist\GxSdtExpand-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- `KeyFilter`: atiende los dos atajos configurables cuando el foco está en un editor de código.
- `SdtExpander`: toma la línea del cursor, busca la variable en el `VariablesPart` del objeto y resuelve su
  tipo como GeneXus (`StructureTypeReference.Deserialize` + `StructureInfoProvider.GetItem`): da el nivel
  del SDT o de la transacción, sea la raíz, un ítem de SDT o un nivel del BC. Recorre sus miembros, declara
  las variables de ítem con el tipo que arma GeneXus para ese ítem (`AttCustomType.CreateFromReference`) y
  reemplaza la línea en un solo grupo de deshacer.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
