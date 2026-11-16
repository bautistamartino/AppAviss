# Plan — 001 Dominio y base local

Spec: [spec.md](spec.md)

## Resumen
Las reglas puras (validación de caravana y de tactos, qué es "último vigente") viven en `Domain`; los casos de uso en `Application` hablan con puertos (repositorios, unidad de trabajo, reloj, backups); `Infrastructure` implementa todo sobre SQLite + EF Core. Las garantías críticas (unicidad, claves foráneas, inmutabilidad, no borrar) se ponen **también en la base** con índices únicos, CHECK, claves compuestas y triggers escritos a mano dentro de la migración inicial. El arranque (crear / actualizar con backup / restaurar) es un servicio de `Infrastructure` que deja un `ResultadoArranque`. No hay pantallas.

## Base de datos

Convenciones: toda tabla con `Id` GUID (TEXT) y `CreadoEnUtc`; las editables con `ActualizadoEnUtc`. Fechas: `DateTime` con `Kind=Utc` guardado como texto de ancho fijo `yyyy-MM-ddTHH:mm:ss.fffffffZ` mediante un `ValueConverter` (escribir una fecha no UTC lanza excepción; leer devuelve siempre `Kind=Utc`). Enums guardados como texto con CHECK. Toda FK con `DeleteBehavior.Restrict`.

| Tabla | Columnas (además de Id/Creado/Actualizado) | Reglas en la base |
|---|---|---|
| Establecimiento | Nombre, Notas?, Activo | CHECK `length(trim(Nombre))>0` |
| Rodeo | EstablecimientoId, Nombre, FechaImportacionUtc, ArchivoOrigenNombre, ArchivoOrigenHash, ArchivoOrigenBlob, PerfilExcel, Estado (Abierto/Exportado), Activo | todo NOT NULL; CHECK hash de 64 caracteres, blob no vacío, Estado válido; FK a Establecimiento; **sin** índice único en hash (RF-2) |
| Usuario | Nombre, Matricula?, Activo | CHECK Nombre no vacío, `length(Matricula)<=30`; trigger "Sin asignar" |
| EstadoReproductivo | Codigo, Nombre, NombreClave (generada), CodigoDairyComp, Activo | único Codigo; CHECK Codigo `NOT GLOB '*[^A-Z0-9_]*'` y no vacío; único NombreClave; único CodigoDairyComp **donde Activo=1** (índice parcial) |
| Animal | RodeoId, NumeroCaravana, NumeroCaravanaClave (generada), IdElectronico?, EstadoReproductivoId?, FechaUltimoTactoUtc?, Observaciones?, Origen (Importado/AltaEnCampo), Anulado, AnuladoEnUtc?, DatosOriginalesJson | ver abajo |
| Tacto | AnimalId, FechaHoraUtc, OrdenInsercion, EstadoAnteriorId?, EstadoNuevoId, DiasPrenez?, Observaciones?, UsuarioId, DispositivoId, Tipo (Normal/Anulacion), TactoAnuladoId?, Anulado | ver abajo |
| Historial | Entidad, EntidadId, Campo, ValorAnterior?, ValorNuevo?, FechaHoraUtc, UsuarioId, Accion | triggers de solo-agregar; FK a Usuario; `EntidadId` sin FK (apunta a varias tablas) |

**Animal**
- `NumeroCaravanaClave` = columna generada **almacenada** con `upper(replace(replace(...(NumeroCaravana,'ñ','Ñ'),'á','Á')...))` para ñ á é í ó ú ü. Se hace así porque `upper()` y `COLLATE NOCASE` de SQLite solo entienden ASCII y la Ñ no se igualaría (RF-4). La misma regla existe en C# (`Caravana.Clave`) como función propia (mayúsculas ASCII + esos 7 reemplazos, **no** `ToUpperInvariant`), y un test comprueba que ambas dan lo mismo.
- Índice único parcial `(RodeoId, NumeroCaravanaClave)` donde `Anulado = 0`. Es también el índice de búsqueda por caravana, por eso las consultas siempre filtran `Anulado = 0` (para que SQLite use el índice parcial).
- Índice único parcial `(RodeoId, IdElectronico)` donde `IdElectronico IS NOT NULL AND Anulado = 0`.
- CHECK: `length(NumeroCaravana) BETWEEN 1 AND 20`, `NumeroCaravana = trim(NumeroCaravana)`; `IdElectronico` sin espacios y de hasta 32; `Origen` válido; `Anulado IN (0,1)`; `Anulado = 1` solo si `Origen = 'AltaEnCampo'` (un importado no se anula).
- Triggers BEFORE UPDATE: abortan cambios en `Origen`, `DatosOriginalesJson`, `RodeoId`; abortan si `Origen='Importado'` y cambia `NumeroCaravana`; abortan pasar `Anulado` de 1 a 0.
- FK a Rodeo y a EstadoReproductivo (opcional).

