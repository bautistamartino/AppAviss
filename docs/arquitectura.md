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
- **EstadoReproductivo** (catálogo de eventos, spec 005): `Codigo`, `Nombre`, `CodigoDairyComp`, `Tipo` (`Estado` o `SoloEvento`), `EsPrincipal`, `Orden` (solo vale entre principales) y `ClaveEstandar?` (identidad interna estable de los 9 estándar; renombrar o cambiar el código no la altera; índice único parcial). Principales de arranque: Revisar Nuevamente, OK, VACIA, PREÑADA, Aborto, NOINSEM; OK, Aborto y Revisar Nuevamente son `SoloEvento` con código provisorio. Valores vistos en la muestra: PREÑADA, INSEMIN, SECA, VACIA, FRESCA, NOINSEM. No es un enum fijo porque puede variar entre tambos.
- **Tacto**: `AnimalId`, `FechaHoraUtc`, `EstadoAnteriorId`, `EstadoNuevoId`, `DiasPrenez?`, `Observaciones`, `Anulado`, `TactoAnuladoId?`. Evento que solo se agrega: una corrección es un tacto nuevo.
- **Historial** (auditoría): `Entidad`, `EntidadId`, `Campo`, `ValorAnterior`, `ValorNuevo`, `FechaHoraUtc`, `Accion`. Un trigger de SQLite aborta cualquier UPDATE o DELETE.
- **Usuario** (veterinario): **se quitó en la spec 006**. Ninguna entidad guarda quién hizo el trabajo ni en qué tablet (migración `QuitarVeterinario`).
- **LecturaRfid**: log crudo (`Codigo`, `FechaHoraUtc`, `AnimalId?`, `Resultado`) para diagnóstico y recuperación.
- **Exportacion**: `RodeoId`, `FechaUtc`, `ArchivoNombre`, `Hash`.
- **Manga** (spec 003): `RodeoId`, `Numero` (único por rodeo). Nunca cambia. Se crea con la **primera marca** de la manga: la "manga por empezar" no existe en la base.
- **CierreManga** (spec 003): `MangaId`, `Tipo` (Manual / TerminoElTacto / Automatico), `FechaHoraUtc`, `CantidadAviss`, `CantidadConfirmada?`, `Diferencia?`, `Anulado`. Una manga está cerrada si tiene un cierre con `Anulado = 0` (un índice único parcial garantiza uno solo). Reabrir = anular el cierre; nunca se modifica ni se borra.
- **MarcaPasada** (spec 003): `AnimalId`, `MangaId`, `FechaHoraUtc`, `OrdenInsercion`, `Anulado`. Una marca vigente por vaca y manga (índice único parcial); un trigger impide marcar una vaca en la manga de otro rodeo. Comparte con `Tacto` el **orden de guardado** (`OrdenInsercion` = máximo entre ambas tablas + 1, dentro de la transacción).
- **Día local** (spec 003): "hoy" es el día calendario de la zona horaria del dispositivo (`DiaLocal`, convierte a un rango UTC; maneja días de 23 o 25 horas). No se guarda ningún "día": el avance y el contador se calculan por consulta. Una manga abierta no se cierra a medianoche: se cierra sola (tipo `Automatico`) al volver a entrar a trabajar un día posterior.
- **Espera** (spec 004): `AnimalId`, `FechaHoraUtc`, `Estado` (Pendiente / Resuelta / Salteada), `ResueltaEnUtc?`, `RegistroResolucionId?`. Una vaca en espera mientras se tacta a otra; **sin temporizador ni vencimiento**. Un índice único parcial impide dos pendientes de la misma vaca; un trigger solo deja pasar Pendiente → Resuelta o Salteada (una vez) y no deja borrar. Se resuelve en la misma transacción que el tacto o el rechazo que la cierra. Sobrevive a cerrar la app.
- **Rechazo** (spec 004): `AnimalId`, `FechaHoraUtc`, `OrdenInsercion`, `Observaciones` (el motivo, escrito a mano, tope 300), `Anulado`, `AnuladoEnUtc?`. Marca de "no se pasó por el tacto"; **no cambia el estado de la vaca ni su fecha de último tacto**. Solo se agrega: un trigger permite únicamente Anulado de 0 a 1 y no deja borrar. Comparte el orden de guardado con `Tacto` y `MarcaPasada`.
- **Historial** suma cinco acciones (poner en espera, resolver, saltear, registrar y anular rechazo); el CHECK de `Accion` se reescribió a mano en la migración `EsperaYRechazo` (recreando los triggers de inmutabilidad).
- **Eventos de la ficha** (spec 004): la lista que ve el veterinario es `Eventos` (los estados activos del catálogo más el evento fijo **Rechazo**, `EventosCampo.Rechazo`); no es lo mismo que `Estados` (solo estados reproductivos). Elegir Rechazo guarda un `Rechazo`; cualquier otro, un `Tacto`. **Desde la spec 005**, un evento `SoloEvento` del catálogo (OK, Aborto, Revisar Nuevamente) también se guarda como `Rechazo`, con `Rechazo.EventoId` apuntando al evento (nulo = el Rechazo fijo); no cambia el estado ni el último tacto. La tabla se sigue llamando `Rechazo` a propósito. Triggers: un `Rechazo` solo apunta a un `SoloEvento`, un `Tacto` Normal no apunta a un `SoloEvento` y `EventoId` no se modifica.

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

