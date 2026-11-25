# Plan — 007 Exportación

Spec: [spec.md](spec.md)

> **Ajuste 2026-11-24:** columna de evento `Evento1` (provisoria); SCTIM = hora del registro; CVET 0 con registro vigente sale como cualquier otra vaca (revisado el mismo día, ver pregunta 8 de la spec); la exportación es por rodeo importado (último vigente por vaca, sin acumular entre rodeos). El resto del plan no cambia: donde dice `EVENTO` leer `Evento1`, y las preguntas 5, 7 y 8 ya están resueltas.
>
> **Ajuste 2026-11-24 (2):** solo se exporta el tacto de la **jornada** del rodeo (día local del registro vigente más reciente); no hay lógica ni preguntas sobre segundos tactos. En `ConsultaExportacion` el filtro por jornada se aplica sobre los registros ya traídos (`JornadaDeTacto`); el resumen muestra la fecha de la jornada. La pregunta 10 está resuelta.
>
> **Estado de la spec:** `aprobada` (2026-11-24). Este plan se escribió antes de la aprobación formal con la **recomendación** de cada pregunta abierta como valor provisional; la aprobación cerró todas con lo recomendado. Si el responsable cambia una recomendación, se cambia en un solo lugar (ver "Lo provisional").

## Resumen
Un servicio de exportación lee de SQLite el **último registro vigente** de cada vaca, lo convierte en seis valores de texto (fecha, hora, evento, días de preñez, días de aborto, resultado) y se los pasa a un **exportador** (en `Aviss.Excel`) que toma el **original guardado en el rodeo**, agrega las columnas nuevas a la derecha y devuelve los bytes del archivo en CSV o .xls. El servicio **reabre y compara** el resultado con el original (constitución §2), recién entonces lo guarda en Descargas con un puerto de Android y deja la constancia en una tabla nueva. Una sola pantalla: lista de rodeos, resumen, elegir formato, resultado con "Compartir".

## Lo provisional (cada valor vive en un solo lugar)
| Pregunta | Valor provisional | Dónde vive |
|---|---|---|
| 1 Nombres de columnas (2026-11-24: evento = `Evento1` provisorio, el nombre real del Daily se consigue después; los otros tres, sin confirmar) | `SCDAT`, `SCTIM` (fijos), `Evento1`, `DIASPRE`, `DIASABO`, `RESABO` (provisorios) | `ColumnasExportacion` (Application): una lista con nombre, orden y tipo; cambiar un nombre es cambiar una línea |
| 3 CSV (codificación y separador) | `;`, Windows-1252, encabezados entre comillas, `;` final, CRLF | `ConvencionCsvDairyComp` (Excel), solo para originales `.xls`; un original CSV se respeta byte a byte |
| 4 Lugar de entrega | Descargas + Compartir | puerto `IEntregaArchivo` (Application) con la implementación Android aparte |
| 12 Nombre del archivo | `<original sin extensión>_aviss_AAAAMMDD_HHmm.<ext>` | `NombreArchivoExportado` (Application, función pura) |
| 9 Decimales de .xls a CSV | punto | `ConvencionCsvDairyComp` |

## Base de datos

### Entidad nueva `Exportacion` (Domain)
Constancia de cada archivo entregado (RF-21). Solo se agrega. Campos: `Id`, `RodeoId`, `FechaUtc` (fecha y hora exactas de la entrega), `ArchivoNombre`, `Formato` (`Csv` o `Xls`, enum nuevo `FormatoExportacion`), `Hash` (SHA-256 del archivo, 64 caracteres), `CantidadFilas`, `CantidadVacasConEvento`, `CreadoEnUtc`. Sin `ActualizadoEnUtc` (nunca cambia). Sin veterinario ni tablet (spec 006). Sus reglas (hash de 64, cantidades no negativas, nombre no vacío) van en `Exportacion.Registrar(...)` y se repiten como CHECK en la base.

### Migración `Exportaciones` (después de `EventosConPasos`)
- `dotnet ef migrations add Exportaciones -p src/Aviss.Infrastructure -s src/Aviss.Infrastructure` genera la tabla con su clave foránea a `Rodeo` y su CHECK; después se **completa a mano** con `migrationBuilder.Sql`:
  - índice `IX_Exportacion_RodeoId_FechaUtc` (para "la última exportación del rodeo");
  - triggers `TR_Exportacion_NoModificar` y `TR_Exportacion_NoBorrar` (RAISE ABORT), igual que los de las demás tablas de solo agregar;
  - CHECK de `Formato IN ('Csv','Xls')`, de `length(Hash) = 64` y de las cantidades `>= 0`.
