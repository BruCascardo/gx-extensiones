# Instalación

Requisitos: **GeneXus 18 Upgrade 15** (las extensiones están compiladas para ese upgrade). No hace falta
instalar nada más.

## Todas juntas (recomendado)

1. Bajar `GxExtensiones-<versión>.zip` del
   [último release](https://github.com/BruCascardo/gx-extensiones/releases/latest) y descomprimirlo en
   cualquier carpeta.
2. Cerrar GeneXus.
3. Doble clic en **InstalarPackages.exe**.
    - Si Windows avisa *"Windows protegió su PC"*: *Más información* > *Ejecutar de todas formas*.
4. Aparece la lista de extensiones, cada una con lo que hace y si ya está instalada. Escribir:
    - `all` para instalar todas,
    - `3` para instalar solo la 3,
    - `1,3,5` para instalar varias,

    y Enter. Enter sin escribir nada sale sin instalar.
5. Esperar a que diga *Listo*, apretar una tecla y abrir GeneXus normalmente.

El instalador copia los zip elegidos a `C:\Packages`, los descomprime y los instala todos juntos. Si alguna
ya está instalada y al día, la saltea. Para actualizar a un release nuevo se hace lo mismo.

### Si algo falla

| Mensaje | Qué hacer |
|---|---|
| *Sin permiso para escribir...* | Clic derecho en `InstalarPackages.exe` > *Ejecutar como administrador*. |
| *No encontré GeneXus 18 U15* o *Encontré varias instalaciones* | Abrir una consola en la carpeta y ejecutar `InstalarPackages.exe -GxProgramDir "C:\ruta\a\GeneXus18U15"`. |
| *GeneXus está abierto* | Cerrarlo (revisar también el Administrador de tareas) y volver a ejecutar. |

Otras opciones:

```powershell
InstalarPackages.exe -Reinstalar                         # reinstala aunque estén al día
InstalarPackages.exe -Paquetes all                       # sin menú (para scripts)
InstalarPackages.exe -Paquetes "GxQuickVar,GxSdtJson"
```

## Una sola extensión

Cada release trae también el zip de cada extensión por separado (`GxQuickVar-1.0.4.zip`, por ejemplo).

1. Cerrar GeneXus.
2. Descomprimir el zip en cualquier carpeta.
3. Doble clic en **Instalar.cmd**.

```powershell
.\deploy.ps1 -GxProgramDir 'D:\GeneXus\GX18U15'   # si no la encuentra o hay varias
.\deploy.ps1 -Uninstall                           # desinstalar
```

## Desinstalar

Con el zip de la extensión descomprimido: `Instalar.cmd -Uninstall` (GeneXus cerrado).