**Tacto**
- `OrdenInsercion` INTEGER con índice único; lo asigna el caso de uso (`max+1` dentro de la transacción). No se usa `rowid` porque `VACUUM` puede renumerarlo en tablas con clave de texto.
- Clave alternativa única `(AnimalId, Id)` y FK compuesta `(AnimalId, TactoAnuladoId) → Tacto(AnimalId, Id)`: un tacto de anulación solo puede referenciar un tacto del **mismo** animal (RF-16). Con `TactoAnuladoId` nulo la FK no se evalúa.
- Índice único parcial en `TactoAnuladoId` (donde no es nulo): un tacto se anula una sola vez.
- CHECK: `DiasPrenez` entre 0 y 300; `(Tipo='Anulacion') = (TactoAnuladoId IS NOT NULL)`; `Tipo='Normal' OR Anulado=0` (una anulación no se anula, RF-14).
- Índice `(AnimalId, FechaHoraUtc, OrdenInsercion)` para "último vigente".
- FK a Animal, Usuario, EstadoReproductivo (anterior y nuevo).
- Trigger BEFORE DELETE: aborta siempre. Trigger BEFORE UPDATE: aborta salvo que `OLD.Anulado=0 AND NEW.Anulado=1` y todas las demás columnas sean iguales (comparadas con `IS`) excepto `ActualizadoEnUtc` (RF-12).

**Historial**: triggers BEFORE UPDATE y BEFORE DELETE con `RAISE(ABORT, ...)` (RF-11). Se agrega `CreadoEnUtc` igual a `FechaHoraUtc` por uniformidad con la constitución.

**Triggers de no borrar** (BEFORE DELETE → abort) en Establecimiento, Rodeo, Usuario, EstadoReproductivo y Animal (RF-15, RF-24), además de las FK en Restrict.

**Trigger "Sin asignar"**: BEFORE UPDATE OF Activo en Usuario que aborta si el usuario es el de `Id` fijo `Usuario.SinAsignarId` (constante GUID del dominio), pasa a Activo=0 y no existe otro usuario activo (RF-7b).

**Migración**: una sola, `Inicial`, en `src/Aviss.Infrastructure/Migrations`, generada con EF Core; los triggers, el CHECK de caravana y los índices parciales van como `migrationBuilder.Sql(...)` / `HasCheckConstraint` / `HasFilter` y `Down` los elimina. El SQL queda copiado **literalmente dentro de la migración** (no referenciado desde una constante compartida) para que una migración ya publicada no cambie si alguien edita código después.

**Conexión** (interceptor de `DbConnection`, se aplica en cada apertura): `PRAGMA foreign_keys=ON`, `journal_mode=WAL`, `synchronous=FULL`, `busy_timeout=5000`; luego lee `PRAGMA foreign_keys` y si no devuelve 1 lanza una excepción propia (la app no abre, RF-16).

**Datos iniciales** (no van como `HasData`): los siembra el código de arranque. Estados: se agregan los que falten **por `Codigo`** (PRENADA→"PREÑADA", INSEMIN, SECA, VACIA, FRESCA, NOINSEM; código de Dairy Comp = nombre), sin tocar los existentes ni reactivar (RF-6). Usuario "Sin asignar" (Id fijo): solo en la creación de la base (RF-7b).

## Capas y servicios

