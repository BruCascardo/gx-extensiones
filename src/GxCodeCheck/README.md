# GxCodeCheck

Extensión para el IDE de GeneXus 18 (U15): al guardar marca los errores **en el código**, con un
subrayado ondulado como en VS Code, en lugar de dejarlos solo en el Output. Al dejar el mouse sobre el subrayado
aparece un cartel que explica qué pasa y qué revisar, con el mensaje original de GeneXus. El texto se
puede seleccionar y copiar.

| Marca | Qué es | Cuándo aparece |
|---|---|---|
| Rojo fuerte: onda doble, fondo rojo y la línea teñida | Error de sintaxis: lo que GeneXus no te deja guardar | Al guardar |
| Rojo fuerte | Error de tipos y parámetros (lo que hace fallar el build): un parámetro de otro tipo o de más, una asignación entre tipos distintos... | Al guardar (chequeo rápido), y la especificación lo confirma unos segundos después |
| Naranja | Advertencia: una línea que termina en `+` justo antes de otra instrucción, una variable no definida (mal escrita), un parámetro `in:` que se asigna... | Al guardar o unos segundos después |
| Punteado gris | Consecuencia de otro error: se arregla sola cuando corregís el que la provoca | Junto con ese error |

Si el editor muestra el margen de indicadores (el de los breakpoints), las líneas con error llevan además
un círculo rojo. Al guardar con errores, la barra de estado dice cuántos hay y dónde está el primero.

Mientras escribís no revisa nada (no frena el editor): los subrayados se corren con el texto y al volver
a guardar se reemplazan. Con el cursor sobre un subrayado, el error también se ve en la barra de estado
de GeneXus.

## El cartel

- **Qué pasa**, en castellano y con los nombres del código: *"Sobra 'x': lo que está antes ya es una
  instrucción completa"*, *"No existe la función 'Trimm'. ¿Quisiste decir 'Trim'?"*, *"Los tipos no
  coinciden en '&Valor.Estado = ""': a la izquierda es domValorEstado y el valor es Character"*.
- De dónde sale (parser, especificación o revisión del texto), el código (`src0257`, `spc0010`...) y la
  línea.
- El mensaje original de GeneXus, para buscarlo en la wiki o compararlo con el Output.
- **Copiar** (abajo a la derecha) copia todo, con el objeto, la parte, la línea y la columna. También se
  puede seleccionar un pedazo con el mouse y copiarlo con Ctrl+C.
- Se cierra solo cuando el mouse se va del subrayado y del cartel, o con Esc.

### Correcciones

Cuando GxCodeCheck sabe cómo arreglarlo, el cartel ofrece la corrección abajo a la izquierda: un clic la
aplica (se deshace con Ctrl+Z) y el subrayado se va.

| Error | Corrección |
|---|---|
| Miembro de un SDT, método o valor de un dominio mal escrito (`&Cupon.Nombr`, `&Texto.Trimm()`, `domEstado.ACTVO`) | Cambiar por el que se parece |
| Función que no existe y se parece a una que sí (`Lenn(&x)`) | Cambiar por esa función |
| Variable no definida | Declararla con GxQuickVar (si está instalada) |
| Punto suelto | Borrarlo |
| Bloque sin su End, cuando la sangría dice dónde va | Agregar la línea con el End, con la sangría del bloque |
| Literal donde va un valor de dominio (`"AC"`) | Cambiar por `domEstado.ACTIVO` |
| `Commit` o `Rollback` | Cambiar por `prcCommit.Call()` o `prcRollback.Call()` |
| `.Trim()` sobre un dominio enumerado | Sacarlo |
| `and` en un `Where` | Pasar la condición a otra cláusula `Where` (si no hay un `When`) |
| Commit on exit en Yes | Ponerlo en No (se aplica al guardar el objeto) |

Sin mouse: con el cursor sobre lo marcado, **Ctrl+.** (como en VS Code; se cambia en las opciones) abre el
cartel con el foco, para elegir la corrección con Tab y Enter o con su número.

La corrección se ubica donde está ahora el subrayado, aunque hayas escrito desde que guardaste. Si en ese
lugar el texto cambió, no toca nada y la barra de estado lo avisa: al guardar se vuelve a revisar.

## Errores que confunden

Hay errores en los que el Output de GeneXus muestra muchos mensajes que no se entienden, o lo marca en
otra línea. GxCodeCheck los ordena así:

- **Un paréntesis, un corchete o unas comillas de más o de menos.** Con `Trim(&Texto))` GeneXus no
  marca nada en esa línea y da errores en las de abajo. GxCodeCheck revisa que cada `(`, `[` y `{` se
  cierre, que cada texto cierre sus comillas en la línea y que cada `/*` tenga su `*/`. Marca el
  símbolo que sobra o el que no se cierra, y los errores que vienen después quedan en gris, como
  consecuencia.
