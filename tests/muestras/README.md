# Muestras de prueba

Archivos `.xls` para probar la importación de Dairy Comp. **Todos son inventados**: IDs, estados y fechas salen de un generador con semilla fija. Nunca se guarda aquí un Excel real de un tambo (constitución, principio 6); el `.gitignore` excluye cualquier `.xls`/`.xlsx` fuera de esta carpeta.

Los genera `tests/Aviss.Excel.Tests/GeneradorMuestras.cs`. Un test (`MuestrasTests`) comprueba que lo que está acá coincide byte a byte con lo que genera el código, y otro que no haya archivos que el generador no conozca.

## Archivos

| Archivo | Qué es | Qué debe pasar al importarlo |
|---|---|---|
| `dairy-comp-226-vacas.xls` | 226 vacas, columnas de Dairy Comp (`ID, CVET, FPART, LACT, DEL, FCELO, VINS, UTORO, COM, RPRO, FAPAR`), fechas como texto con espacios, fila final `Total:226` | Se lee: 226 filas de datos |
| `filas-con-problemas.xls` | `ID` vacío, repetido, `ñ1`/`Ñ1`, de 21 caracteres, con espacio en el medio, decimal, estado desconocido y `Total:12` | Filas excluidas con su motivo; el resto entra |
| `vacio.xls` | 0 bytes | Rechazo: archivo vacío |
| `corrupto.xls` | Cabecera de Excel viejo seguida de basura | Rechazo: archivo dañado |
| `xlsx-renombrado.xls` | Un `.xlsx` de verdad con extensión `.xls` | Rechazo: formato no soportado |
| `texto-plano.xls` | Un texto con extensión `.xls` | Rechazo: formato no soportado |
| `con-contrasena.xls` | `.xls` cifrado (registro FILEPASS de Excel 97) | Rechazo: protegido con contraseña |
| `xlsx-con-contrasena.xls` | Contenedor de un `.xlsx` con contraseña | Rechazo: protegido con contraseña |
| `sin-hojas.xls` | Libro válido sin hojas | Rechazo: sin hojas |
| `sin-columna-id.xls`, `sin-columna-rpro.xls` | Falta una columna obligatoria | Rechazo indicando cuál |
| `encabezado-duplicado.xls` | `lact` y `LACT` | Rechazo indicando la columna |
| `solo-encabezados.xls` | Encabezados sin filas | Rechazo: sin filas de datos |

## Cómo regenerarlos

Solo hace falta si se cambia el generador. Desde la raíz del repo, en PowerShell:

```powershell
$env:AVISS_GENERAR_MUESTRAS = "1"
dotnet test tests/Aviss.Excel.Tests --filter "Categoria=Generador"
Remove-Item Env:AVISS_GENERAR_MUESTRAS
```

Después se corre `dotnet test` normal y se commitean los archivos que cambiaron.