| Capa | Qué se agrega | RF |
|---|---|---|
| Domain | Entidades con constructores privados y métodos de fábrica que validan (`Establecimiento`, `Rodeo`, `Usuario`, `EstadoReproductivo`, `Animal`, `Tacto`, `Historial`); enums (`OrigenAnimal`, `EstadoRodeo`, `TipoTacto`, `AccionHistorial`); `Caravana` (valida y calcula la clave); `IdElectronico` (valida); `ReglasTacto` (funciones puras: validación de RF-8b y selección del último vigente / vigente anterior); `EstadosIniciales` (los 6 + códigos) | RF-3, 4, 4b, 5, 7, 8b, 13, 14, 17, 18 |
| Application | `Resultado` / `Resultado<T>` con `CodigoError` (rechazos de negocio sin excepciones, mensaje en español sin datos). Puertos: `IRelojService` (`UtcAhora`), `IDispositivoService` (`ObtenerId()`), `IUnitOfWork` (`EjecutarAsync(Func<Task<T>>)` abre transacción, confirma o revierte), `IEstablecimientoRepository`, `IRodeoRepository`, `IUsuarioRepository`, `IEstadoReproductivoRepository`, `IAnimalRepository` (por caravana normalizada, tiene tactos vigentes, contar no anulados), `ITactoRepository` (último vigente, siguiente `OrdenInsercion`), `IHistorialRepository` (solo `Agregar`), `IBackupService`, `IArranqueService`, `IArranqueResultado`. Servicios: `IEstablecimientoService`, `IRodeoService` (crear, dar de baja, contar animales), `IUsuarioService`, `ICatalogoEstadosService` (crear, modificar, desactivar, activar), `IAnimalService` (alta en campo, editar, anular alta), `ITrabajoCampoService` (`RegistrarTactoAsync`, `AnularTactoAsync`). Comandos: `RegistrarTactoCommand` (con `EstadoEsperado? EstadoAnteriorEsperado`: nulo = "no informado", si no, trae el id o vacío), `AnularTactoCommand`, `AltaEnCampoCommand`, `EditarAnimalCommand`, `CrearRodeoCommand`. | RF-1, 2, 5, 7, 8, 8b, 9, 10, 13, 14, 15, 24, 25, 26 |
| Infrastructure | `AvissDbContext` + configuraciones por entidad; migración `Inicial`; interceptor de conexión; `ConvertidorFechaUtc`; repositorios y `UnitOfWork` (un `DbContext` **scoped** por operación; la transacción es la de `Database.BeginTransactionAsync`); `RelojSistema`; `DispositivoService` (archivo `dispositivo.id` en el directorio de datos, **fuera** de la base, escrito a un temporal y movido); `BackupService`; `Migrador` (`IMigrador`: pendientes, aplicadas, aplicar; implementación EF); `SembradorInicial`; `ArranqueService`; `ResultadoArranque` en una clase singleton `ArranqueResultado`; `RutasAviss` (record con el directorio de datos, de donde salen `aviss.db`, `aviss.db.creando`, `backups/`, `danadas/`, `dispositivo.id`); `IEspacioLibreProvider`; extensión `AddAvissInfrastructure(rutas)`; `AvissDbContextFactory` de tiempo de diseño. | RF-1 a 28 |
| App | Solo cableado: `MauiProgram` llama a `AddAvissInfrastructure` con `FileSystem.AppDataDirectory` y lanza `IArranqueService.IniciarAsync()` guardando el resultado. Sin pantallas ni ViewModels. | RF-28 |

**Reglas de los casos de uso (resumen)**
- `RegistrarTactoAsync` (una transacción): leer animal (+ validar baja de animal/rodeo/establecimiento/estado) → tomar `ahora` del `IRelojService` → `ReglasTacto.Validar` (estado anterior, futuro +5 min, anterior al último vigente, 0–300 días) → siguiente `OrdenInsercion` → actualizar animal (estado, fecha último tacto, `ActualizadoEnUtc`) + insertar tacto + insertar un historial → `SaveChanges` único → commit. Cualquier fallo revierte todo.
- `AnularTactoAsync` (una transacción): validar RF-14 → insertar tacto de anulación (RF-13) → animal vuelve al estado anterior del anulado y a la fecha del último vigente anterior (o vacía) → marcar original `Anulado` (el trigger lo permite) → historial → commit.
- Fecha de último tacto: se guarda la `FechaHoraUtc` del tacto.
- Anular alta: sin tactos vigentes, solo `Origen=AltaEnCampo`; marca `Anulado`, libera caravana (por el índice parcial), historial.
- Editar animal: un historial por campo cambiado; cambiar caravana solo en alta en campo; sin cambios reales no genera nada.
- Historial: `Tacto` → Entidad "Tacto", Campo "Estado" (valores = ids de estado); anulación de tacto → igual sobre el tacto de anulación, Accion `AnulacionTacto`; alta → Entidad "Animal", Campo "Alta"; anulación de alta → Campo "Anulado" (0→1); ediciones → Campo = nombre del campo.

