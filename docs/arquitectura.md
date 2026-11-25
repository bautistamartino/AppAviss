# Aviss — Arquitectura

App Android (tablets) para veterinarios que hacen tacto reproductivo en tambos. Funciona 100 % offline, importa un Excel de Dairy Comp, trabaja sobre SQLite con un bastón Tru-Test XRS2 y exporta un Excel para volver a Dairy Comp.

Estado: **borrador v1, pendiente de validar** los puntos de la sección 14.

## 1. Decisiones clave

| Tema | Decisión | Motivo |
|---|---|---|
| Plataforma | .NET MAUI, solo Android (.NET 10) | Se conoce C#. El valor está en dominio, SQLite y Excel, que son C# puro. |
| Alternativa | Kotlin nativo | Solo si el spike del bastón falla en MAUI. |
| Backend ASP.NET local | No | App offline de un solo usuario por tablet. Un servidor local agrega fallos sin aportar nada. |
| Base de datos | SQLite + EF Core | Migraciones, transacciones, LINQ. Volumen chico (miles de vacas). |
| Excel | **NPOI** (lectura y escritura de `.xls`; `.xlsx` también) | El archivo de muestra es `.xls`. ClosedXML solo lee `.xlsx`, no sirve. |
| UI | MVVM con CommunityToolkit.Mvvm | Estándar en MAUI, testeable. |
| Arquitectura | Clean Architecture liviana | Las dependencias apuntan hacia el dominio. |

## 2. Solución y proyectos

```
Aviss.sln
src/
  Aviss.Domain/            entidades, enums, reglas. Sin dependencias.
  Aviss.Application/       casos de uso, DTOs, interfaces (puertos)
  Aviss.Infrastructure/    EF Core/SQLite, repositorios, backups
  Aviss.Excel/             importar/exportar (NPOI) + perfiles de formato
  Aviss.Rfid/              abstracción del lector + parsers (sin Android)
  Aviss.App/               MAUI: UI, ViewModels, DI
    Platforms/Android/     transportes Bluetooth/USB/HID
tests/
  Aviss.Domain.Tests/  Aviss.Application.Tests/  Aviss.Excel.Tests/
  Aviss.Rfid.Tests/    Aviss.Infrastructure.Tests/
docs/
```

`Aviss.Excel` y `Aviss.Rfid` son proyectos aparte porque son los dos puntos de variación futuros (otro sistema, otro lector).

## 3. Entidades

Todas con `Guid Id`, `CreadoEnUtc`, `ActualizadoEnUtc`.