- No reconstruye ninguna tabla existente ni toca `Historial`: **no se agrega ninguna acción nueva al historial** (reescribir su CHECK exigiría reconstruir la tabla y recrear sus triggers; la constancia propia ya cubre la auditoría). `Down` borra la tabla.
- `Rodeo.MarcarExportado` ya existe (spec 001): solo se lo empieza a usar. No hay cambio de esquema en `Rodeo`.
- Se verifica con `dotnet ef migrations has-pending-model-changes`.

## Capas y servicios

| Capa | Qué se agrega | RF |
|---|---|---|
| Domain | `Exportacion` y `FormatoExportacion`. Regla pura `FormatoColumnasExportacion` (`Fecha(utc, zona)` → `dd/MM/yyyy`; `Hora(utc, zona)` → `HHmm`; cultura invariante). | RF-6, RF-7, RF-21 |
| Application | **Contratos y reglas puras**: `ColumnasExportacion` (nombre, orden); `ValoresExportacion` (los seis textos de una vaca); `RegistroExportable` (dato crudo: caravana, tipo de registro, fecha UTC, orden de guardado, nombre del evento, días informados, días de aborto, resultado); `JornadaDeTacto` (de los registros vigentes del rodeo calcula el día local del más reciente y descarta los de otros días) y `SelectorRegistroExportable` (de la lista de registros de una vaca de esa jornada devuelve el último vigente por fecha, y con empate por orden de guardado); `ArmadorValoresExportacion` (de un `RegistroExportable` y la zona devuelve los `ValoresExportacion`, con las reglas de qué columna va vacía según el evento); `NombreArchivoExportado`. | RF-6 a RF-12 |
| Application (puertos) | `IConsultaExportacion` (registros exportables del rodeo, resumen y última exportación); `IExportadorArchivo` (implementado en Excel); `IEntregaArchivo` (guardar en Descargas y compartir; implementado en Android); `IExportacionRepository` (agregar y buscar la última). | RF-2, RF-18 a RF-21 |
| Application (servicio) | `IExportacionService`: `ListarRodeosAsync()`, `ObtenerResumenAsync(rodeoId)`, `ExportarAsync(rodeoId, formato, ct)` → `Resultado<ExportacionEntregadaDto>` (nombre, lugar, cantidades). Códigos de error nuevos en `CodigoError`: `ExportacionNoValida`, `ColumnaDuplicada`, `ArchivoNoGuardado` (más los que ya existen: `NoEncontrado`, `ArchivoRechazado`). Mensajes fijos en español, sin datos del tambo. | RF-1 a RF-3, RF-14, RF-18, RF-23, RF-24 |
| Infrastructure | `ConsultaExportacion` (consultas por lote, ver abajo), `ExportacionRepository`, `ExportacionService` (singleton como `ImportacionService`: un scope por operación, `SemaphoreSlim` para la parte que escribe), configuración EF y registros en `DependencyInjection`. | RF-2, RF-12, RF-21, RF-25 |
| Excel | `ExportadorDairyComp : IExportadorArchivo` con dos escritores (`EscritorXls`, `EscritorCsv`) y un `ValidadorExportacion`. Ver "Cómo se arma el archivo". | RF-4, RF-5, RF-15 a RF-18 |
| App (compartida) | `ExportarViewModel`, `INavegacion.IrAExportarAsync`, botón "Exportar" en `InicioViewModel`. | RF-1 a RF-3, RF-28 |
| App (Android) | `EntregaArchivoAndroid` en `Platforms/Android` (constitución §5): `MediaStore.Downloads` para guardar y un `Intent` `ACTION_SEND` con el `content://` de MediaStore para compartir (el `Share` de MAUI solo recibe rutas de archivo y no sirve para un archivo de MediaStore). | RF-19, RF-20, RF-24 |