**Arranque (`ArranqueService.IniciarAsync`)**
1. Asegura directorios y el `IDispositivoService`.
2. Si existe `aviss.db.creando` (con sus `-wal`/`-shm`/`-journal`), lo descarta.
3. Sin `aviss.db`: crea en `aviss.db.creando` (migraciones + estados + "Sin asignar" + `PRAGMA integrity_check`), cierra conexiones, hace checkpoint de WAL y mueve a `aviss.db` con `File.Move` (RF-20). Sin backup previo.
4. Con `aviss.db`: abre en solo lectura y hace `PRAGMA quick_check`; si falla o no es SQLite, copia el archivo a `danadas/`, **no** lo modifica y devuelve `BaseDanada` (RF-27). Si las migraciones aplicadas incluyen alguna que la app no conoce: `BaseMasNueva`, no se toca.
5. Si hay migraciones pendientes: espacio libre → backup `PreviaActualizacion` verificado → huella del esquema (`sqlite_master`) y lista de migraciones aplicadas → `IMigrador.Aplicar` → si falla: si el esquema o las migraciones difieren de lo anterior se restaura la copia (copiar a `aviss.db.restaurando` y reemplazar); si no, queda como estaba; resultado `ActualizacionFallidaRestaurada`; si la restauración falla, `ActualizacionFallidaSinRestaurar` y no se borra ni sobrescribe nada (RF-19, RF-22).
6. Siembra estados faltantes; abre una conexión y verifica `foreign_keys=1`.
7. Guarda el `ResultadoArranque` (tipo + mensaje en español fijo, sin rutas, nombres ni excepciones con texto de SQLite).

**Backups (`BackupService.CrearAsync(MotivoBackup)`)**: calcula espacio necesario (tamaño de base + `-wal` + 10 % + 16 MB) y compara con `IEspacioLibreProvider` → copia con la API de backup de SQLite (`SqliteConnection.BackupDatabase`, consistente aunque haya WAL) a `<nombre>.tmp` → la **verifica** (abre, `integrity_check`, mismo esquema y misma cantidad de migraciones aplicadas) → renombra al nombre final `aviss-AAAAMMDDTHHmmssfffZ-{previa-actualizacion|a-pedido}.db` → recién entonces rota: conserva las 10 más nuevas (por la fecha del nombre) más la previa a actualización más reciente si quedó fuera; borra el resto. Los `.tmp` huérfanos no cuentan y se limpian.

## Pantallas
No hay pantallas ni ViewModels en esta spec. La única huella en `Aviss.App` es el cableado de arranque (RF-28); la pantalla que muestre el `ResultadoArranque` es de una spec futura.

## Archivos
- Crear (nombres orientativos):
  - `src/Aviss.Domain/`: `Entidades/*.cs`, `Enums/*.cs`, `Reglas/Caravana.cs`, `Reglas/IdElectronico.cs`, `Reglas/ReglasTacto.cs`, `Catalogo/EstadosIniciales.cs`
  - `src/Aviss.Application/`: `Comun/Resultado.cs`, `Comun/CodigoError.cs`, `Puertos/*.cs`, `Servicios/*.cs`, `Comandos/*.cs`, `Arranque/ResultadoArranque.cs`
  - `src/Aviss.Infrastructure/`: `Persistencia/AvissDbContext.cs`, `Persistencia/Configuraciones/*.cs`, `Persistencia/ConvertidorFechaUtc.cs`, `Persistencia/InterceptorConexion.cs`, `Persistencia/AvissDbContextFactory.cs`, `Migrations/*`, `Repositorios/*.cs`, `UnitOfWork.cs`, `Servicios/*.cs`, `Arranque/*.cs`, `Backups/BackupService.cs`, `Sistema/RelojSistema.cs`, `Sistema/DispositivoService.cs`, `Sistema/EspacioLibreProvider.cs`, `RutasAviss.cs`, `DependencyInjection.cs`
  - `.config/dotnet-tools.json` (herramienta local `dotnet-ef`)
  - Tests: `tests/Aviss.Domain.Tests/*`, `tests/Aviss.Infrastructure.Tests/*` (con `Soporte/` para `FakeReloj`, `BaseDeDatosDePrueba`, `EscenarioConServicios`)
- Modificar: `src/Aviss.Infrastructure/Aviss.Infrastructure.csproj`, `tests/Aviss.Infrastructure.Tests/*.csproj` (paquetes), `src/Aviss.App/MauiProgram.cs`, `CLAUDE.md` (comando de migración confirmado), `MEMORY.md`, `docs/plan.md` (marcar 1.1).

