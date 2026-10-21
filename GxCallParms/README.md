# GxCallParms

Extensión para el IDE de GeneXus 18 (U15): al escribir el `(` de `Objeto(`, `Objeto.call(` u `Objeto.udp(`
en Events, Source, Rules o Conditions (tecleado o insertado por el IntelliSense de GeneXus), completa los parámetros con las variables de la regla `parm` del
objeto llamado y **las declara en el objeto actual con el mismo tipo**.

```genexus
// Cliente_Get: parm(in:&CliId, in:&Contexto, out:&sdtCliente);
Cliente_Get.udp(                    →   &sdtCliente = Cliente_Get.udp(&CliId, &Contexto)

// Lotes.Registrar (procedimiento Registrar del módulo Lotes): parm(in:&LoteId, out:&sdtOutput);
Lotes.Registrar(                    →   &sdtOutput = Lotes.Registrar(&LoteId)

// Cliente_Actualizar: parm(in:&sdtCliente, out:&sdtOutput, inout:&Contexto);
Cliente_Actualizar.call(            →   Cliente_Actualizar.call(&sdtCliente, &sdtOutput, &Contexto)
```

- **Una sola salida**: si el objeto tiene un único parámetro `out:` y es el último, no va entre los
  paréntesis: queda `&Salida = Objeto.udp(&Entrada)`. Con `.call(` pasa lo mismo (se cambia a `.udp`);
  se puede apagar en las opciones y entonces `.call(` pone todos los parámetros entre paréntesis.
- La llamada implícita (`Objeto(` / `Modulo.Objeto(`) se trata como `.udp(`. Si el nombre no es un objeto
  de la KB (`Trim(`, `Format(`…) no hace nada.
- Si ya escribiste `&Resultado = Objeto.udp(`, completa solo las entradas y declara `&Resultado` con el
  tipo de la salida (si no existía).
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
- Todo el texto agregado se deshace con un solo Ctrl+Z.

**Alt+P** (configurable) hace lo mismo a pedido sobre la llamada bajo el cursor, por ejemplo una que ya
estaba escrita con `()` vacíos. También está en el menú contextual del editor y en *Edit*.

En **Tools > Options > GxCallParms** se cambia el atajo, se apaga el completado automático al escribir `(`
o la conversión de `.call(` a `.udp(`. Configuración en `%APPDATA%\GxCallParms\settings.txt`.

**Diagnóstico**: `%APPDATA%\GxCallParms\log.txt` registra qué llamada detectó, cómo resolvió el objeto y qué
completó (se apaga en las opciones). Si no completa algo, ahí está el porqué.

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
  `SourceHelper.DataProviderSignature.CreateReturnValueVariable`), declara las variables
  (`Variable.CopyPropertiesFrom`, como `Variable.MoveToPart` de GeneXus) y escribe el texto en un solo grupo
  de deshacer.
- `OptionsPage` / `Settings`: configuración. `Log`: diagnóstico.