### Consulta de datos (`ConsultaExportacion`)
Sin consultar vaca por vaca (20.000 vacas: nada de N+1). Dentro de **una sola transacción de lectura** (WAL: una instantánea; RF-25) se traen tres listas y se unen en memoria:
1. Animales del rodeo no anulados: `Id`, `NumeroCaravana`, `DatosOriginalesJson` (de ahí sale CVET con `ReglasCvet.EntraEnTacto`; sin columna CVET entra).
2. Tactos del rodeo con `Tipo = Normal` y `Anulado = 0`: fecha, orden, `EstadoNuevoId`, `EventoId`, `DiasPreniezInformados`, `DiasAborto`.
3. Rechazos del rodeo con `Anulado = 0`: fecha, orden, `EventoId`, `DiasAborto`.
Más el catálogo de estados (nombre y `ClaveEstandar`). Con eso:
- **Nombre del evento:** tacto con `EventoId` → nombre de ese evento; tacto sin `EventoId` → nombre del estado nuevo; rechazo con `EventoId` → nombre de ese evento; rechazo sin `EventoId` → "Rechazo" (constante única, la misma que usa la ficha).
- **Resultado del aborto:** tacto de Aborto → `ReglasResultadoAborto.DesdeClaveEstado(clave del estado nuevo)` y su etiqueta (`Vacía OK`, `Inseminada`, `Seca`, `Fresca`); rechazo con `DiasAborto` → `Rechazo`. Se reutiliza la regla de la spec 005; no se vuelve a escribir.
- Las vacas con CVET 0 no se tratan aparte: si tienen registro vigente en la jornada entran en el diccionario como cualquier otra y, si no, quedan fuera y salen vacías (RF-11, decisión 2026-11-24).
- El diccionario resultante es por **clave de caravana** (`Caravana.Clave`, la misma regla de unicidad de la base y de la importación).

### Cómo se arma el archivo (`ExportadorDairyComp`)
Entrada: los bytes del original (`Rodeo.ArchivoOrigenBlob`), el formato pedido y el diccionario por clave de caravana. Es CPU: corre en `Task.Run` desde el servicio.
1. **Reconocer el original** con la misma detección del perfil (OLE2 = `.xls`, texto = CSV/TXT). Para eso `PerfilDairyComp` expone, en la misma clase y como `internal`, el método que abre el libro (hoy privado dentro de `LeerInterno`: `HSSFWorkbook` o `LectorBiffTolerante` si NPOI falla) y `LectorCsv` expone la decodificación y la partición en registros. No se duplica nada de la importación.
2. **Mapear filas a vacas** con las reglas de la importación: se busca la columna `ID`; cada fila se evalúa con `ReglasIdImportacion.Evaluar`; las filas con ID inválido y **todas** las de un ID repetido no reciben valores (`ValidadorImportacion` ya las excluye así). La fila `Total` tampoco.
3. **Colisión de nombres:** si algún encabezado original coincide (sin mayúsculas) con una columna nueva que no sea `SCDAT`/`SCTIM`, se devuelve `ColumnaDuplicada` sin escribir nada (RF-14). **(modificado 2026-11-24)** `SCDAT`/`SCTIM` que el original trae se reemplazan solo en las vacas con tacto y las que faltan se crean y se avisan (`PlanColumnasExportacion` decide, y lo usan el exportador, los dos escritores y el validador; `ResultadoGeneracionArchivo.ColumnasCreadas` y `ExportacionEntregadaDto.ColumnasCreadas` llevan el aviso a la pantalla).
4. **Escribir según formato:**
   - **.xls:** se agregan las columnas **sobre la hoja ya abierta** (en memoria) y se escribe el libro a un `MemoryStream`. Fecha, hora y evento van como celdas de **texto**; días, como número. Si el original es CSV, la hoja sale de `LectorCsv.ComoHoja` (los valores quedan como los lee la importación).
   - **CSV con original CSV:** trabajo **a nivel de texto**. Se decodifica con la misma lógica (`UTF-8` con o sin marca, UTF-16 o Windows-1252), se recorre registro por registro respetando comillas y saltos dentro de un campo, y a cada registro se le agregan los campos nuevos al final. Si la línea ya termina en el separador (como las de Dairy Comp: `...;     0 ;`), se agregan los campos nuevos cada uno seguido de su separador, sin dejar una columna vacía en el medio; si no termina en separador, se agrega primero uno. El encabezado nuevo va entre comillas si el original los traía entre comillas. Se vuelve a codificar con la **misma** codificación y la **misma** marca de orden de bytes, y se conservan los saltos de línea de cada línea. Así cada línea original queda igual byte por byte hasta el punto de agregar.
   - **CSV con original .xls:** se renderiza la hoja a texto con `ConvencionCsvDairyComp`: texto tal cual; números en forma invariante sin ceros de más; fechas como las muestra la celda; fila `Total` como `Total:;N` si el original la tenía.