- **Un `.` suelto.** Un punto al final de una línea (`&Texto = &Otro.`) hace que GeneXus siga la
  instrucción en la línea siguiente (`&Otro.If ...`) y se pierda hasta el final del objeto: da un error
  por cada `Where`, `Case`, `EndFor` o `EndIf` que encuentra. Se marca el punto y todo lo demás queda en
  gris. Si lo que quedó mal al final de la línea es otra cosa, el primer error (*'If' is an unexpected
  expression*) avisa que hay que revisar el final de la línea anterior, y los comandos de bloque que
  GeneXus "no esperaba" quedan como consecuencia mientras los bloques del texto cierren bien.
- **Un comentario sin `//`.** Una línea como `Creo el lote con 2 detalles` da un error por palabra; queda
  un solo error para la línea: *"Esta línea no es código GeneXus. Si es un comentario, empezala con
  '//'"*.
- **Una letra o una palabra de más** al final de una instrucción (`&Numero = 5 x`): GeneXus pide una
  asignación que no tiene nada que ver; el cartel dice que sobra la `x`.
- **Una línea que termina en `+`, `-`, `*` o `/`** antes de otra instrucción: GeneXus las une en una sola
  expresión sin dar error (`&Numero = 5 + &Fecha = Today()`) y falla recién al especificar. Queda como
  advertencia en el operador.
- **Un `For`, `If`, `Do While`, `Do Case`, `Sub`, `Event` o `New` sin su End.** GeneXus avisa que falta
  el `EndFor` en cualquier lado y además dice que las subrutinas "no están definidas" (quedaron adentro
  del bloque abierto). Se marca el bloque que no se cierra, con dónde iría el End según la sangría; si hay
  varios anidados, la sangría dice a cuál le falta. Lo demás que informó GeneXus queda como consecuencia.
- **Un End de más**, o bloques cruzados (un `EndFor` dentro de un `If`): se marcan el bloque que quedó sin
  cerrar y el End que sobra.
- Los chequeos de paréntesis, comillas, puntos y bloques solo corren cuando GeneXus no pudo guardar: si guardó, la
  estructura está bien y no se marca nada que no sea un error.
- Los mensajes que GeneXus repite o deja sin completar (`'{0}' is an unexpected expression`) quedan una
  sola vez y como consecuencia del error anterior.

## Errores de tipos y parámetros

GeneXus no informa los errores de tipos al guardar: los encuentra la **especificación**, varios segundos
después. Por eso GxCodeCheck tiene un chequeo propio, el **chequeo rápido**, que corre al guardar y tarda
milisegundos (no usa el especificador). Revisa los casos comunes:

- **Llamadas a procedimientos** contra su `parm()`: parámetros de más o de menos y de otro tipo, con
  `.Call()`, `.Udp()`, `.Submit()` o sin, y usados como función (`&x = Proc(...)`, donde el último
  parámetro es el resultado).
- **Métodos de textos y fechas** (`CharAt`, `Substring`, `IndexOf`, `Replace`, `AddDays`...): parámetros
  de más, de menos o de otro tipo. Un método mal escrito (`&Texto.Lenght()`) queda como advertencia con
  el nombre correcto.
- **Colecciones**: `Add`, `AddRange`, `Item`, `Remove`, `IndexOf`, también en los SDT que son colección por
  su tipo (`Messages`): `&Out.Messages.AddRange(&Otro.Output)` avisa que va la colección, no el SDT.
- **Miembros de los SDT** que no existen (`&Sdt.Nombr`, con el nombre parecido que sí existe).
- **Valores de dominios enumerados** que no existen (`domEstado.ACTVO`): el parser no los revisa al
  guardar y la especificación sí.
- **Asignaciones y comparaciones** entre tipos distintos: `&Numero = "abc"`, `&Sdt.Id = &Texto`,
  `If &Texto = 1`, `Where CliId = &Texto`, `&Texto = &A + &B` con números.
- **Parámetros**: un `in:` del propio objeto que se asigna (advertencia, como en GeneXus) y un
  procedimiento usado como función cuyo último parámetro es `in:` (no devuelve nada).
- **Condiciones** que no son verdaderas o falsas: `If &Texto`, `Do While &Sdt`.
- **Textos que no entran**: `&Codigo = "ABCD"` con un `Character(3)` (advertencia: GeneXus lo corta).
- **Variables no definidas** (advertencia).
- **Objetos que no existen** en un `.Call()`, `.Udp()` o `.Submit()` (el build falla). GeneXus no deja
  guardar uno así, pero puede pasar en un objeto que no se tocó si el llamado cambió de nombre o de módulo.