- **Establecimiento** (tambo): `Nombre`, `Notas`. Cada tablet guarda varios.
- **Rodeo** (una importación): `EstablecimientoId`, `Nombre`, `FechaImportacionUtc`, `ArchivoOrigenNombre`, `ArchivoOrigenHash` (SHA-256), `ArchivoOrigenBlob`, `PerfilExcel`, `Estado` (Abierto / Exportado).
- **Animal**: `RodeoId`, `NumeroCaravana` (= `ID` de Dairy Comp), `IdElectronico?`, `EstadoReproductivoId`, `FechaUltimoTacto?`, `Observaciones`, `Origen` (Importado / AltaEnCampo), `DatosOriginalesJson` (todas las columnas del Excel tal cual).
- **EstadoReproductivo** (catálogo de eventos, spec 005): `Codigo`, `Nombre`, `CodigoDairyComp`, `Tipo` (`Estado` o `SoloEvento`), `EsPrincipal`, `Orden` (solo vale entre principales) y `ClaveEstandar?` (identidad interna estable de los 10 estándar; renombrar o cambiar el código no la altera; índice único parcial). `Pasos` (qué pide la pantalla después de elegir: días de preñez, días de aborto y resultado) y `EstadoDestinoId?` (estado al que lleva el evento: Preñada fecha previa apunta a PREÑADA y comparte su estado). Principales de arranque: Revisar Nuevamente, OK, VACIA, Preñada fecha correcta (clave `PRENADA`), Preñada fecha previa, Aborto, NOINSEM; OK y Revisar Nuevamente son `SoloEvento` y Aborto es un estado con pasos; los códigos de OK, Aborto, Revisar Nuevamente y Preñada fecha previa son provisorios. Valores vistos en la muestra: PREÑADA, INSEMIN, SECA, VACIA, FRESCA, NOINSEM. No es un enum fijo porque puede variar entre tambos.
- **Tacto**: `AnimalId`, `FechaHoraUtc`, `EstadoAnteriorId`, `EstadoNuevoId`, `DiasPrenez?`, `EventoId?` (evento elegido, spec 005), `DiasPreniezInformados?` (1 a 400, Preñada fecha previa), `DiasAborto?` (0 a 400, Aborto con estado), `Observaciones`, `Evento2Id?` y `Observaciones2?` (segundo evento del mismo tacto, spec 011: nunca cambia el estado ni lleva pasos; `Observaciones2` solo con `Evento2Id`), `Anulado`, `TactoAnuladoId?`. Evento que solo se agrega: una corrección es un tacto nuevo.
- **Historial** (auditoría): `Entidad`, `EntidadId`, `Campo`, `ValorAnterior`, `ValorNuevo`, `FechaHoraUtc`, `Accion`. Un trigger de SQLite aborta cualquier UPDATE o DELETE.
- **Usuario** (veterinario): **se quitó en la spec 006**. Ninguna entidad guarda quién hizo el trabajo ni en qué tablet (migración `QuitarVeterinario`).
- **LecturaRfid**: log crudo (`Codigo`, `FechaHoraUtc`, `AnimalId?`, `Resultado`) para diagnóstico y recuperación.
- **Exportacion** (spec 007): constancia de cada archivo de vuelta entregado. `RodeoId`, `FechaUtc` (UTC exacta), `ArchivoNombre`, `Formato` (`Csv` / `Xls`), `Hash` (SHA-256 del archivo, 64 caracteres), `CantidadFilas`, `CantidadVacasConEvento`, `CreadoEnUtc`. Solo se agrega: triggers `TR_Exportacion_NoModificar` y `TR_Exportacion_NoBorrar` y CHECK de formato, hash y cantidades; índice `(RodeoId, FechaUtc)` para "la última exportación". Sin veterinario ni tablet. Migración `Exportaciones` (después de `EventosConPasos`).
- **Manga** (spec 003): `RodeoId`, `Numero` (único por rodeo). Nunca cambia. Se crea con la **primera marca** de la manga: la "manga por empezar" no existe en la base.
- **CierreManga** (spec 003): `MangaId`, `Tipo` (Manual / TerminoElTacto / Automatico), `FechaHoraUtc`, `CantidadAviss`, `CantidadConfirmada?`, `Diferencia?`, `Anulado`. Una manga está cerrada si tiene un cierre con `Anulado = 0` (un índice único parcial garantiza uno solo). Reabrir = anular el cierre; nunca se modifica ni se borra.
- **MarcaPasada** (spec 003): `AnimalId`, `MangaId`, `FechaHoraUtc`, `OrdenInsercion`, `Anulado`. Una marca vigente por vaca y manga (índice único parcial); un trigger impide marcar una vaca en la manga de otro rodeo. Comparte con `Tacto` el **orden de guardado** (`OrdenInsercion` = máximo entre ambas tablas + 1, dentro de la transacción).
- **Día local** (spec 003): "hoy" es el día calendario de la zona horaria del dispositivo (`DiaLocal`, convierte a un rango UTC; maneja días de 23 o 25 horas). No se guarda ningún "día": el avance y el contador se calculan por consulta. Una manga abierta no se cierra a medianoche: se cierra sola (tipo `Automatico`) al volver a entrar a trabajar un día posterior.
- **Espera** (spec 004): `AnimalId`, `FechaHoraUtc`, `Estado` (Pendiente / Resuelta / Salteada), `ResueltaEnUtc?`, `RegistroResolucionId?`. Una vaca en espera mientras se tacta a otra; **sin temporizador ni vencimiento**. Un índice único parcial impide dos pendientes de la misma vaca; un trigger solo deja pasar Pendiente → Resuelta o Salteada (una vez) y no deja borrar. Se resuelve en la misma transacción que el tacto o el rechazo que la cierra. Sobrevive a cerrar la app.
- **Rechazo** (spec 004): `AnimalId`, `FechaHoraUtc`, `OrdenInsercion`, `EventoId?` (spec 005: evento solo registro; nulo = Rechazo fijo), `DiasAborto?` (Aborto con resultado Rechazo), `Observaciones` (el motivo, escrito a mano, tope 300), `Evento2Id?` y `Observaciones2?` (segundo evento, spec 011; solo con `EventoId`, nunca con el Rechazo fijo ni con Aborto), `Anulado`, `AnuladoEnUtc?`. Marca de "no se pasó por el tacto"; **no cambia el estado de la vaca ni su fecha de último tacto**. Solo se agrega: un trigger permite únicamente Anulado de 0 a 1 y no deja borrar. Comparte el orden de guardado con `Tacto` y `MarcaPasada`.
- **Historial** suma cinco acciones (poner en espera, resolver, saltear, registrar y anular rechazo); el CHECK de `Accion` se reescribió a mano en la migración `EsperaYRechazo` (recreando los triggers de inmutabilidad).
- **Eventos de la ficha** (spec 004): la lista que ve el veterinario es `Eventos` (los estados activos del catálogo más el evento fijo **Rechazo**, `EventosCampo.Rechazo`); no es lo mismo que `Estados` (solo estados reproductivos). Elegir Rechazo guarda un `Rechazo`; cualquier otro, un `Tacto`. **Desde la spec 005**, un evento `SoloEvento` del catálogo (OK, Revisar Nuevamente) también se guarda como `Rechazo`; Aborto es un `Estado` con pasos y solo se guarda como `Rechazo` si su resultado fue Rechazo (con `DiasAborto`), con `Rechazo.EventoId` apuntando al evento (nulo = el Rechazo fijo); no cambia el estado ni el último tacto. La tabla se sigue llamando `Rechazo` a propósito. Triggers: un `Rechazo` apunta a un `SoloEvento` o a Aborto (con días), un `Tacto` Normal no apunta a un `SoloEvento` y `EventoId` no se modifica.

