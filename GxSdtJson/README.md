# GxSdtJson

Extensión para el IDE de GeneXus 18 (U15) que copia la estructura de un SDT como JSON y la genera a
partir de un JSON. Sirve para pasar ítems de un SDT a otro, o de una KB a otra, sin rehacerlos a mano.

## Uso

Al abrir un SDT aparece la barra **SDT JSON** con dos botones (también en el menú contextual de la
estructura y en el menú **Edit**):

- **Copiar SDT como JSON**: deja en el portapapeles la estructura del SDT abierto (incluye lo que
  editaste y todavía no grabaste).
- **Importar SDT desde JSON...**: abre un cuadro donde pegás el JSON (si en el portapapeles hay un
  JSON, ya viene pegado) y elegís:
  - **Reemplazar la estructura** del SDT abierto.
  - **Agregar al final**: los ítems que ya existen con el mismo nombre se saltean.
  - **Crear un SDT nuevo** con el nombre que pongas. Se crea en la misma carpeta o módulo que el
    objeto activo, se graba y se abre.

  Reemplazar y agregar dejan el SDT modificado sin grabar: si no te sirve, cerralo sin grabar.
  Ctrl+Enter importa.

Sin ningún SDT abierto, **Tools > Crear/importar SDT desde JSON...** crea uno nuevo.

### Copiar y pegar parte de un SDT (Ctrl+C / Ctrl+V)

En la grilla de la estructura:

- **Ctrl+C** copia como JSON las filas seleccionadas (miembros, subestructuras con todo lo que tienen
  adentro, colecciones). Con la fila raíz seleccionada copia el SDT entero. GeneXus copia lo de siempre
  (los atributos y dominios siguen pudiéndose pegar en una transacción); solo cambia el texto.
- **Ctrl+V** en otro SDT (o en el mismo): si el portapapeles tiene ese JSON, inserta los ítems donde
  está parado el cursor, igual que un ítem nuevo: parado en un miembro, después de él; en una
  subestructura o en la raíz, al final. Si ya hay un ítem con ese nombre, el pegado se numera
  (`Valor1`). Se deshace con un Ctrl+Z y el SDT queda modificado sin grabar.

También pega cualquier JSON con el formato de abajo (por ejemplo, el arreglo de ítems escrito a mano).
Si el portapapeles no tiene JSON, Ctrl+V pega como siempre. Editando una celda, Ctrl+C y Ctrl+V son los
del texto.

## Formato

```json
{
  "name": "SDTCliente",
  "description": "Cliente del sistema",
  "items": [
    { "name": "Id", "type": "Numeric(8.0)" },
    { "name": "Nombre", "type": "VarChar(40)", "description": "Nombre completo", "jsonName": "nombre" },
    { "name": "Saldo", "type": "Domain:Importe", "baseType": "Numeric(12.2-)" },
    { "name": "Alta", "type": "Attribute:CliFchAlta", "baseType": "Date" },
    { "name": "Tags", "type": "VarChar(20)", "collection": true },
    { "name": "Contacto", "type": "SDTContacto, Sistema.Global" },
    {
      "name": "Telefonos", "collection": true, "collectionItemName": "Telefono",
      "items": [
        { "name": "Numero", "type": "Character(20)" }
      ]
    }
  ]
}
```

- Un elemento con `items` es un **nivel**; sin `items`, un **miembro** con `type`.
- `type` se escribe como en GeneXus: `VarChar(40)`, `Numeric(10.2-)` (el `-` es con signo), `Date`,
  `Boolean`, un SDT/BC/EO (`SDTContacto`, `SDTContacto, Sistema.Global` o `Sistema.Global.SDTContacto`),
  `Domain:X` o `Attribute:X` (el prefijo es opcional: un nombre suelto que no sea un tipo ni un dominio se
  toma como atributo).
- `baseType` (lo agrega solo al copiar): el tipo real de un dominio o atributo. Si en la KB de destino
  ese dominio/atributo no existe, se usa este tipo y se avisa.
- Se puede agregar ` collection` o `[]` al tipo en vez de `"collection": true`.
- `description`, `jsonName` y `collectionItemName` solo se copian si no son los que pone GeneXus.
- Al importar también se acepta directamente el arreglo de ítems (`[ {...}, {...} ]`), útil para
  escribir a mano o pedírselo a una IA.

Si un tipo no existe en la KB (un SDT de otra KB, por ejemplo) el ítem queda `VarChar(40)` y al
final se muestra la lista de avisos (también en la ventana Output).

## Instalar

Requisitos: **GeneXus 18 Upgrade 15** (está compilada para ese upgrade). No hace falta .NET SDK.

1. Cerrar GeneXus.
2. Descomprimir el zip en cualquier carpeta.
3. Doble clic en **Instalar.cmd** (si Windows avisa que el archivo viene de internet: *Más información* >
   *Ejecutar de todas formas*). O, en esa carpeta, desde PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
```

El script busca la instalación de GeneXus 18 U15, copia `GxSdtJson.dll` a `<GeneXus>\Packages` y
ejecuta `GeneXus.exe /install`.

```powershell
.\deploy.ps1 -GxProgramDir 'D:\GeneXus\GX18U15'   # si no la encuentra o hay varias
.\deploy.ps1 -Uninstall                           # desinstalar
```

Si la barra **SDT JSON** no aparece, activarla con clic derecho sobre la zona de barras de herramientas.

## Compilar

Requiere .NET SDK 8+.

```powershell
dotnet build -c Release
.\pack.ps1                      # arma dist\GxSdtJson-<versión>.zip
dotnet run --project tests      # pruebas del formato JSON
```

Desde el código fuente, **Instalar.cmd** (o `deploy.ps1`) cada vez sube la versión (x.y.z+1), compila
y arma ese zip antes de instalar: para compartir alcanza con tomar el zip más nuevo de `dist\`, que es
lo mismo que quedó instalado. `.\pack.ps1 -SubirVersion` hace lo mismo sin instalar.

## Cómo funciona

- `SdtJsonPackage`: comandos y cuándo se ven (solo con un SDT como documento activo).
- `SdtDocument`: el SDT activo y su editor de estructura. El editor tiene su propia copia de la
  grilla: antes de copiar se le pide que baje los cambios al part (`IGxView.UpdateData`) y después de
  importar se recarga (`IGxView.UpdateView`) dejando el documento modificado.
- `SdtClipboard`: Ctrl+C / Ctrl+V en la grilla. Copiar y pegar los atiende el propio editor de SDT
  (antes que los packages), así que las teclas se toman con un hook de teclado del hilo de la UI. Para
  pegar arma los ítems con `SdtWriter` y los inserta con los comandos del editor (`CmdInsertItem`), así
  se pueden deshacer. Usa por reflexión miembros no públicos del editor de GX 18 U15.
- `SdtNode` / `SdtJsonFormat`: el formato JSON, sin dependencias de GeneXus (probado en `tests\`).
- `SdtReader` / `SdtWriter`: de la estructura del SDT a `SdtNode` y al revés. Los tipos se escriben con
  `DataType.ToString()` y se leen con `DataType.ParseInto`, y si no, buscando el SDT/BC/dominio por nombre.