**Paquetes nuevos** (versiones 10.x de EF Core, a fijar al agregarlos)
- `Microsoft.EntityFrameworkCore.Sqlite` (Infrastructure): es el stack ya decidido; trae `Microsoft.Data.Sqlite` y el SQLite nativo para Android.
- `Microsoft.EntityFrameworkCore.Design` con `PrivateAssets=all` (Infrastructure): necesario solo para generar migraciones.
- `Microsoft.Extensions.DependencyInjection.Abstractions` (Infrastructure): para la extensión `AddAvissInfrastructure`.
- `dotnet-ef` como herramienta **local** del repo (`.config/dotnet-tools.json`): versión fijada para que cualquiera genere migraciones igual.
- Tests de Infrastructure: `Microsoft.Extensions.DependencyInjection` (armar el contenedor como lo hará la app). Nada más: se prueba contra SQLite real, sin InMemory ni librerías de mocks (los fakes son clases a mano).

## Decisiones
- **Garantías en la base con SQL escrito a mano en la migración** — la constitución exige que unicidad, FK e inmutabilidad valgan aunque el intento no venga de la app. Alternativa descartada: validar solo en los servicios, porque un acceso directo o un bug las saltearía.
- **Clave de caravana como columna generada con reemplazos explícitos** — igualar mayúsculas/minúsculas incluida la Ñ sin depender de código. Alternativas descartadas: `COLLATE NOCASE` (solo ASCII, la Ñ no se igualaría); una collation propia registrada por conexión (la base dejaría de abrirse con cualquier otra herramienta y de ser verificable fuera de la app); columna normalizada escrita por la app (un insert directo la podría saltear).
- **Índices únicos parciales (`Anulado = 0`)** — así el alta anulada libera la caravana y el identificador electrónico. Alternativa descartada: borrar o renombrar la caravana del anulado, porque rompe "no se borra nada" y "las columnas no cambian".
- **`OrdenInsercion` explícito en Tacto** — desempata por orden real de guardado y sobrevive a `VACUUM`. Alternativa descartada: `rowid`, que SQLite puede renumerar en tablas sin clave entera.
- **FK compuesta `(AnimalId, TactoAnuladoId)`** — la base misma garantiza que la anulación apunta a un tacto del mismo animal. Alternativa descartada: trigger de consulta cruzada, más SQL a mano y más fácil de errar.
- **Fechas como texto ISO de ancho fijo con `Z`** — ordenan bien como texto, se leen a mano y son UTC sin ambigüedad. Alternativas descartadas: `DateTimeOffset` (EF/SQLite lo maneja con limitaciones de orden) y ticks enteros (ilegibles al inspeccionar la base).
- **Datos iniciales sembrados por código y por `Codigo`, no por `HasData`** — RF-6 pide agregar solo los faltantes sin pisar cambios; `HasData` se re-aplica como "estado esperado" en cada migración. Alternativa descartada: `HasData`, porque podría pisar o reactivar.
- **Creación inicial en archivo temporal y `File.Move`** — un corte de energía nunca deja una base a medias en `aviss.db`. Alternativa descartada: crear directo y marcar "completa" al final, que obliga a leer una base posiblemente corrupta para decidir.
- **Backup con `BackupDatabase` de SQLite a `.tmp`, verificación y recién después renombre** — es consistente con WAL y la copia incompleta nunca parece válida. Alternativa descartada: `File.Copy` (con WAL puede copiar una base inconsistente) y `VACUUM INTO` (también sirve, pero deja fuera la verificación de esquema que ya hacemos con la API estándar).
- **Base dañada: se copia aparte y la original no se toca** — evita que un reinicio cree una base nueva y "esconda" la dañada. Alternativa descartada: renombrarla y crear una vacía, porque el veterinario perdería la vista de sus datos sin aviso.
- **Identificador de dispositivo en un archivo fuera de la base** — RF-23 exige que sobreviva a recrear o restaurar la base. Alternativa descartada: guardarlo en una tabla (se perdería al recrear) o en `Preferences` de MAUI (`Infrastructure` no puede depender de MAUI; constitución §5).
- **Rechazos de negocio como `Resultado` con código de error, no excepciones** — los ViewModels futuros mostrarán el mensaje; las excepciones quedan para fallos reales (disco, base). Alternativa descartada: excepciones de dominio, más difíciles de testear y de mapear a mensajes.
- **Casos de uso en `Application` y sus tests en `Infrastructure.Tests` contra SQLite real** — lo que importa es la transacción y los constraints reales. Alternativa descartada: fakes de repositorios en `Application.Tests`, que no probarían nada de lo que la spec garantiza.
- **`DbContext` scoped por operación** — un contexto y una transacción por caso de uso, sin estado compartido. Alternativa descartada: contexto singleton (cachea entidades y mezcla operaciones).
- **Sin identificar al usuario por servicio `IUsuarioActualService` todavía** — los comandos reciben `UsuarioId` explícito; "Sin asignar" tiene Id fijo. La selección de veterinario es de otra spec (se agrega entonces el puerto). Alternativa descartada: crear ahora el servicio con estado, que sería código sin consumidor.
- **La hora del tacto la toma el servicio de `IRelojService`** — y se usa el mismo instante para validar y guardar. Alternativa descartada: que el comando traiga la fecha, porque abre la puerta a fechas manipuladas desde la pantalla (RF-8b).
- **Verificación de FK al abrir con excepción propia** — si el pragma no queda en 1 la app no abre (RF-16). Alternativa descartada: confiar en que `Microsoft.Data.Sqlite` lo activa por defecto, porque cambia entre versiones.
- **Un solo `SaveChanges` por caso de uso dentro de la transacción** — atomicidad doble (el `SaveChanges` ya es una transacción y la externa cubre además las lecturas y la numeración). Alternativa descartada: varios `SaveChanges` sin transacción externa.