5. **Devolver** los bytes, la extensión y los conteos (filas, filas con valores).

### Validación (`ValidadorExportacion`, RF-18)
Se reabre el archivo generado con `PerfilDairyComp.Leer` (sirve para los dos formatos) y se lo compara con la lectura del original (también con `PerfilDairyComp.Leer`): misma cantidad de filas; encabezados originales iguales y en el mismo orden, seguidos de los nuevos; **cada celda original igual** (texto y tipo, tal como las lee la importación); para cada fila, el valor de cada columna nueva es el esperado según el diccionario (vacío en las que no tienen registro). Cualquier diferencia devuelve `ExportacionNoValida` con un mensaje fijo (sin valores) y no se guarda nada. Las comparaciones nunca escriben los valores en mensajes ni en logs.

### Servicio y orden de pasos (`ExportacionService.ExportarAsync`)
1. Leer el rodeo y su original (si no existe o está de baja: `NoEncontrado`, RF-23).
2. `ConsultaExportacion` arma el diccionario (instantánea única).
3. `IExportadorArchivo.Generar` (fuera del hilo de la pantalla).
4. `ValidadorExportacion` reabre y compara.
5. `IEntregaArchivo.GuardarAsync` escribe en Descargas (en Android, con `IS_PENDING` hasta terminar: el archivo no es visible a medias). Si falla, se borra lo parcial y se devuelve `ArchivoNoGuardado`.
6. **Recién ahora**, en una transacción corta: `Exportacion.Registrar` y `Rodeo.MarcarExportado`. Si esto falla, el archivo ya está guardado: se devuelve el resultado correcto con un aviso de que no quedó la constancia (nada de lo guardado se pierde, RF-22).
7. Devolver nombre, lugar y conteos. Compartir lo hace la pantalla con `IEntregaArchivo.CompartirAsync`.

## Pantallas

**`ExportarPage` + `ExportarViewModel`**, ruta `Exportar`, botón "Exportar" nuevo en `InicioPage` (debajo de "Importar rodeo", 72 de alto, texto 24).
- **Estado 1, lista:** rodeos activos agrupados por establecimiento, cada fila con el nombre y la cantidad de vacas. Botón grande "Volver".
- **Estado 2, resumen** (al tocar un rodeo): "N de M vacas con evento", "Sin evento: K" y "Última exportación: dd/mm/aaaa HH:mm, N vacas". Debajo, dos botones grandes: **"Generar CSV"** y **"Generar Excel (.xls)"** (el formato se elige acá, al final; ninguno viene elegido) y "Volver".
- **Estado 3, generando:** indicador de actividad y botones deshabilitados; si se vuelve atrás se cancela y no se guarda nada.
- **Estado 4, resultado:** nombre del archivo, "Guardado en Descargas", cantidades, botones "Compartir" y "Listo". Los errores se muestran con el mensaje fijo y un "Reintentar".
- Vertical 800x1340, botones de 72 dp, alto contraste, sin escritura. Regla de testeo del proyecto: `ExportarViewModel` va en `src/Aviss.App/ViewModels/` (sin tipos de MAUI) y se prueba enlazado desde `Aviss.Application.Tests` con fakes en `ViewModels/Soporte/` (`FakeExportacionService`, `FakeEntregaArchivo`).
- **Qué no hace la pantalla:** no elige carpeta, no pide permisos, no muestra caravanas ni valores.