## 4. DTOs

- Importación: `FilaAnimalImportada`, `ResultadoImportacion` (leídas, importadas, excluidas, `ErrorFila[]`), `VistaPreviaImportacion`.
- Campo: `FichaAnimalDto`, `RegistrarTactoCommand`, `ResultadoLecturaDto` (Encontrado / NoEncontrado / Duplicado).
- Listados: `RodeoResumenDto`, `AvanceRodeoDto` (tactadas vs. pendientes).
- Exportación: `ExportacionResultadoDto` (ruta y hash).

## 5. Servicios y repositorios

Servicios (Application): `IImportacionService`, `ITrabajoCampoService` (`ProcesarLecturaAsync`, `RegistrarTactoAsync`; es el corazón), `IAnimalService`, `IRodeoService`, `IHistorialService`, `IExportacionService`, `IBackupService`, `IRelojService` (sin servicio de usuario ni de dispositivo desde la spec 006).

**Fachada de campo (spec 003):** los ViewModels solo conocen `ICampoService` (en `Application`). Es un singleton que abre un scope (un `DbContext` y una transacción) por operación y delega en servicios internos (`MarcaPasadaService`, `MangaService`, `AltaCampoService`, `ConsultaCampoService`, `TrabajoCampoService`); las escrituras se serializan porque SQLite admite un solo escritor. Cubre iniciar trabajo, procesar lecturas (marca automática), ficha, tacto, alta con marca, deshacer, cierre y reapertura de mangas, movimientos y avance.

**Espera y rechazo (spec 004):** `ICampoService` suma `PonerEnEsperaAsync`, `SaltearEsperaAsync`, `ListarEsperasAsync` y `RegistrarRechazoAsync`; `CorregirRegistroAsync` ("Editar" un movimiento) cambia un tacto por un rechazo y al revés, y anula el original, en una sola transacción. `DeshacerAsync` también deshace rechazos. Se quitó `CorregirTactoAsync`.

Repositorios: `IEstablecimientoRepository`, `IRodeoRepository`, `IAnimalRepository` (por caravana e ID electrónico), `ITactoRepository`, `IHistorialRepository`. Un `IUnitOfWork` abre una transacción por caso de uso; no hay repositorios genéricos.

## 6. Lector RFID (XRS2)

Lo que existe hoy (spec 008, implementada; probado con tests y falta la tablet):