Solo juzga lo que entiende seguro: variables, miembros de SDT, literales, atributos, valores de dominios,
llamadas y las operaciones simples entre ellos (números entre sí, textos sumados, fecha más días,
comparaciones). Lo que no entiende lo deja pasar: un texto más un número, las funciones del lenguaje
(`Format`, `iif`...) más allá de lo que devuelven, los Data Providers, los Business Components y sus
métodos, las propiedades de los controles, los parámetros `out:` que reciben un valor fijo. Lee las
variables del objeto, los SDT y el `parm()` de cada procedimiento llamado desde la KB abierta. Lo que no
es del objeto editado lo recuerda: un `parm()` hasta que ese procedimiento se guarda, los SDT y atributos
hasta que se guarda un SDT, una transacción o un dominio. Al abrir un objeto, mientras no se usa el teclado
ni el mouse, lee de antemano los SDT de sus variables y el `parm()` de lo que llama, para que el primer
guardado tampoco espere: en una KB de prueba, de 200 ms por guardado pasó a 1 a 3 ms.

### Al cambiar el parm() de un procedimiento

Si guardás un procedimiento con el `parm()` cambiado (un parámetro más o menos, otro tipo, `in:` en lugar
de `out:`; cambiar solo el nombre de un parámetro no cuenta), GxCodeCheck revisa los objetos que lo usan
(menos los que tienen Generate Object en False) y avisa cuáles quedaron mal, sin esperar al build:

- En el **Output**, un error por llamada que quedó mal: con doble clic se abre ese objeto en esa línea.
- En la **barra de estado**, cuántos de los objetos que lo usan quedaron mal.
- Si alguno está abierto, **subrayado** en su editor.

Cuando un parámetro falta o sobra, el mensaje dice cuál probablemente es: *"Faltan parámetros: X recibe 3
parámetros y tiene 2. Probablemente falta el parámetro 2: &Importe (Numeric(10.2))"*. Revisa de a un
objeto por vez (unos milisegundos cada uno) y solo mientras no usás el teclado ni el mouse. Se compara
con el `parm()` que tenía el procedimiento al abrirlo.

Cuando termina la especificación de lo guardado, sus errores reemplazan a los del chequeo rápido: son los
que valen. GeneXus 18 especifica en segundo plano cada objeto que se guarda (es el Output
*Background Build*, que normalmente nadie mira) y GxCodeCheck subraya esos errores en el código del
objeto. También marca los de cada Build o Rebuild.

- La especificación tarda unos segundos (en un procedimiento grande, 5 a 10): es lo que tarda GeneXus,
  que antes de especificar el objeto prepara el modelo durante 1 a 4 segundos. Mientras tanto la barra de
  estado dice *esperando la especificación de ...* y al terminar cuántos errores encontró.
- GeneXus espera 3 segundos después de guardar antes de especificar; GxCodeCheck lo baja a 300 ms
  (se cambia en las opciones).
- Al guardar se borran los errores anteriores del objeto. Si volvés a guardar antes de que termine la
  especificación, lo que esa especificación informe se descarta: era del código anterior.
- La especificación da solo la línea: se marca lo que nombra el mensaje (el objeto llamado, el
  parámetro, la colección) o, si no se encuentra, la instrucción entera.
- Una variable que no está definida (*spc0047*, casi siempre un nombre mal escrito que GeneXus
  autodefine) se marca en todos los lugares donde aparece.
- Solo se especifican en segundo plano los objetos que ya se compilaron alguna vez en el ambiente
  activo; un objeto nuevo muestra sus errores de especificación después del primer Build.

## Revisar un módulo o la KB sin build

**Tools > GxCodeCheck: revisar el código...** (o el botón de la hoja con el tilde verde, en la barra GxCodeCheck) corre el chequeo rápido sobre muchos objetos a la vez, para
saber antes de un build (o antes de subir los cambios) qué lo va a hacer fallar. Se elige qué revisar:

| Qué | Para qué |
|---|---|
| **Mis cambios pendientes de commit**, **y los objetos que usan lo cambiado** | Antes de subir: lo que tocaste y lo que tu cambio puede haber roto (quién llama al procedimiento al que le cambiaste el `parm()`, quién usa el SDT al que le sacaste un miembro, el dominio al que le cambiaste un valor). Son los objetos que muestra el diálogo de Commit de GeneXus Server: lo que trajo un Update no está, y no depende del usuario (que suele ser compartido) |
| **Todo lo modificado desde** una fecha y hora | Lo mismo, en una KB sin GeneXus Server. Incluye lo que trajo un Update |
| **Un módulo**, con sus submódulos | Revisar un área entera |
| **La KB entera** | Una revisión general |