## Archivos
- **Crear:**
  - `src/Aviss.Domain/Entidades/Exportacion.cs`, `Enums/FormatoExportacion.cs`, `Reglas/FormatoColumnasExportacion.cs`.
  - `src/Aviss.Application/Exportacion/` (`ColumnasExportacion`, `ValoresExportacion`, `RegistroExportable`, `SelectorRegistroExportable`, `ArmadorValoresExportacion`, `NombreArchivoExportado`, `DtosExportacion`, `MensajesExportacion`) y los puertos (`IConsultaExportacion`, `IExportadorArchivo`, `IEntregaArchivo`, `IExportacionRepository`, `IExportacionService`).
  - `src/Aviss.Infrastructure/Servicios/ExportacionService.cs`, `ConsultaExportacion.cs`, `ExportacionRepository.cs`, configuración EF y `Migrations/<fecha>_Exportaciones.cs` (+ `.Designer.cs`).
  - `src/Aviss.Excel/Exportacion/` (`ExportadorDairyComp`, `EscritorXls`, `EscritorCsv`, `ConvencionCsvDairyComp`, `ValidadorExportacion`).
  - `src/Aviss.App/ViewModels/ExportarViewModel.cs`, `Paginas/ExportarPage.xaml` (+ `.cs`), `Platforms/Android/EntregaArchivoAndroid.cs`.
  - Tests: ver "Estrategia de tests".
- **Modificar:**
  - `src/Aviss.Excel/PerfilDairyComp.cs` y `LectorCsv.cs`: sacar a métodos `internal` la apertura del libro, la decodificación y la partición en registros (sin cambiar lo que hacen).
  - `src/Aviss.Excel/DependencyInjection.cs` (registrar `IExportadorArchivo`), `src/Aviss.Infrastructure/DependencyInjection.cs` (servicio, consulta, repositorio), `AvissDbContext` (`DbSet<Exportacion>`), `src/Aviss.App/MauiProgram.cs` (página, ViewModel, `IEntregaArchivo` de Android), `AppShell.xaml.cs` (ruta), `NavegacionShell` e `INavegacion`, `InicioViewModel` y `InicioPage.xaml` (botón), `CodigoError`.
  - Documentos al cierre: `docs/arquitectura.md` (sección 7, "Exportación", y la entidad `Exportacion`), `docs/plan.md`, `docs/requerimientos.md`, `MEMORY.md`, `CLAUDE.md` si cambia algo del dominio.

## Decisiones
- **Se arma desde la copia guardada en el rodeo y se genera un archivo nuevo** — es lo que dice la constitución §2 y lo que pidió el responsable. Alternativa descartada: pedirle al veterinario el archivo original otra vez o editarlo en su lugar; rompe "el original no se modifica" y en Android no hay ruta (llega un `content://`).
- **Dos caminos de escritura (editar la hoja abierta y editar el texto), no uno solo** — un CSV de Dairy Comp trae espacios de relleno, decimales con coma y un `;` final; si se lo lee a una tabla de celdas y se lo vuelve a escribir, esos detalles cambian (`12,5` pasaría a `12.5`, `" 20001 "` a `20001`), y la exportación debe conservar los valores originales. Alternativa descartada: una sola ruta "leer todo a una tabla y escribir de nuevo en cualquier formato": más simple, pero altera el original en CSV.
- **En .xls se agrega sobre la hoja abierta y no se reescribe celda por celda** — así se conservan los estilos cuando NPOI puede abrir el libro. Si NPOI no puede y entra `LectorBiffTolerante`, se pierden estilos pero **no valores**. Alternativa descartada: reconstruir siempre el libro: pierde estilos aunque no haga falta.
- **Fecha y hora en .xls como texto** — `dd/mm/yyyy` y `0905` salen exactamente así; una celda de fecha o número dependería del formato regional del programa que la abra y perdería el cero. Alternativa descartada: celdas con formato de fecha y de número: ambiguas.
- **Reabrir y comparar con el mismo perfil de importación** — es la validación que pide la constitución §2 y reutiliza código probado. Alternativa descartada: comprobar solo la cantidad de filas: no detecta un valor cambiado.
- **Las filas se cruzan por ID con las reglas de la importación** — las vacas excluidas al importar (ID inválido o repetido) no reciben valores porque en la base no existen como animal. Alternativa descartada: guardar el número de fila de cada animal (migración nueva sobre `Animal` y reimportar): innecesario, `ID` ya identifica a la vaca.
- **Consultas por lote en una instantánea** — 20.000 vacas; un viaje por vaca no cumpliría el tope. Alternativa descartada: usar `IRegistroCampoRepository.ObtenerUltimoVigenteAsync` por animal (una consulta cada una).
- **Tabla `Exportacion` propia, de solo agregar, sin acción de historial** — permite "última exportación" y deja huella y hash. Alternativa descartada: guardar en `Historial`; exige reescribir su CHECK y recrear triggers. Alternativa descartada: no guardar nada; no habría aviso de exportación repetida ni auditoría.
- **El nombre del evento sale del catálogo al exportar** — decisión P5 del responsable; si se renombró un evento después de tactar, sale el nombre actual. Alternativa descartada: guardar una copia del nombre en cada registro: duplica datos y complica la migración.
- **La constancia se agrega después de guardar el archivo** — si algo falla antes no queda "exportado" un rodeo sin archivo. El caso opuesto (archivo sin constancia) no pierde nada. Alternativa descartada: registrar primero; puede dejar un rodeo marcado sin archivo.
- **Descargas + Compartir con puerto aparte** — sin permisos especiales y el veterinario lo encuentra desde cualquier aplicación. Alternativa descartada: carpeta privada de la app y compartir sí o sí: no se puede copiar por cable sin pasos. Provisorio hasta la pregunta 4.
- **Rechazar si la columna nueva ya existe** — no hay forma segura de elegir cuál manda. Alternativa descartada: renombrar la nueva (`SCDAT_2`): Dairy Comp no la reconocería.
- **Una sola pantalla con la lista de rodeos** — menos toques (constitución §9) que repetir "establecimiento y rodeo" en dos pasos. Alternativa descartada: reusar la pantalla de elección de trabajo: tiene avance y mangas que acá no aplican.
- **No se agregan filas para las altas a nivel animal** — el archivo de vuelta mantiene las filas del original; agregar filas nuevas puede crear animales en Dairy Comp. Alternativa descartada: agregarlas al final: sin validar con Dairy Comp (prueba 0.2).
- **Sin cambios en `Animal`, `Tacto` ni `Rechazo`** — todo lo necesario ya está guardado desde la 005.