## Estrategia de tests

Todo en xUnit. Convenciones de soporte (en `Aviss.Infrastructure.Tests/Soporte`):
- **Reloj de prueba:** `FakeReloj : IRelojService` con `UtcAhora` fijo y métodos `Avanzar(TimeSpan)` / `Fijar(DateTime)`. Se inyecta en el contenedor de prueba y en `BackupService`/`ArranqueService`, de modo que los nombres de backup y las horas de tactos sean deterministas.
- **Base de prueba:** `BaseDeDatosDePrueba` crea una carpeta temporal única por test, corre la migración real y se borra al terminar. Da una `SqliteConnection` cruda para probar triggers y constraints **salteando la app**. `EscenarioConServicios` arma el contenedor real (`AddAvissInfrastructure` + `FakeReloj` + `FakeEspacioLibre`) y entrega un scope nuevo por operación.
- **Triggers:** SQL crudo (`UPDATE`/`DELETE` directos) y se espera `SqliteException`; luego se relee el registro para confirmar que quedó intacto.
- **Falla a mitad de transacción:** dentro del test se crea con SQL crudo un trigger `BEFORE INSERT ON Historial ... RAISE(ABORT)`; así `RegistrarTactoAsync` falla **después** de haber ejecutado el UPDATE del animal y el INSERT del tacto, y se verifica que animal, tacto e historial quedaron como antes. Lo mismo para la anulación. Además se prueba que cerrar una conexión con la transacción sin confirmar no deja nada (corte de energía simulado); la prueba real con la app cortada es la verificación manual del emulador.
- **Migración vieja y fallida:** `IMigrador` es un puerto; en el test se usa un `MigradorFalso` que, sobre una base real de prueba, agrega una tabla "v2" y recién después lanza excepción (falla a mitad). Con el migrador EF real se prueban: base nueva sin backup, base ya al día sin backup, y base "más nueva" (fila ajena insertada en `__EFMigrationsHistory`).
- **Backups:** `FakeEspacioLibre` (devuelve el espacio indicado) y carpetas temporales; se crean 11/12 copias con `FakeReloj` avanzando y se comprueba cuáles quedan. Una copia incompleta se simula dejando un `.tmp` y haciendo fallar la verificación con una base trucha.
- **Sin datos en mensajes:** se crea una base con un animal de nombre/caravana reconocible, se fuerzan los errores de arranque y se verifica que `Mensaje` no contiene esos textos ni rutas.
- **Rendimiento:** categoría `[Trait("Categoria","Rendimiento")]`; se cargan 20.000 animales y 100.000 tactos con SQL masivo en una transacción y se mide con `Stopwatch` (tras un calentamiento) la búsqueda por caravana (< 100 ms) y el registro de un tacto (< 300 ms).