Revisa el Source de los procedimientos y los Events de los Web Panels y las transacciones. No revisa los
objetos con **Generate Object** en False (el build no los genera, así que no lo pueden hacer fallar; el
resumen dice cuántos salteó) ni los de los módulos de solo lectura (los de GeneXus y los instalados). Lo que encuentra va al **Output
"GxCodeCheck"**: un renglón por error con el objeto y la línea, y con doble clic se abre el objeto ahí.
También queda subrayado en el editor cuando abrís el objeto, hasta que lo guardes. Al final, el diálogo,
el Output y la barra de estado dicen cuántos objetos se revisaron y cuántos tienen errores.

Por defecto informa los errores (lo que rompe el build); con **Incluir las advertencias y el estándar del
equipo** también lo demás. El diálogo recuerda lo último que elegiste, y abre con el módulo del objeto que
estás editando.

Tarda lo que GeneXus en leer cada objeto de la base, unas decenas de milisegundos: en una KB de prueba, 14
objetos en 0,25 s. Revisa de a un objeto por vez sin trabar GeneXus, y se puede cancelar.

**No reemplaza al build.** Encuentra lo que el chequeo rápido entiende (ver *Errores de tipos y
parámetros*), sobre lo que está guardado (no lo que tengas sin guardar en un editor). No revisa lo que
solo sale al especificar la navegación (un atributo fuera de la tabla extendida, un For Each sin tabla
base), los Data Providers, las reglas ni lo que depende del generador. Los errores de sintaxis no hacen
falta: GeneXus no deja guardar un objeto con uno, y tampoco deja borrar un objeto que otro usa.

## Estándar del equipo

Al guardar también marca, como advertencia, lo que GeneXus acepta pero el estándar del equipo no. La
especificación no lo revisa, así que queda marcado hasta el próximo guardado:

| Regla | Ejemplo | Qué propone |
|---|---|---|
| Un literal donde va un valor de un dominio enumerado | `&Estado = "AC"`, `Where CliEstado = "AC"`, `Proc("AC")` | `domEstado.ACTIVO` (busca qué valor del dominio es) |
| `.Clone()` | `&Items.Add(&Item.Clone())` | instanciar con `&Item = new()` antes de cargarlo |
| `Commit` o `Rollback` | `Commit` | `prcCommit.Call()` o `prcRollback.Call()` |
| Submit directo | `EnviarPago.Submit(...)` | crear `EnviarPago_submit` y hacer submit de ese |
| Funciones o métodos en un `Where` | `Where CliNombre = &Nombre.Trim()` | calcular el valor antes en una variable |
| `and` en un `Where` | `Where CliId = &Id and CliEstado = &Estado` | otra cláusula `Where` (si no hay un `or`) |
| `.Trim()` sobre un dominio enumerado | `&Estado.Trim()` | sin `.Trim()` |
| If dentro del Else | `Else` / `If ...` / `EndIf` / `EndIf` | `Do Case` |
| Commit on exit en Yes | un procedimiento con la propiedad en Yes | No: el commit lo hace quien llama |

Lo de `When` en un `Where` no cuenta: se calcula antes del For Each. Un literal vacío (`""`) o `0` que no
es un valor del dominio tampoco: es "sin valor". Cada regla se puede apagar en las opciones.

## Opciones

En **Tools > Options > GxCodeCheck**:

- Activado (apagado no marca nada).
- El atajo de las correcciones (Ctrl+. por defecto).
- Chequeo rápido de tipos al guardar.
- Al cambiar el `parm()` de un procedimiento, revisar los objetos que lo llaman.
- Marcar lo que no sigue el estándar del equipo, y cuáles de sus reglas.
- Marcar los errores de la especificación, y también las advertencias.
- Cuántos milisegundos después de guardar especifica GeneXus en segundo plano (por defecto 300; GeneXus
  trae 3000).
- Registro de diagnóstico en `%APPDATA%\GxCodeCheck\log.txt`: cada mensaje de GeneXus que lee, cuánto
  tarda el chequeo rápido, cuántos segundos después de guardar empieza y termina la especificación y, al
  terminar, qué encontraron los dos (*Comparación de ...*): lo que solo encontró la especificación es lo
  que al chequeo rápido se le escapa, y lo que solo encontró el chequeo, un error que no existe.

Configuración en `%APPDATA%\GxCodeCheck\settings.txt`.

## Cómo funciona

- No corre el parser por su cuenta (en un objeto grande tarda segundos): lee del **Output** de GeneXus
  los errores que GeneXus ya informa al guardar y al especificar, con el objeto, la parte y la línea de
  cada mensaje. Funciona en las partes con código: Source, Events, Rules, Conditions, el Source de los
  Data Providers.
- La especificación es la que GeneXus ya hace al guardar (opción *Background Build*, activada por
  defecto) y en los builds.
- Lo único que hace por su cuenta, al guardar, es revisar el texto (paréntesis, comillas, comentarios,
  operadores al final de la línea) y el chequeo rápido de tipos, que llevan milisegundos.