```
Aviss.Application (puertos)
  IRfidReader         Lecturas(ct), Estado (Desconectado | Conectando | Conectado), Motivo (MotivoSinBaston),
                      EstadoCambiado, ConectarAsync(), DesconectarAsync(), ReconectarAhoraAsync()
  IPreferenciasBaston Obtener() / Guardar(BastonRecordado)      IAvisosCampo.BastonPerdido() (vibración corta)
Aviss.Rfid (sin Android)
  LectorBastonXrs2    el lector: supervisor en segundo plano + canal de lecturas + Inyectar() para el simulador de DEBUG
  AcumuladorLineas -> InterpreteLineaBaston -> FiltroRepetidas -> canal   PoliticaReintentos   OpcionesLectorBaston
  IEnlaceSerieBaston / IConexionSerie / IBluetoothBaston   (puertos de plataforma)
Aviss.App (Platforms/Android)
  EnlaceSerieBastonAndroid (socket SPP)   BluetoothBastonAndroid (permiso, emparejados, estado, ajustes)
```

- **Transporte confirmado en la prueba del 2026-11-24:** Bluetooth clásico SPP. La lectura de un botón llega como texto ASCII `982 000458467499` más CRLF (15 dígitos, una línea por lectura). Es el EID que la importación guarda (spec 010). Sin SDK ni BLE ni teclado.
- **Estados** (los ve el veterinario en el chip de la barra y en el cartel de la pantalla de trabajo): *Buscando el bastón* (ámbar, solo en el primer ciclo de intentos), *Conectado* (verde) y *Sin bastón* (rojo) con su motivo: no elegido, sin permiso, Bluetooth apagado, sin Bluetooth, no emparejado, no responde. Mientras sigue sin conectar el estado no cambia (no parpadea).
- **Estrategia de conexión:** `ConectarAsync` arranca el supervisor y vuelve enseguida. Sin bastón recordado no intenta nada (no elige solo). Un ciclo = socket **inseguro** y después el **seguro** (el que anduvo se prueba primero la vez siguiente), cada intento con **8 s** de límite que se corta cerrando el socket; entre ciclos espera 1, 2, 4, 8, 15 y 15 s (se reinicia al conectar) y "Tocá para reconectar" la salta. Si la conexión se pierde: una vibración corta, `Conectando` y vuelta a empezar con 1 s. Apagado y sin permiso se re-chequean en cada vuelta, así que prender el Bluetooth o conceder el permiso reconecta solo. Valores provisorios en `OpcionesLectorBaston` (3 s, 8 s y las esperas).
- **Lectura:** un `AcumuladorLineas` nuevo por conexión arma líneas (CR, LF o CRLF, partidas en paquetes, con tope de bytes), el intérprete exige 15 dígitos (con o sin espacio; todo lo demás es basura que se ignora en silencio), el filtro ignora el mismo EID a menos de 3 s de la última aceptada y el código llega por el mismo camino que la búsqueda a mano (`EncolarLecturaAsync`): la ficha, la marca, el CVET 0 y el aviso de la spec 009 se comportan igual. Nada se guarda por perder el bastón: la ficha abierta, el evento y la observación quedan intactos.
- **Ciclo de vida:** el lector corta al salir de la pantalla de trabajo y cuando la app pasa a segundo plano o se apaga la pantalla (`Window.Stopped`); vuelve a conectar de inmediato al volver (`Window.Resumed`). No hay servicio en primer plano ni lectura con la pantalla apagada.
- **Bastón elegido:** se recuerda por tablet en las preferencias (no en la base), con la pantalla "Elegir el bastón" (emparejados, los XRS primero). El permiso de Bluetooth (`BLUETOOTH_CONNECT`; antes de Android 12, `BLUETOOTH`) se pide ahí o al entrar a trabajar con un bastón elegido, no al abrir la app. No se pide ubicación ni `BLUETOOTH_SCAN`; el manifiesto no tiene `INTERNET`.
- **Versiones:** Debug y Release registran el lector real; el simulador (`FakeRfidReader`) solo se usa en tests y, en DEBUG, el panel del simulador entra por `LectorBastonXrs2.Inyectar` (mismo filtro y misma cola). La pantalla "Diagnóstico del bastón" (spike) sigue solo en DEBUG. `LectorNoDisponible` queda para una tablet sin Bluetooth.
- **No resuelto, a ver en la tablet:** si el XRS2 guarda y reenvía lo leído con el bastón desconectado; si hay lecturas en ráfaga; cuánto tarda en aceptar una conexión tras prenderse. El socket inseguro es una decisión consciente (el bastón ya está emparejado y solo manda números de caravana).
- El Excel trae el número de caravana (ID corto) y el bastón entrega el EID de 15 dígitos; la relación viene de la columna `EID` del archivo (spec 010): `MarcaPasadaService.BuscarAsync` busca primero el número corto y después el EID (con o sin espacios). `INormalizadorDeId` ya no hace falta: no se creó.
- Desarrollo sin hardware: `FakeRfidReader` y `FakeEnlaceSerie` con tramas inventadas (prefijo `999`, nunca los EID reales).

