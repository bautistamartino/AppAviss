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
- **Rodeo** (una importación): `EstablecimientoId`, `Nombre`, `FechaImportacionUtc`, `CantidadAnimales`, `ArchivoOrigenNombre`, `ArchivoOrigenHash` (SHA-256), `ArchivoOrigenBlob`, `PerfilExcel`, `Estado` (Abierto / Exportado).
- **Animal**: `RodeoId`, `NumeroCaravana` (= `ID` de Dairy Comp), `IdElectronico?`, `EstadoReproductivoId`, `FechaUltimoTacto?`, `Observaciones`, `Origen` (Importado / AltaEnCampo), `DatosOriginalesJson` (todas las columnas del Excel tal cual).
- **EstadoReproductivo**: catálogo con `Codigo`, `Nombre`, `CodigoDairyComp`. Valores vistos en la muestra: PREÑADA, INSEMIN, SECA, VACIA, FRESCA, NOINSEM. No es un enum fijo porque puede variar entre tambos.
- **Tacto**: `AnimalId`, `FechaHoraUtc`, `EstadoAnteriorId`, `EstadoNuevoId`, `DiasPrenez?`, `Observaciones`, `UsuarioId`, `DispositivoId`, `Anulado`, `TactoAnuladoId?`. Evento que solo se agrega: una corrección es un tacto nuevo.
- **Historial** (auditoría): `Entidad`, `EntidadId`, `Campo`, `ValorAnterior`, `ValorNuevo`, `FechaHoraUtc`, `UsuarioId`, `Accion`. Un trigger de SQLite aborta cualquier UPDATE o DELETE.
- **Usuario** (veterinario): `Nombre`, `Matricula`. Se elige al abrir la app.
- **LecturaRfid**: log crudo (`Codigo`, `FechaHoraUtc`, `AnimalId?`, `Resultado`) para diagnóstico y recuperación.
- **Exportacion**: `RodeoId`, `FechaUtc`, `ArchivoNombre`, `Hash`.

## 4. DTOs

- Importación: `FilaAnimalImportada`, `ResultadoImportacion` (leídas, importadas, rechazadas, `ErrorFila[]`), `VistaPreviaImportacion`.
- Campo: `FichaAnimalDto`, `RegistrarTactoCommand`, `ResultadoLecturaDto` (Encontrado / NoEncontrado / Duplicado).
- Listados: `RodeoResumenDto`, `AvanceRodeoDto` (tactadas vs. pendientes).
- Exportación: `ExportacionResultadoDto` (ruta y hash).

## 5. Servicios y repositorios

Servicios (Application): `IImportacionService`, `ITrabajoCampoService` (`ProcesarLecturaAsync`, `RegistrarTactoAsync`; es el corazón), `IAnimalService`, `IRodeoService`, `IHistorialService`, `IExportacionService`, `IBackupService`, `IRelojService`, `IUsuarioActualService`.

Repositorios: `IEstablecimientoRepository`, `IRodeoRepository`, `IAnimalRepository` (por caravana e ID electrónico), `ITactoRepository`, `IHistorialRepository`. Un `IUnitOfWork` abre una transacción por caso de uso; no hay repositorios genéricos.

## 6. Lector RFID (XRS2)

```
IRfidReader      IAsyncEnumerable<TagRead> Lecturas, EstadoConexion, ConectarAsync(), DesconectarAsync()
IRfidTransport   BluetoothClassicTransport (principal, SPP/RFCOMM) | BleTransport | HidKeyboardTransport | UsbSerialTransport
ITagParser       TruTestAsciiParser, Iso11784Parser
INormalizadorDeId  convierte el código leído al NumeroCaravana (regla configurable)
```

- Hipótesis (confianza media, **sin validar**): el XRS2 usa Bluetooth clásico SPP y manda el ID como texto. No hay SDK .NET público conocido.
- `TruTestXrs2Reader` = transporte + parser. Cambiar de método es cambiar una línea de DI.
- Conexión robusta: servicio en primer plano, reconexión con backoff, debounce de lecturas repetidas.
- Fallback: búsqueda manual por número.
- Desarrollo sin hardware: `FakeRfidReader` que reproduce tramas grabadas, con el emulador de Android.
- El Excel trae el número de caravana (ID corto). El bastón puede entregar un número de 15 dígitos, así que `INormalizadorDeId` queda configurable hasta ver una lectura real.

## 7. Excel

Archivo de muestra (Dairy Comp, `.xls`): una hoja, 226 vacas, columnas `ID, CVET, FPART, LACT, DEL, FCELO, VINS, UTORO, COM, RPRO, FAPAR`, fechas como **texto con espacios** (`" 5/11/26"`) y una fila final `Total:N`.

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

Lectura → ficha → un toque en el estado → guardado automático → vuelve a esperar. Meta: 1 toque por vaca.

- Botones grandes, alto contraste, legibles al sol, pensados para una mano.
- Deshacer con snackbar de 5 s (genera un tacto de anulación).
- Vibración y sonido al leer; pantalla siempre encendida durante el trabajo.
- No encontrado: botón "Dar de alta en campo" con el ID cargado.

## 11. Multi-tablet y multi-veterinario

Cada tablet es independiente: varios establecimientos, varios veterinarios (se elige al abrir). Sin sincronización central en la primera versión; cada tablet exporta su Excel. Los IDs son GUID y los eventos solo se agregan, así que sumar sincronización después no requiere rediseñar.

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
4. Exportación con validación (según sección 14).
5. Integración real del XRS2.
6. Pulido de UX, backups, empaquetado y distribución.

## 14. Pendientes de validar

- **Reimportación en Dairy Comp** (prueba manual con una copia, sin cambios y luego con una vaca pasada a PREÑADA): ¿mapea columnas o usa formato fijo?, ¿acepta `.xls` y `.xlsx`?, ¿cómo trata la fila `Total` y las fechas de texto?, ¿qué columnas debe cambiar un tacto para que genere el evento correcto?
- **Bastón XRS2**: transporte real, formato de trama, relación entre el número leído y el `ID` de caravana. Sin hardware ni tablet por ahora.
- **Distribución**: cómo se instalan y actualizan las apps en las tablets.