## Estrategia de tests
Tests primero. Las muestras son `tests/muestras/dairy-comp-226-vacas.xls` y `tests/muestras/dairy-comp-282-vacas.csv` (anonimizadas, ya en el repo); no se commitea ningún archivo real.

| RF | Test (proyecto › clase › caso) |
|---|---|
| RF-1, RF-2 | Infrastructure.Tests › `ExportacionServiceTests` › lista solo rodeos activos por establecimiento; resumen con vacas con y sin evento, la de CVET 0 tactada a propósito incluida, y última exportación |
| RF-3, RF-28 | Application.Tests › `ExportarViewModelTests` › formato sin elegir no genera; los dos botones; volver; cancelar al generar; errores con mensaje fijo |
| RF-4, RF-5 | Excel.Tests › `ExportadorDairyComp*Tests` › mismas filas y columnas con la muestra de 226 (.xls) y la de 282 (CSV) en los cuatro cruces; el arreglo de bytes del original en la base queda igual (hash antes y después) |
| RF-6, RF-7 | Domain.Tests › `FormatoColumnasExportacionTests` › fecha y hora en zonas distintas, `0905`, `0010`, medianoche local, cambio de horario de verano |
| RF-8 | Application.Tests › `ArmadorValoresExportacionTests` › nombre (no código) de cada evento, evento renombrado, evento creado a mano, Rechazo fijo |
| RF-9, RF-10 | Application.Tests › `ArmadorValoresExportacionTests` › días de preñez solo en fecha previa; días de aborto 0 y 400; los cinco resultados; Aborto con Rechazo |
| RF-11 | Application.Tests › `ArmadorValoresExportacionTests` y Excel.Tests › vacas sin registro (CVET 0 sin registro incluida), fila excluida, ID repetido y fila `Total` con columnas nuevas vacías |
| RF-12 | Application.Tests › `JornadaDeTactoTests` › registros de un día anterior no salen, día local con zona y cerca de la medianoche; `SelectorRegistroExportableTests` › anulado ignorado; corregido con "Editar"; empate por orden de guardado; Infrastructure.Tests › `ConsultaExportacionTests` con tactos, rechazos y anulaciones reales |
| RF-13 | Excel.Tests › columna `RPRO` idéntica celda por celda en los cuatro cruces |
| RF-14 | Excel.Tests › original con columna `scdat` en minúscula rechaza sin generar; Infrastructure.Tests › `ExportacionServiceTests` › no registra nada |
| RF-15 | Excel.Tests › `EscritorCsvTests` › CSV a CSV con la muestra de 282: cada línea original igual byte por byte hasta el punto de agregar, `;` final sin columna vacía, comillas en el encabezado, Windows-1252 conservado (Ñ y tildes), UTF-8 con marca, UTF-16, campo con salto de línea; .xls a CSV con la convención |
| RF-16 | Excel.Tests › `EscritorXlsTests` › fecha y hora como texto, días numéricos, una hoja, libro que NPOI no abre pasa por `LectorBiffTolerante` |
| RF-17 | Excel.Tests › los cuatro cruces con la misma lista de vacas dan los mismos valores |
| RF-18 | Excel.Tests › `ValidadorExportacionTests` › detecta fila de menos, valor original cambiado, columna faltante y valor nuevo equivocado; Infrastructure.Tests › si falla la validación no se guarda ni se registra |
| RF-19, RF-20, RF-24 | Infrastructure.Tests › `ExportacionServiceTests` con `FakeEntregaArchivo`: nombre sin pisar, sin espacio, falla de escritura sin constancia; Application.Tests › `NombreArchivoExportadoTests`; el guardado real y el compartir en el emulador |
| RF-21 | Infrastructure.Tests › `ExportacionRepositoryTests`, `EsquemaExportacionTests` (UPDATE y DELETE rechazados, CHECK de hash y formato, clave foránea) y `MigracionExportacionesTests` (base de la versión anterior con datos: nada cambia, tabla nueva, `has-pending-model-changes` limpio) |
| RF-22 | Infrastructure.Tests › un rodeo importado nuevo no trae nada del anterior; la exportación no cambia datos y se puede repetir; seguir tactando después |
| RF-23 | Infrastructure.Tests › rodeo inexistente o de baja; original ilegible |
| RF-25 | Infrastructure.Tests › `ConsultaExportacionTests` › un tacto guardado mientras se exporta no mezcla momentos (lectura en una sola transacción) |
| RF-26 | Infrastructure.Tests › `ExportacionRendimientoTests` (categoría Rendimiento, `[Collection("Rendimiento")]`) › 20.000 vacas con registros, cada formato, tope 15 s |
| RF-27 | Infrastructure.Tests y Application.Tests › los mensajes de error no traen caravanas ni nombres; el archivo no trae columnas de veterinario ni de tablet |