## 7. Excel

Archivo de muestra (Dairy Comp, `.xls`): una hoja, 226 vacas, columnas `ID, CVET, FPART, LACT, DEL, FCELO, VINS, UTORO, COM, RPRO, FAPAR`, fechas como **texto con espacios** (`" 5/11/26"`) y una fila final `Total:N`. `VINS` es un **número** (cantidad de inseminaciones), no una fecha.

**Importación**
1. Se lee una copia del archivo; el original no se toca.
2. Un `IExcelProfile` (`PerfilDairyComp`) define columnas, detección del formato y reglas.
3. Validación en dos pasos: estructura (columnas) y datos (ID duplicados, fechas inválidas, estados desconocidos). Se ignora la fila `Total`.
4. Vista previa con errores por fila (y, si el archivo trae `EID`, "Con EID: N · Sin EID: M"); el usuario confirma.
5. Se inserta todo en una transacción.
6. Se guarda el archivo original (blob y hash) en el `Rodeo`.

**Columna `EID` (spec 010):** `PerfilDairyComp` la reconoce por su encabezado (en cualquier posición, sin distinguir mayúsculas) y `ReglasEidImportacion` evalúa cada celda: 15 dígitos (con o sin espacios, o número entero) = válido; vacía, espacios o `-` = sin EID, sin advertencia; cualquier otra cosa = inválido. `ValidadorImportacion` descarta los inválidos y los repetidos entre las filas no excluidas por `ID` (advertencias `EidInvalido` y `EidRepetido` sin el valor) y cuenta `ConEid`/`SinEid`. El animal siempre entra; `EID` no es columna opcional con advertencia, y la ficha lo muestra en una sola fila (`ConstructorFicha.ColumnasPropias`). Un rodeo viejo no se actualiza: el archivo con EID se importa como rodeo nuevo. `SCDAT` y `SCTIM` del archivo real se ignoran al importar y la exportación las reemplaza (ver más abajo).

**Exportación** (spec 007)
- **Se agrega, no se reemplaza.** Se parte de la **copia del original** guardada en el rodeo (nunca del archivo que eligió el veterinario) y se genera un archivo **nuevo** con todas las filas y columnas del original sin tocar (`RPRO` incluida) y **columnas nuevas a la derecha**: `SCDAT` (`dd/MM/yy`), `SCTIM` (`HH:mm`), con el mismo formato que trae Dairy Comp (2026-11-25), `Evento1` (nombre del evento), `Evento2` (nombre del segundo evento, spec 011; siempre presente, justo a la derecha de `Evento1`, vacía si no hay), `DIASPRE` (días de preñez informados), `DIASABO` y `RESABO` (días y resultado del aborto). Los nombres viven en `ColumnasExportacion` (Application) y son **provisorios** salvo `SCDAT` y `SCTIM`. Fecha y hora salen en la hora local de la tablet. Si el original ya trae una columna con ese nombre se rechaza, **salvo `SCDAT` y `SCTIM`** (regla del 2026-11-24): Dairy Comp siempre las exporta, así que si el original las trae se **reemplaza** su valor solo en las vacas con tacto (el resto de las filas queda igual) y si faltan se crean a la derecha y la pantalla de resultado avisa (`PlanColumnasExportacion`).
- **Qué se exporta:** solo la **jornada** del rodeo (el día local del registro vigente más reciente); de cada vaca, su último registro vigente de esa jornada (los anulados no cuentan). Vacas sin registro (también las de CVET 0 sin registro; las de CVET 0 tactadas a propósito salen como cualquier otra), filas excluidas al importar y la fila `Total` salen con las columnas nuevas vacías.
- **Formatos (a elección al final):** CSV a CSV trabaja a nivel de texto y conserva byte por byte cada línea original (codificación, comillas, relleno, `;` final); .xls agrega sobre la hoja abierta y escribe fecha y hora como texto; los cuatro cruces (CSV o .xls hacia CSV o .xls) están cubiertos. .xls a CSV usa `ConvencionCsvDairyComp` (`;`, Windows-1252, encabezados entre comillas).
- **Orden de `ExportacionService.ExportarAsync`:** leer rodeo y original → armar el diccionario de valores por caravana (tres consultas por lote en una sola transacción de lectura) → generar → **reabrir y comparar** con el original (`ValidadorExportacion`, con las reglas de importación) → guardar en Descargas → **recién entonces** dejar la constancia (`Exportacion`) y marcar el rodeo como exportado. Si algo falla antes, no queda nada; si falla solo la constancia, el archivo ya está guardado y se avisa.
- **Entrega (puerto `IEntregaArchivo`, implementación en `Platforms/Android/EntregaArchivoAndroid`):** `MediaStore.Downloads` con `IS_PENDING = 1` mientras se escribe (el archivo no se ve a medias), nombre que no pisa otro, y "Compartir" con un `Intent` de enviar sobre el `content://` (el `Share` de MAUI solo recibe rutas de archivo). Sin permisos especiales (Android 10 o más). Sin red.
- **Pantalla:** `ExportarPage` + `ExportarViewModel`, una sola con cuatro pasos (lista de rodeos por establecimiento, resumen con "N de M con evento", generando con "Cancelar", resultado con "Compartir" y "Listo"). Se llega desde el botón "Exportar" del inicio.
- Alta en campo: no se agregan filas (el archivo de vuelta mantiene las del original).
- **Pendiente (prueba 0.2):** que Dairy Comp acepte las columnas nuevas y cómo se llaman de verdad.