| RF | Test (proyecto › clase › caso) |
|---|---|
| RF-1 | Infrastructure.Tests › `EstablecimientoServiceTests` › guarda varios; nombre vacío rechazado; notas opcionales |
| RF-2 | Infrastructure.Tests › `RodeoServiceTests` › exige todos los campos; cantidad por conteo (incluye altas, excluye anuladas); dos rodeos con misma huella; rodeo Exportado acepta tactos y altas. `EsquemaBaseTests` › NOT NULL y CHECK de rodeo por SQL crudo |
| RF-3 | Domain.Tests › `AnimalTests` (alta mínima, estado y fecha vacíos). Infrastructure.Tests › `AnimalServiceTests` › fecha vacía hasta el primer tacto; columnas originales y origen inmutables (también por trigger, `EsquemaBaseTests`) |
| RF-4 | Domain.Tests › `CaravanaTests` › límites (0, 1, 20, 21 caracteres), espacios en extremos, "0123"≠"123", clave de mayúsculas con Ñ y vocales con tilde. Infrastructure.Tests › `EsquemaBaseTests` › duplicado por SQL crudo con otra capitalización/Ñ rechazado; mismo número en otro rodeo válido; anulado libera el número; equivalencia entre `Caravana.Clave` y la columna generada |
| RF-4b | Domain.Tests › `IdElectronicoTests` (espacios, 32). Infrastructure.Tests › `EsquemaBaseTests` › duplicado en rodeo rechazado, no anulados únicamente |
| RF-5 | Domain.Tests › `EstadoReproductivoTests` (formato de código). Infrastructure.Tests › `CatalogoEstadosServiceTests` › alta sin cambiar código; renombrar no rompe tactos ni historial; nombre repetido con otra capitalización/Ñ rechazado; código de Dairy Comp único solo entre activos |
| RF-6 | Infrastructure.Tests › `SembradorInicialTests` › seis estados con código = nombre; segunda corrida no duplica; no pisa un estado modificado ni reactiva uno desactivado; agrega uno faltante |
| RF-7 / 7b | Infrastructure.Tests › `UsuarioServiceTests` › matrícula de 31 caracteres rechazada, sin contraseña; tacto/alta/edición/anulación guardan usuario. `EsquemaBaseTests` › trigger impide baja de "Sin asignar" siendo el único activo y la permite con otro. `ArranqueServiceTests` › creación inicial deja "Sin asignar" |
| RF-8 | Infrastructure.Tests › `RegistrarTactoTests` › actualiza animal+tacto+un historial; animal sin estado (estado anterior vacío); mismo estado válido; falla forzada con trigger revierte todo |
| RF-8b | Domain.Tests › `ReglasTactoTests` (cada regla, bordes +5 min, 0/300/301/-1, igualdad con último vigente). Infrastructure.Tests › `RegistrarTactoTests` › rechazos por animal anulado, rodeo/establecimiento/estado de baja, estado anterior distinto, reloj anterior al último vigente con mensaje que sugiere revisar la hora |
| RF-9 | Infrastructure.Tests › `RegistrarTactoTests` › leído por una conexión nueva inmediatamente después; conexión cerrada sin commit no deja nada |
| RF-10 | Infrastructure.Tests › `HistorialTests` › un registro por alta, por campo editado, por tacto, por anulación de tacto y de alta, con valores por id de estado |
| RF-11 | Infrastructure.Tests › `EsquemaBaseTests` › UPDATE y DELETE directos sobre Historial abortan y el registro queda intacto |
| RF-12 | Infrastructure.Tests › `EsquemaBaseTests` › UPDATE/DELETE de Tacto abortan; `Anulado` 0→1 con `ActualizadoEnUtc` permitido; 1→0 rechazado; cambiar cualquier otro campo junto con la marca rechazado |
| RF-13 | Infrastructure.Tests › `AnularTactoTests` › tacto de anulación con los datos exactos, animal vuelve a estado y fecha anteriores (o vacía si era el único), original marcado, historial; otro usuario puede anular; falla forzada revierte |
| RF-14 | Infrastructure.Tests › `AnularTactoTests` › rechaza ya anulado, de anulación, no último, animal anulado; la anulación no cuenta como último; CHECK de la base contra anular una anulación |
| RF-15 | Infrastructure.Tests › `EsquemaBaseTests` › DELETE de Animal rechazado |
| RF-16 | Infrastructure.Tests › `EsquemaBaseTests` › animal sin rodeo, rodeo sin establecimiento, tacto con animal/usuario/estados inexistentes, anulación apuntando a tacto de otro animal; DELETE con dependientes rechazado. `ConexionTests` › `foreign_keys` es 1 en cada conexión; `VerificarForeignKeys` lanza si está en 0 |
| RF-17 | Infrastructure.Tests › `FechasUtcTests` › con `FakeReloj`, el instante leído es idéntico y `Kind=Utc`; escribir fecha local lanza; todas las entidades tienen `CreadoEnUtc`; editables `ActualizadoEnUtc` cambia al editar |
| RF-18 | Infrastructure.Tests › `FechasUtcTests` › Ids GUID distintos; dos tactos con la misma fecha y hora se ordenan por `OrdenInsercion`; índice único del orden |
| RF-19 | Infrastructure.Tests › `ArranqueServiceTests` › base con migración pendiente genera backup previo verificado antes de migrar; sin espacio o con backup fallido no migra |
| RF-20 | Infrastructure.Tests › `ArranqueServiceTests` › sin base crea completa sin backup; `.creando` huérfano se descarta y se recrea; `aviss.db` nunca existe a medias |
| RF-21 | Infrastructure.Tests › `BackupServiceTests` › 11.ª copia borra la más antigua; la previa más reciente nunca se borra; vieja se borra solo con la nueva verificada; sin espacio no copia; `.tmp` huérfano ignorado; nombre con fecha UTC y motivo |
| RF-22 | Infrastructure.Tests › `ArranqueServiceTests` › migrador falso que falla a mitad restaura la base original (esquema idéntico); falla de restauración deja base y copia intactas y reporta el error |
| RF-23 | Infrastructure.Tests › `DispositivoServiceTests` › mismo Id entre instancias; no cambia al borrar y recrear la base; todo tacto guarda el Id |
| RF-24 | Infrastructure.Tests › `BajasTests` › DELETE directo de establecimiento, rodeo, usuario y estado abortan; baja no altera tactos/historial/animales; no admite rodeo/alta/tacto en baja; usuario o estado de baja no se eligen para registros nuevos |
| RF-25 | Infrastructure.Tests › `AltaEnCampoTests` › datos mínimos, historial, caravana repetida rechazada; anular alta libera número, rechaza con tactos vigentes, importado no se anula; animal anulado no cuenta ni admite tactos/ediciones |
| RF-26 | Infrastructure.Tests › `EditarAnimalTests` › edita observaciones, id electrónico y caravana (solo alta en campo); un historial por campo; usuario y fecha de modificación |
| RF-27 | Infrastructure.Tests › `ArranqueServiceTests` › base con migración desconocida no se abre ni cambia (huella de archivo igual); archivo no SQLite o dañado: se copia a `danadas/`, el original queda intacto |
| RF-28 | Infrastructure.Tests › `ArranqueServiceTests` › `IArranqueResultado` refleja cada tipo con mensaje en español; los mensajes no contienen caravanas, nombres ni rutas |
| Volumen | Infrastructure.Tests › `RendimientoTests` (Trait Rendimiento) |
| Build | `dotnet build` y `dotnet test` en verde |