## Riesgos
- **Dairy Comp puede no aceptar columnas extra** (pregunta 2, prueba 0.2 bloqueada por permisos). El plan agrega solo columnas a la derecha, sin tocar las originales; si las rechaza habrá que definir otra forma de entrega en otra spec.
- **Nombres de columnas provisorios** (pregunta 1): están aislados en `ColumnasExportacion`, pero los tests y la validación los usan por esa lista, no por texto escrito en cada uno.
- **CSV .xls a CSV:** decimales, fechas y la fila `Total` salen con una convención supuesta (pregunta 9); solo hay una muestra real.
- **Archivos .xls que NPOI no sabe abrir:** al pasar por `LectorBiffTolerante` se pierden estilos (no valores).
- **Fidelidad de tipos:** una celda de fecha real en un `.xls` se lee como texto `dd/MM/yyyy` y se vuelve a escribir como texto; el valor visible no cambia, el tipo sí. La validación lo compara como lo lee la importación.
- **Memoria:** 25 MB de original más el generado más el validado en memoria a la vez; en tablets chicas hay que medirlo en el cierre (RF-26).
- **Android:** `MediaStore.Downloads` y el compartir se comportan distinto según el fabricante; se prueba en la tablet real (SM-X110) y en el emulador. Si no hay tablet, queda como pendiente de verificación manual.
- **SQLite con un solo escritor:** la constancia espera si hay un tacto guardándose; es una escritura chica y se serializa con la misma regla que las demás.
- **CVET 0 con registro guardado** sale como cualquier otra vaca (decisión del responsable del 2026-11-24, tras la spec 009; antes salía vacía); el resumen ya no tiene una línea aparte (pregunta 8).
- **Alcance por jornada:** la "jornada" se toma como el día local del registro más reciente del rodeo (no el día en que se exporta), para que exportar pasada la medianoche no deje el archivo vacío. Es una interpretación de la decisión del 2026-11-24; la pantalla muestra la fecha de la jornada en el resumen.
- **Advertencias de dependencias NPOI** (NU1902/NU1903): ya anotadas en `MEMORY.md`; la exportación no suma paquetes.