## 8. SQLite

- Archivo en almacenamiento privado de la app; `foreign_keys=ON`, `journal_mode=WAL`, `synchronous=FULL`.
- Una transacción por tacto: actualizar `Animal` + insertar `Tacto` + insertar `Historial`.
- Índice único `(RodeoId, NumeroCaravana)`; índice por `IdElectronico`.
- Migraciones de EF Core al iniciar, con backup previo. Fechas en UTC.

## 9. Integridad de datos

- Autoguardado: cada acción se confirma en SQLite al instante.
- Historial y tactos inmutables (triggers). Nada se borra: baja lógica o anulación.
- SHA-256 del original y del exportado.
- Backups rotativos de la base (antes de importar, cada N tactos, al exportar).
- Log de lecturas + recuperación al abrir tras un cierre inesperado.
- Tests de integración que cortan una transacción a la mitad.

## 10. Flujo de trabajo y UX

Lectura → marca automática → ficha → elegir el evento (y, si se quiere, una observación) → **Entrar** guarda → vuelve a esperar. La tablet se usa **en vertical**.

- Botones grandes, alto contraste, legibles al sol, pensados para una mano.
- **CVET** de Dairy Comp: 0 = no entra en tacto (chicharra, queda marcada y suma, sin ficha); otro valor = motivo del tacto, se muestra en el título.
- **Lectura con ficha abierta (spec 009):** con una ficha abierta, una lectura de otra vaca que entra en tacto abre el aviso de tres opciones (sin marcar a la nueva hasta elegir); CVET 0, repetidas, en espera sin ficha abierta y no encontradas siguen como antes. La ficha de CVET 0 es completa. Atrás de Android = Cancelar lectura.
- Barra de arriba siempre visible (engranaje, flechita para contraer, título); contraída o abierta, con avance, horarios, contador de manga (opcional) y "Terminar el tacto".
- **Mangas opcionales por establecimiento** (engranaje): cantidad a mano, contador N/M y cierre automático al completarse.
- Deshacer con aviso de 5 s al guardar un tacto o cerrar una manga; en "Últimos movimientos", **Deshacer** y **Editar** (editar = anular y registrar uno nuevo, en una transacción).
- Vibración y sonido (pitido de repetido, chicharra de CVET 0, tono de manga completa); pantalla siempre encendida durante el trabajo.
- No encontrado: aviso con el ID cargado y botones "Buscar a mano" y "Cerrar" (no hay alta en campo desde el 2026-11-19: los números se corrigen en el bastón).
- **Lista de eventos en dos niveles (spec 005):** el casillero Evento muestra los principales activos (botones de 60 dp, en su `Orden`) y "Otro evento". Este abre la lista secundaria: Rechazo primero y el resto alfabético (sin tildes ni mayúsculas, Ñ entre N y O), con "Volver" y "+ Agregar evento" fijos. **Pasos posteriores (segunda vuelta):** Preñada fecha previa abre un campo de días de preñez; Aborto pide días de aborto y luego el resultado (Vacía OK, Inseminada, Seca, Fresca o Rechazo; derivado, sin columna propia); el casillero muestra el resumen y **Entrar** guarda, atómico, con fecha y hora exactas. Los triggers de la migración `EventosConPasos` validan rangos y la coherencia de evento, destino y resultado. Elegir no guarda (lo hace **Entrar**) y resalta el evento actual. Es el mismo `SelectorEventosView` en la ficha y en Editar de movimientos; cualquier evento queda a 3 toques como máximo, salvo Preñada fecha previa y Aborto, que suman pasos (días y resultado).
- **Dos eventos por tacto (spec 011):** con un Evento 1 que admite un segundo (`ReglasSegundoEvento.AdmiteSegundo`, banderas `AdmiteSegundo`/`PuedeSerSegundo` del `EventoCampoDto`) aparece "+ evento" (texto chico, en el renglón de los rótulos) y abre la fila 2 (Evento 2 y su observación, "Quitar"). Es **un solo registro** (`Tacto` o `Rechazo` con `Evento2Id` y `Observaciones2`): Deshacer, Editar y la exportación no cambian de forma. El Evento 2 nunca cambia el estado ni lleva pasos; solo OK, Revisar Nuevamente y los creados a mano pueden serlo. La lista del Evento 2 es el mismo selector en "modo segundo" y las capas de eventos y de observación se reutilizan con `FilaActiva`. Historial: una línea más con `Campo = "Evento2"`. Los triggers `TR_Tacto_NoModificar`, `TR_Rechazo_NoModificar`, `TR_Tacto_EventoValido` y `TR_Rechazo_SoloEvento` se recrearon en la migración `DosEventosPorTacto` (después de `Exportaciones`; `ADD COLUMN`, sin reconstruir tablas). El tope de 300 caracteres de las observaciones lo hacen valer la pantalla y el servicio, no la base.
- **Teclado y márgenes (Android):** el teclado se baja al buscar y `MainActivity` aplica los márgenes del sistema (barras y teclado), porque desde Android 15 la app se dibuja bajo las barras.
- **ViewModels grandes:** `TrabajoViewModel` está partido en archivos `partial` por tema (núcleo, `.Ficha`, `.Mangas`, `.Avisos`).

