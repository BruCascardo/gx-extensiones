# GxCallParms

Extensión para el IDE de GeneXus 18 (U15): al escribir el `(` de `Objeto(`, `Objeto.call(` u `Objeto.udp(`
en Events, Source, Rules o Conditions (tecleado o insertado por el IntelliSense de GeneXus), **sugiere qué pasar
en cada parámetro mientras escribís**, como el IntelliSense, y declara en el objeto actual, con el tipo del parámetro,
las variables que no existían.

```genexus
// Cliente_Get: parm(in:&CliId, in:&Contexto, out:&sdtCliente);
Cliente_Get.udp(                    →   &sdtCliente = Cliente_Get.udp(&CliId, &Contexto)

// Lotes.Registrar (procedimiento Registrar del módulo Lotes): parm(in:&LoteId, out:&sdtOutput);
Lotes.Registrar(                    →   &sdtOutput = Lotes.Registrar(&LoteId)

// Cliente_Actualizar: parm(in:&sdtCliente, out:&sdtOutput, inout:&Contexto);
Cliente_Actualizar.call(            →   Cliente_Actualizar.call(&sdtCliente, &sdtOutput, &Contexto)
```

## Como el IntelliSense

Se escribe en el mismo editor, sin ventanas: debajo de la línea aparece la **ayuda de parámetros** (la de
GeneXus) con la firma del objeto, el parámetro actual en negrita y su tipo, y en cada lugar vacío se abre la
**lista de autocompletado** con lo que se puede pasar:

- **E**, valores del dominio: si el parámetro es de entrada y su tipo es un dominio enumerado (directo o por el
  atributo en el que se basa), `LogTipo.Debug`, `LogTipo.Error`… con su descripción. Si el dominio está en
  otro módulo va calificado (`Sistema.LogTipo.Debug`).
- **V**, variables del mismo tipo: las declaradas en el objeto con el mismo dominio, atributo, SDT / BC o tipo
  básico (y colección o no).
- **M**, miembros de SDTs y Business Components: aunque no haya una `&sdtOutput`, si hay una variable SDT / BC
  con un miembro de ese tipo aparece `&sdtRespuesta.Output`. Recorre los subniveles y los miembros de tipo
  SDT / BC (hasta 3 niveles); no entra en colecciones. Se suman apenas se ve la primera lista (es lo que más
  tarda, porque carga las estructuras), una sola vez por llamada.
- **+**, la variable del parámetro (`&CliId`) si todavía no existe: se puede elegir, pero nunca se escribe sola.
- **P**, predefinidos: los configurados para ese objeto (ver abajo).

La lista no tiene nada elegido: escribir busca en ella, y ↑ ↓ eligen.

- **Enter** o **Tab** escriben lo elegido y pasan al siguiente parámetro (agregando `, `). Sin nada elegido,
  dejan lo escrito tal cual y también pasan al siguiente. En el último salen del `)`.
- **Esc** cierra la lista; con la lista cerrada, termina de completar (lo escrito queda).
- `,` también pasa al siguiente, y `)` sobre el paréntesis de la llamada sale de ella.
- **Ctrl+Enter** completa los lugares vacíos que faltan con las variables de la regla `parm` (o lo predefinido)
  y termina.
- Al escribir `&` al principio de un lugar, en vez de todas las variables se ven las sugerencias del parámetro
  (si tiene variables para sugerir). Con `.` o cualquier otro carácter sigue la lista de GeneXus como siempre.

Al terminar (último parámetro, `)`, Esc o el cursor fuera de la llamada) cada `&Variable` escrita sola en un
lugar, si no existe, se declara con el tipo de su parámetro. Lo demás (`LogTipo.Debug`, `&sdt.Item`, `10`,
`!"clave"`, una expresión) no se toca.

Se puede apagar en las opciones y entonces completa sin preguntar con las variables de la regla `parm`, como antes.

### Valores predefinidos por objeto

En **Tools > Options > GxCallParms**, una línea por objeto con un lugar por parámetro (en orden) separado por `;`:

```text
prcLog: ?LogTipo.Debug | LogTipo.Error; &Pgmname;
```

- `?A | B` abre la lista con `A` ya elegida (Enter la escribe) y `B` como otra opción.
- Un valor sin `?` se escribe solo al llegar a ese lugar y se pasa al siguiente.
- Vacío (como el tercero, después del último `;`) es un lugar normal, para escribirlo.
- `*`, o no ponerlo, es lo normal para ese parámetro.

Viene con `prcLog` así: sugiere el tipo de log (`LogTipo.Debug` elegido), pone `&Pgmname` y deja el
cursor en el mensaje: `prcLog(LogTipo.Debug, &Pgmname, |)`. El nombre puede ser el del objeto o el
calificado (`Sistema.prcLog`). Las líneas que no se entienden se avisan en la página de opciones y se ignoran.

## Detalles