```
IRfidReader      IAsyncEnumerable<TagRead> Lecturas, EstadoConexion, ConectarAsync(), DesconectarAsync()
IRfidTransport   BluetoothClassicTransport (principal, SPP/RFCOMM) | BleTransport | HidKeyboardTransport | UsbSerialTransport
ITagParser       TruTestAsciiParser, Iso11784Parser
INormalizadorDeId  convierte el código leído al NumeroCaravana (regla configurable)
```

- `IRfidReader` y `TagRead` viven en `Aviss.Application` (los puertos los define `Application`); `Aviss.Rfid` los implementa. Hoy hay `FakeRfidReader` (simulador, solo en compilaciones `DEBUG`) y `LectorNoDisponible` (el que se registra en la versión que se distribuye hasta que exista el lector real).
- Hipótesis (confianza media, **sin validar**): el XRS2 usa Bluetooth clásico SPP y manda el ID como texto. No hay SDK .NET público conocido.
- `TruTestXrs2Reader` = transporte + parser. Cambiar de método es cambiar una línea de DI.
- Conexión robusta: servicio en primer plano, reconexión con backoff, debounce de lecturas repetidas.
- Fallback: búsqueda manual por número.
- Desarrollo sin hardware: `FakeRfidReader` que reproduce tramas grabadas, con el emulador de Android.
- El Excel trae el número de caravana (ID corto). El bastón puede entregar un número de 15 dígitos, así que `INormalizadorDeId` queda configurable hasta ver una lectura real.

## 7. Excel

Archivo de muestra (Dairy Comp, `.xls`): una hoja, 226 vacas, columnas `ID, CVET, FPART, LACT, DEL, FCELO, VINS, UTORO, COM, RPRO, FAPAR`, fechas como **texto con espacios** (`" 5/11/26"`) y una fila final `Total:N`. `VINS` es un **número** (cantidad de inseminaciones), no una fecha.

**Importación**
1. Se lee una copia del archivo; el original no se toca.
2. Un `IExcelProfile` (`PerfilDairyComp`) define columnas, detección del formato y reglas.
3. Validación en dos pasos: estructura (columnas) y datos (ID duplicados, fechas inválidas, estados desconocidos). Se ignora la fila `Total`.
4. Vista previa con errores por fila; el usuario confirma.
5. Se inserta todo en una transacción.
6. Se guarda el archivo original (blob y hash) en el `Rodeo`.

**Exportación**
- Se parte de la **copia del original** guardada en la base y se actualizan solo las celdas modificadas (`RPRO`, `COM`, `FAPAR` y las que resulten de la prueba). Se conservan estilos y estructura.
- Dairy Comp crea **eventos** al importar, así que el archivo de vuelta debe expresar el tacto como Dairy Comp lo espera. Las reglas exactas dependen de la prueba (sección 14).
- Alta en campo: se agrega la fila al final si el perfil lo permite.
- Se reabre y valida el archivo exportado con las reglas de importación, y se guarda con nombre nuevo.

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
- Barra de arriba siempre visible (engranaje, flechita para contraer, título); contraída o abierta, con avance, horarios, contador de manga (opcional) y "Terminar el tacto".
- **Mangas opcionales por establecimiento** (engranaje): cantidad a mano, contador N/M y cierre automático al completarse.
- Deshacer con aviso de 5 s al guardar un tacto o cerrar una manga; en "Últimos movimientos", **Deshacer** y **Editar** (editar = anular y registrar uno nuevo, en una transacción).
- Vibración y sonido (pitido de repetido, chicharra de CVET 0, tono de manga completa); pantalla siempre encendida durante el trabajo.
- No encontrado: aviso con el ID cargado y botones "Buscar a mano" y "Cerrar" (no hay alta en campo desde el 2026-11-19: los números se corrigen en el bastón).
- **Lista de eventos en dos niveles (spec 005):** el casillero Evento muestra los principales activos (botones de 60 dp, en su `Orden`) y "Otro evento". Este abre la lista secundaria: Rechazo primero y el resto alfabético (sin tildes ni mayúsculas, Ñ entre N y O), con "Volver" y "+ Nuevo evento" fijos. Elegir no guarda (lo hace **Entrar**) y resalta el evento actual. Es el mismo `SelectorEventosView` en la ficha y en Editar de movimientos; cualquier evento queda a 3 toques como máximo.
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