## 11. Multi-tablet, sin veterinario

Cada tablet es independiente: varios establecimientos. **La app no registra qué veterinario trabaja ni identifica a cada tablet** (spec 006): al abrir se elige solo establecimiento y rodeo, y ningún dato guarda quién lo hizo ni se exporta. Por eso no hay tabla `Usuario` ni `DispositivoId`; el cierre automático de una manga se distingue por su tipo, no por quién. Sin sincronización central en la primera versión; cada tablet exporta su Excel. Los IDs son GUID y los eventos solo se agregan, así que sumar sincronización después no requiere rediseñar.

## 12. Escalabilidad

- Otros sistemas: nuevo `IExcelProfile`.
- Otros lectores: nuevo transporte o parser.
- Otros eventos (pesaje, sanidad): `Tacto` pasa a ser un tipo de `EventoCampo`.
- Nube: sincronización encima del modelo actual.

## 13. Roadmap

0. Prueba manual de reimportación en Dairy Comp + spike del bastón cuando haya hardware.
1. Dominio, SQLite y migraciones, con tests.
2. Importación del `.xls` con vista previa.
3. Flujo de campo con `FakeRfidReader` en emulador.
4. Exportación con validación (según sección 14) (spec 007; la 004 es En Espera y Rechazo, la 005 eventos del catálogo y la 006 saca el veterinario).
5. Integración real del XRS2 (spec 008).
6. Pulido de UX, backups, empaquetado y distribución.

## 14. Pendientes de validar

- **Reimportación en Dairy Comp** (prueba manual con una copia, sin cambios y luego con una vaca pasada a PREÑADA): ¿mapea columnas o usa formato fijo?, ¿acepta `.xls` y `.xlsx`?, ¿cómo trata la fila `Total` y las fechas de texto?, ¿qué columnas debe cambiar un tacto para que genere el evento correcto?
- **Bastón XRS2**: transporte real, formato de trama, relación entre el número leído y el `ID` de caravana. Sin hardware ni tablet por ahora.
- **Distribución**: cómo se instalan y actualizan las apps en las tablets.