- **Una sola salida**: si el objeto tiene un único parámetro `out:` y es el último, no va entre los
  paréntesis: queda `&Salida = Objeto.udp(&Entrada)`. Con `.call(` pasa lo mismo (se cambia a `.udp`);
  se puede apagar en las opciones y entonces `.call(` pone todos los parámetros entre paréntesis.
- La llamada implícita (`Objeto(` / `Modulo.Objeto(`) se trata como `.udp(`. Si el nombre no es un objeto
  de la KB (`Trim(`, `Format(`…) no hace nada.
- Si ya escribiste `&Resultado = Objeto.udp(`, completa solo las entradas y declara `&Resultado` con el
  tipo de la salida (si no existía). Si no, la salida es el último lugar de la lista: al terminar, si ahí
  quedó una variable (`&sdtCliente` o `&sdt.Cliente`) sale a `&sdtCliente = Objeto.udp(...)`, y si quedó vacío se saca.
- Dentro de una expresión (`If Objeto.udp(` …) completa las entradas y no agrega la asignación.
- Los nombres son los de la regla `parm` del objeto llamado: la variable, o el atributo si el parámetro es
  un atributo (la variable queda "Based on" ese atributo). La salida de un Data Provider toma el nombre de
  su SDT (`&sdtClientes = Clientes_Get.udp()`).
- El tipo se copia tal cual del objeto llamado (como cuando GeneXus copia una variable a otro objeto):
  dominio, atributo, SDT, colección, largo, etc.
- Las variables que **ya están declaradas no se tocan**; si tienen otro tipo, se avisa en la barra de estado.
  Las que GeneXus había autodefinido toman el tipo del parámetro.
- Solo actúa si los paréntesis están vacíos (`Objeto.call(` al final de la línea o `Objeto.call()`), y no
  en comentarios ni strings. Resuelve el objeto desde el módulo del objeto actual, también con el nombre
  calificado (`Sistema.Global.Cliente_Get.udp(`).
- Sin sugerir (opción apagada), todo el texto agregado se deshace con un solo Ctrl+Z; sugiriendo, se
  deshace como lo que se escribe a mano.

**Alt+P** (configurable) hace lo mismo a pedido sobre la llamada bajo el cursor, por ejemplo una que ya
estaba escrita con `()` vacíos. También está en el menú contextual del editor y en *Edit*.

En **Tools > Options > GxCallParms** se cambia el atajo, se apaga el completado automático al escribir `(`,
la conversión de `.call(` a `.udp(` o las sugerencias en cada parámetro, y se editan los valores predefinidos. Configuración en `%APPDATA%\GxCallParms\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxCallParms\log.txt` registra qué llamada detectó, cómo resolvió el objeto y qué
completó, y cuánto tardó en armar las sugerencias (se apaga en las opciones). Si no completa algo, ahí está el porqué.

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
.\pack.ps1        # arma dist\GxCallParms-<versión>.zip para compartir
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar.

## Cómo funciona

- `KeyFilter`: engancha el editor de código que tiene el foco (`CallFiller.Hook`) y maneja el atajo configurable.
- `CallFiller.Hook`: escucha `DocumentTextChanged`; cuando entra un `(` (tecleado o del IntelliSense) mira,
  diferido, la línea del cursor.
- `CallFiller`: encuentra `Objeto(` / `Objeto.call(` / `Objeto.udp(` en la línea, resuelve el objeto con
  `Objects.ResolveName` desde el módulo del objeto actual (y desde la raíz; por tipo si hace falta), lee los parámetros con
  `ICallableInfo.GetSignatures()` (para un Data Provider agrega su salida con
  `SourceHelper.DataProviderSignature.CreateReturnValueVariable`), sugiere cada lugar (`CallSession`) y declara
  las variables (`Variable.CopyPropertiesFrom`, como `Variable.MoveToPart` de GeneXus). Las sugerencias salen de
  los valores del dominio (`Properties.ATT.EnumValues`), las variables de la parte Variables y los miembros de
  las variables SDT / BC (`StructureInfoProvider`, como GxSdtExpand), comparando el tipo con el del parámetro.
- `CallSession`: usa el IntelliPrompt de Actipro del editor, el mismo de GeneXus: `ParameterInfo` para la firma
  (con `ValidTextRange` entre los paréntesis, así se cierra sola si el cursor sale) y `MemberList` para las
  sugerencias, marcada con su `Context` para distinguirla de las de GeneXus. Las teclas se atienden en
  `KeyTyping`, que el editor dispara antes que a la lista: así Enter / Tab sin nada elegido no escriben la
  sugerencia más parecida, y cualquier otro carácter cierra la lista sin completar. Lo elegido se detecta en
  `IntelliPromptMemberListClosing` (no cancelado) y se pasa al siguiente lugar cuando el texto ya está escrito.
  El `&` de GeneXus llega como `TriggerActivated` (`VariablesListTrigger`). Los argumentos se leen de la línea
  separando por las comas de afuera (no las de otros paréntesis ni strings).
- `Icons`: los íconos de las sugerencias, agregados al final de la `ImageList` de la lista (así los de GeneXus no se corren).
- `Presets`: los valores predefinidos por objeto.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