## Riesgos
- **`dotnet ef` con la app Android como proyecto de inicio** (`-s src/Aviss.App`) puede no funcionar. Plan B ya previsto: `AvissDbContextFactory` en Infrastructure y usar `-s src/Aviss.Infrastructure`; se confirma en T2 y se corrige `CLAUDE.md` en T5.
- **Fecha futura (RF-8b) solo se puede probar a nivel de regla pura**: como la hora del tacto sale del mismo reloj que la validación, el servicio nunca la ve futura en un tacto propio. En la práctica un reloj adelantado y luego corregido aparece como "anterior al último vigente". Está cubierto por tests de dominio y de servicio respectivamente.
- **Espacio libre en Android**: `DriveInfo` puede comportarse distinto en Android; se verifica en el emulador (T5) y, si falla, el cálculo queda detrás de `IEspacioLibreProvider` para cambiarlo sin tocar el resto.
- **SQLite nativo en Android**: la app debe compilar y abrir la base con el paquete nativo de EF Core Sqlite; se verifica en el emulador (T5, verificación aparte).
- **Tests de rendimiento** pueden ser lentos o variables según la PC; están en una categoría que se puede excluir con `--filter "Categoria!=Rendimiento"` si molestan, pero siguen siendo parte del criterio de aceptación.
- **Supuestos a registrar en `MEMORY.md`** (T5): código de Dairy Comp = nombre del Excel; caravana tal cual, sin normalizar ceros; identificador electrónico único por rodeo; último tacto por tablet; tamaño de copias por los `.xls` guardados.
- **Una sola migración `Inicial`**: no hay forma de probar con EF real una "base vieja" hasta que exista una segunda migración; el flujo de actualización se prueba con el migrador falso y se reverifica cuando llegue la primera migración real (spec de importación).
