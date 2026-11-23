# Plan — 005 Eventos del catálogo

Spec: [spec.md](spec.md) (aprobada el 2026-11-23). Tareas: [tasks.md](tasks.md). Rama: `spec/005-eventos-del-catalogo`.

## Orden de implementación: DESPUÉS de la spec 004

Se implementa **después** de la 004 (En Espera y Rechazo) y sobre ella. Motivos:
- La 004 crea la tabla `Rechazo` (registro de campo que no cambia el estado, entra en el orden compartido, se anula, se edita) y el `EventoRechazo` fijo. Un "solo evento" (OK, Aborto, Revisar Nuevamente) es **exactamente eso** con un nombre distinto: esta spec lo reutiliza en vez de duplicarlo.
- Los dos tocan los mismos archivos: `TrabajoViewModel.Ficha.cs`, `EstadosTrabajo.cs`, `MovimientosViewModel.cs`, `TrabajoPage.xaml`, `MovimientosPage.xaml`, `DtosCampo.cs`, `ComandosCampo.cs`, `TrabajoCampoService.cs`, `ConsultaCampoService.cs`, `Configuraciones.cs`, el snapshot de migraciones. Hacer la 005 primero obligaría a rehacer la 004 (que ya tiene plan y tareas) sobre una lista de eventos distinta.
- La 004 agrega el ítem fijo "Rechazo" al final de la lista plana (`Estados`); la 005 lo reemplaza por la lista de dos niveles. Es un reemplazo limpio si la 004 ya está, y un conflicto si no.

**Precondición de T1: cumplida.** La 004 ya está mergeada en `main` (existen `Rechazo`, `EventoRechazo` como `static readonly EstadoActivoDto` en `TrabajoViewModel.Espera.cs` con Id `EventosCampo.Rechazo`, `CorregirRegistroAsync` y la migración `EsperaYRechazo`). La migración de esta spec se llama `EventosDelCatalogo` y va **después** de `EsperaYRechazo`. **Coordinación con la spec 006 (sin veterinario, otra sesión):** esta spec no toca nada relativo a `UsuarioId`; conserva el de los comandos (`RegistrarSoloEventoCommand(AnimalId, EventoId, Observaciones, UsuarioId)`) y los conflictos al mergear se resuelven quitándolo. Nombres reales de la 004: la ficha usa `Eventos` (catálogo + Rechazo, en `.Espera.cs`) y `Estados` (`.Ficha.cs`); Editar usa `EstadosParaEditar`; el rechazo se guarda en `TrabajoCampoService.RegistrarRechazoNucleoAsync`.

## Resumen del diseño
- **"Solo evento" = una fila del catálogo con `Tipo = SoloEvento`**, y cada vez que se guarda se escribe en la **tabla `Rechazo` de la 004**, que gana una columna `EventoId` (nulo = el Rechazo fijo; con valor = el evento del catálogo). No toca el estado de la vaca ni su último tacto, por construcción. Se reutilizan orden compartido, anulación, edición, historial, aviso de repetido, movimientos.
- **Identidad estable** de los 9 estándar: columna nueva `ClaveEstandar` en `EstadoReproductivo` (nula en los creados a mano). El `Codigo` interno no se toca (sigue sin cambiar nunca).
- **Principal / orden:** columnas `EsPrincipal` y `Orden` en el catálogo. Los de "Otro evento" no usan `Orden`: se ordenan por nombre en código.
- **Lista de dos niveles** armada por una función pura (`ArmadorListaEventos`) y mostrada por **un solo componente** (`SelectorEventosView` + `SelectorEventosViewModel`) que usan la ficha y la edición de movimientos.
- **Migración sin reconstruir tablas:** `ALTER TABLE ... ADD COLUMN` a mano, para no perder los triggers (EF reconstruiría la tabla).

## Base de datos (migración `EventosDelCatalogo`)

`EstadoReproductivo` (agregar columnas; todas con valor por defecto, así las filas viejas quedan válidas):
| Columna | Definición | Para qué |
|---|---|---|
| `Tipo` | `TEXT NOT NULL DEFAULT 'Estado' CHECK (Tipo IN ('Estado','SoloEvento'))` | RF-1, RF-16, RF-17 |
| `EsPrincipal` | `INTEGER NOT NULL DEFAULT 0 CHECK (EsPrincipal IN (0,1))` | RF-2, RF-3 |
| `Orden` | `INTEGER NOT NULL DEFAULT 0 CHECK (Orden >= 0)` | RF-2 (solo importa entre principales) |
| `ClaveEstandar` | `TEXT NULL CHECK (ClaveEstandar IS NULL OR (length(ClaveEstandar) > 0 AND ClaveEstandar NOT GLOB '*[^A-Z0-9_]*'))` | RF-5, RF-20 |

- Índice único parcial `IX_EstadoReproductivo_ClaveEstandar` sobre `ClaveEstandar WHERE ClaveEstandar IS NOT NULL`: un estándar no se puede duplicar.
- **Relleno (en la misma migración, SQL a mano):** `UPDATE EstadoReproductivo SET ClaveEstandar = Codigo WHERE Codigo IN ('PRENADA','INSEMIN','SECA','VACIA','FRESCA','NOINSEM')`, y marca de principal con su orden: VACIA=3, PRENADA=4, NOINSEM=6. No toca `Nombre`, `CodigoDairyComp` ni `Activo` (un inicial desactivado a mano sigue desactivado, RF-19). Los creados a mano quedan con los valores por defecto (no principal, tipo `Estado`).
- `Rechazo` (de la 004): `ALTER TABLE Rechazo ADD COLUMN EventoId TEXT NULL REFERENCES EstadoReproductivo(Id)` (SQLite lo permite con valor por defecto nulo) + índice `IX_Rechazo_EventoId`.
- **Triggers nuevos** (la regla también en la base, constitución §7):
  - `BEFORE INSERT ON Rechazo` rechaza si `EventoId` no es nulo y el evento no es `Tipo = 'SoloEvento'`.
  - `BEFORE INSERT ON Tacto` rechaza un tacto `Normal` cuyo `EstadoNuevoId` sea de `Tipo = 'SoloEvento'`. Los tactos viejos que quedaron apuntando a un evento adoptado (RF-20) no se tocan: solo se bloquean inserciones nuevas.
  - **Trigger de inmutabilidad de `Rechazo` (004):** si enumera columnas, hay que **recrearlo incluyendo `EventoId`** (DROP + CREATE a mano). Verificarlo con un test: no se puede cambiar `EventoId` de un registro existente.
- **Cómo escribir la migración:** `dotnet ef migrations add EventosDelCatalogo -p src/Aviss.Infrastructure -s src/Aviss.Infrastructure`, y después **reemplazar el `Up` generado por SQL a mano** con `migrationBuilder.Sql` (como `Mangas`). El `Designer` y el snapshot se dejan como los generó EF. Al final, `dotnet ef migrations has-pending-model-changes` debe decir que no hay cambios. `Down` vacío o equivalente (el proyecto no baja versiones).
- No cambia el CHECK de `Accion` de `Historial`: se reutilizan `RechazoCreado` y `RechazoAnulado` (el valor guardado lleva el nombre del evento). Así no hace falta `ReconstruirHistorial`.
- **Atomicidad (RF-19):** el arranque ya hace backup previo y restaura si la migración falla (`ArranqueService`). Lo que falta (los 3 eventos nuevos / adoptados) lo agrega el sembrador **en un solo `SaveChanges`** (una transacción): o están todos o ninguno; si la app se corta entre la migración y la siembra, el próximo arranque termina la siembra (es idempotente).

### Modelo (Domain)
- `Enums`: `TipoEvento { Estado, SoloEvento }` (texto en la base).
- `EstadoReproductivo` gana `Tipo`, `EsPrincipal`, `Orden`, `ClaveEstandar` (solo lectura desde afuera). `Crear(...)` (el de "+ Nuevo evento") deja `Tipo = Estado`, no principal, sin clave: cumple RF-4. Métodos nuevos: `CrearEstandar(inicial, ahora)` y `AdoptarComoEstandar(inicial, ahora)` (solo pone clave, tipo, principal y orden; **no cambia nombre, código ni `Activo`**, RF-20). `Modificar` sigue cambiando nombre y código de Dairy Comp sin tocar la identidad (RF-1, RF-5).
- `Catalogo/EstadosIniciales`: `EstadoInicial` pasa a `(Codigo, Nombre, CodigoDairyComp, Tipo = Estado, EsPrincipal = false, Orden = 0)`; `Codigo` es la `ClaveEstandar`. Los 9:

| Clave | Nombre | Código Dairy Comp | Tipo | Principal / orden |
|---|---|---|---|---|
| `REVISAR_NUEVAMENTE` | Revisar Nuevamente | `REVISAR NUEVAMENTE` (provisorio) | SoloEvento | sí / 1 |
| `OK` | OK | `OK` (provisorio) | SoloEvento | sí / 2 |
| `VACIA` | VACIA | `VACIA` | Estado | sí / 3 |
| `PRENADA` | PREÑADA | `PREÑADA` | Estado | sí / 4 |
| `ABORTO` | Aborto | `ABORTO` (provisorio) | SoloEvento | sí / 5 |
| `NOINSEM` | NOINSEM | `NOINSEM` | Estado | sí / 6 |
| `INSEMIN`, `SECA`, `FRESCA` | igual que hoy | igual que hoy | Estado | no |

- `Reglas/OrdenAlfabetico.cs` (pura): `Clave(nombre)` = mayúsculas, sin tildes ni diéresis, y la Ñ queda **entre la N y la O** (se arma `N` + un carácter mayor que cualquier letra, p. ej. `Ñ` → `N{`, y se compara en orden ordinal). Es una clave **distinta** de `Caravana.CalcularClave` (esa conserva las tildes porque sirve para detectar repetidos).

## Capas y servicios
| Capa | Qué se agrega o cambia | RF |
|---|---|---|
| Domain | `TipoEvento`, columnas y métodos de `EstadoReproductivo`, `EstadosIniciales` ampliado, `OrdenAlfabetico` | RF-1, RF-2, RF-3, RF-5, RF-7 |
| Application | `ArmadorListaEventos` (pura): recibe los estados del catálogo y devuelve `ListaEventosDto(Principales, Otros)`: principales activos por `Orden` (desempata por creación); "Otros" = `EventoRechazo` primero y después los activos no principales por `OrdenAlfabetico.Clave` (desempata por creación) | RF-6, RF-7, RF-11, RF-13 |
| Application | `EventoCampoDto(Id, Nombre, CambiaEstado, EsPrincipal)` reemplaza a `EstadoActivoDto`; `ICampoService.ListarEventosAsync()` reemplaza a `ListarEstadosActivosAsync()` | RF-6, RF-12 |
| Application | `RegistrarRechazoCommand` (004) pasa a **`RegistrarSoloEventoCommand(AnimalId, EventoId, Observaciones, UsuarioId)`** y `RegistrarRechazoAsync` a `RegistrarSoloEventoAsync`; `EventoId = EventoRechazo` es el Rechazo fijo. `MovimientoDto` suma `EventoId` (Guid?) para resaltar el evento del registro sin comparar nombres | RF-12, RF-16, RF-17, RF-18 |
| Application | Importación: `ValidadorImportacion.IndexarEstadosActivos` ignora los `SoloEvento` (un `RPRO` igual a su código entra sin estado y con advertencia, como un código desconocido; `RPRO` vacío sigue sin advertencia) | caso límite RPRO |
| Infrastructure | `SembradorInicial.SembrarEstadosAsync` con adopción (ver decisiones); el servicio de tacto rechaza un `SoloEvento` como estado; el servicio de solo-evento valida que el evento exista, esté activo y sea `SoloEvento` (o sea `EventoRechazo`), guarda `Rechazo.EventoId` (nulo para el Rechazo fijo) y la fecha y hora exactas del reloj, y historiza con el nombre; `CorregirRegistroAsync` resuelve el tipo del evento elegido en la base (estado → tacto, solo evento → rechazo con `EventoId`) | RF-16 a RF-18, RF-22 |
| Infrastructure | `ConsultaCampoService`: lista de eventos con `ArmadorListaEventos`; movimientos con `EventoId`; los nombres del historial y de los movimientos salen de **todo** el catálogo (activo o no) | RF-13, RF-17 |
| Infrastructure | `CatalogoEstadosService.CrearAsync` ("+ Nuevo evento") sin cambios de reglas: crea `Estado`, no principal | RF-4, RF-8 |

## Pantallas
- **`SelectorEventosViewModel`** (App, clase común, sin dependencias de página): `Principales`, `Otros`, `MostrandoOtros`, `Resaltado` (Guid?), `OtroEventoResaltado`, `PermiteNuevo`, comandos `VerOtros`, `Volver`, `Elegir(evento)`; se abre siempre en los principales. `OtroEventoResaltado` es verdadero solo si el evento resaltado está en `Otros` **activos** (un principal desactivado no resalta nada).
- **`SelectorEventosView`** (`Controles/SelectorEventosView.xaml`, `ContentView`): principales como botones de 60 dp en un `ScrollView` con barra siempre visible (`MaximumHeightRequest` 480: los 6 principales + "Otro evento" miden 7 × 60 + 6 × 8 = 468 y entran sin desplazar); "Otro evento"; vista secundaria con `ScrollView` de la lista (Rechazo primero), y **"+ Nuevo evento" y "Volver" fuera del scroll** (siempre visibles). Resaltado con el convertidor `EstadoActual` existente (o uno equivalente) contra `Resaltado`.
- **`TrabajoViewModel` / `TrabajoPage`:** la capa `CuadroTrabajo.Eventos` pasa a mostrar el selector (no se agrega ningún cuadro nuevo: la lista secundaria es una vista dentro de la misma capa, así la regla de las lecturas con cuadro abierto, spec 003 RF-19, se cumple sin cambios, RF-15). `Estados` / `HayEstadosActivos` / `CargarEstadosAsync` pasan a `Selector`. `ElegirEventoAsync` recibe un `EventoCampoDto`. **`EntrarAsync` ramifica por `CambiaEstado`:** verdadero → `RegistrarTactoAsync`; falso → `RegistrarSoloEventoAsync` (incluye `EventoRechazo`). Resaltado inicial = estado actual de la vaca; después, el elegido. El formulario "+ Nuevo evento" se abre desde la vista secundaria; al crear, queda en la vista secundaria sin elegir nada.
- **`MovimientosViewModel` / `MovimientosPage`:** `EstadosParaEditar` se reemplaza por el mismo selector (`PermiteNuevo = false`), resaltando `item.Dto.EventoId`. Un registro de solo evento (OK, Aborto, Revisar Nuevamente o Rechazo) se puede editar y deshacer igual que un tacto.
- Texto y alto contraste según constitución §9; nada de escritura para elegir un evento.

## Archivos
- Crear: `src/Aviss.Domain/Enums/TipoEvento.cs` (o dentro de `Enums.cs`), `src/Aviss.Domain/Reglas/OrdenAlfabetico.cs`, `src/Aviss.Application/Campo/ArmadorListaEventos.cs`, `src/Aviss.Infrastructure/Migrations/*_EventosDelCatalogo.cs` (+ Designer), `src/Aviss.App/ViewModels/SelectorEventosViewModel.cs`, `src/Aviss.App/Controles/SelectorEventosView.xaml(.cs)`; tests: `Domain.Tests › OrdenAlfabeticoTests`, `EstadoReproductivoEventosTests`; `Application.Tests › ArmadorListaEventosTests`, `SelectorEventosViewModelTests`, `TrabajoViewModelEventosTests`; `Infrastructure.Tests › EsquemaEventosTests`, `SembradorEventosTests`, `MigracionEventosTests`, `SoloEventoTests`.
- Modificar: `Domain/Entidades/EstadoReproductivo.cs`, `Domain/Catalogo/EstadosIniciales.cs`, `Infrastructure/Persistencia/Configuraciones/Configuraciones.cs` (catálogo y `Rechazo`), `Migrations/AvissDbContextModelSnapshot.cs`, `Arranque/SembradorInicial.cs`, `Servicios/{TrabajoCampoService,ConsultaCampoService,CampoService,EstructuraServices}.cs`, `Application/Campo/{DtosCampo,ComandosCampo}.cs`, `Application/Servicios/Servicios.cs`, `Application/Importacion/ValidadorImportacion.cs`, `App/ViewModels/{TrabajoViewModel.Ficha,EstadosTrabajo,MovimientosViewModel}.cs`, `App/Paginas/{TrabajoPage,MovimientosPage}.xaml`, fakes y tests de la 004 que usaban `RegistrarRechazoAsync`, `ListarEstadosActivosAsync` o `EstadoActivoDto`, `tests/Aviss.Infrastructure.Tests/Soporte/BaseDeDatosDePrueba.cs`.
- Documentación (la hace el implementador en T5): `docs/plan.md`, `MEMORY.md`, `docs/arquitectura.md`, spec 004 (mención de numeración), esta spec (casillas).

## Decisiones
- **"Solo evento" se guarda en la tabla `Rechazo` (con `EventoId`) y se declara en el catálogo con `Tipo`.** Elegida porque la 004 ya resuelve todo lo difícil (orden compartido, anulación, edición, no tocar el estado, historial) y porque la columna nueva es nula para el Rechazo, así que lo existente no cambia. Alternativas descartadas: (a) **un `Tacto` que no cambia el estado** (nuevo `Tipo` en `Tacto`): obliga a revisar cada consulta de "último tacto vigente", la anulación que restaura el estado anterior y los CHECK de `Tacto`, y mezcla dos significados en una tabla que hoy significa "cambió el estado"; (b) **una tabla nueva** `EventoRegistrado`: duplica entidad, triggers, índices, servicios y pruebas que la 004 ya tiene. Costo asumido: el nombre `Rechazo` queda algo pobre para guardar "OK" (se documenta con comentarios; renombrar tabla y clase tocaría triggers e índices y se deja para una limpieza futura).
- **Identidad estable con una columna `ClaveEstandar`**, no con el `Codigo` interno ni con el nombre. Elegida porque RF-20 obliga a que un evento creado a mano (con otro `Codigo`) pase a ser el estándar sin cambiar su nombre ni su código, y el `Codigo` interno no puede cambiar nunca. Alternativas descartadas: reescribir `Codigo` al adoptar (rompe la regla "no cambia nunca" y su CHECK); identificar por nombre (renombrar rompería el principal, RF-5).
- **Principal y orden como columnas del catálogo**, no constantes en la pantalla (RF-2, constitución §4). `Orden` solo vale entre principales; el resto se ordena por nombre en código. Alternativa descartada: un `Orden` para todos con renumeración al crear eventos: obliga a reescribir filas cada vez que el veterinario crea uno y no aporta nada porque el orden de "Otro evento" es alfabético por regla.
- **Orden alfabético en C#, no en SQL.** `Caravana.CalcularClave` y la columna `NombreClave` conservan las tildes (sirven para detectar repetidos) y SQLite no ordena la Ñ ni las tildes como pide RF-7. Con ~25 eventos ordenar en memoria no cuesta nada y se prueba sin base. Alternativa descartada: una columna generada de orden: otra columna que mantener por una lista de decenas de filas.
- **Rechazo sigue fijo en la aplicación (`EventoRechazo`), no en el catálogo** (decisión de la spec 004; en esta spec es la definición de "Rechazo"): el `ArmadorListaEventos` lo pone primero en "Otros". Alternativa descartada: sembrarlo como un `SoloEvento` más del catálogo: se podría desactivar o mover, contra la regla de la 004.
- **Un solo componente para las dos pantallas** (`SelectorEventosView` + ViewModel). Elegido porque RF-12 pide "la misma lista" en la ficha y en Editar y hoy el XAML está duplicado. Alternativa descartada: copiar el XAML en `TrabajoPage` y `MovimientosPage` (ya pasó y es lo que hay que evitar).
- **La lista secundaria es una vista dentro de la capa `Eventos`**, no un `CuadroTrabajo` nuevo. Así no cambia la regla de lecturas con cuadros abiertos (RF-15) ni las condiciones de `PuedeEntrar`. Alternativa descartada: un cuadro `OtrosEventos`: obligaría a revisar cada `switch` de `CuadroTrabajo` y la cola de lecturas.
- **Un solo comando de guardado de solo-evento (`RegistrarSoloEventoCommand`) con `EventoId`**, y el ViewModel decide por `CambiaEstado`. Elegido porque OK y Rechazo se comportan igual (RF-17, RF-18). Alternativa descartada: dejar `RegistrarRechazoCommand` y agregar otro comando para los del catálogo: dos caminos casi idénticos para mantener y probar.
- **Corrección del código provisorio (RF-1):** cuando llegue el código real se corrige con una **migración de datos** (`UPDATE ... WHERE ClaveEstandar = 'OK' AND CodigoDairyComp = 'OK'`, o sea solo si todavía vale el provisorio) en la spec o el cambio que lo traiga. Es seguro porque los tactos apuntan al `Id` y la identidad es `ClaveEstandar`; esa migración deberá resolver a mano si un evento creado a mano ya usa el código real (el índice único de código entre activos). Esta spec solo deja un test que prueba que `Modificar` conserva `Id`, clave y registros. Alternativa descartada: una columna `EsProvisorio` o pantalla de catálogo (la spec lo excluye).
- **Adopción (RF-20) en el sembrador, no en la migración.** La migración solo rellena lo que se puede decidir con SQL (clave de los 6 iniciales, principal y orden). La adopción compara nombre y código sin mayúsculas y necesita lógica de C# (`Caravana.CalcularClave`). Orden de búsqueda por cada estándar que falta: ya tiene su `ClaveEstandar` → listo; si no, un estado **sin clave** con el mismo `Codigo`, y si no, con el mismo nombre (clave de nombre), y si no, con el mismo código de Dairy Comp (activos o no; sin distinguir mayúsculas). Si hay varios candidatos se toma el más antiguo. Un mismo estado no se adopta para dos estándares. Si no hay candidato se crea; si crear chocara con una unicidad (nombre o código activo tomado por un estado ya adoptado por otro estándar), **se omite ese estándar** como hace hoy el sembrador, sin fallar. Todo en un `SaveChanges`.
- **Consecuencia aceptada de adoptar:** si el "Aborto" creado a mano ya fue usado como estado, las vacas que quedaron con ese estado y los tactos viejos siguen apuntando a él aunque ahora sea `SoloEvento` (se muestran bien; no se pueden crear tactos nuevos con él). La spec lo pide (RF-20) y es raro; queda en los riesgos.

## Estrategia de tests
| RF | Test (proyecto › clase › caso) |
|---|---|
| RF-1 | `Infrastructure.Tests › SembradorEventosTests` (base nueva: 9 estándar, códigos provisorios, tipos); `Domain.Tests › EstadoReproductivoEventosTests` (`Modificar` conserva `Id` y `ClaveEstandar`); `Infrastructure.Tests › SoloEventoTests` (cambiar el código no pierde registros) |
| RF-2, RF-3 | `SembradorEventosTests` (principal y orden de los 6 y de los 3 de "Otro evento"); `EsquemaEventosTests` (CHECK y valores por defecto) |
| RF-4 | `Application.Tests › TrabajoViewModelEventosTests` (crear un evento: queda de tipo estado, no principal, en la lista secundaria, sin tacto guardado); `Infrastructure.Tests › CatalogoEstadosServiceTests` (valores por defecto) |
| RF-5 | `ArmadorListaEventosTests` (renombrar no mueve; desactivar acorta la lista sin reemplazo) |
| RF-6 | `ArmadorListaEventosTests` (principales activos por orden); `TrabajoViewModelEventosTests` (elegir un principal no guarda y habilita Entrar) |
| RF-7 | `Domain.Tests › OrdenAlfabeticoTests` (ANESTRO, ENDOM1, FRESCA, INSEMIN, PROSTAG, SECA; tildes y mayúsculas indistintas; Ñ entre N y O; empate por creación); `ArmadorListaEventosTests` (Rechazo primero) |
| RF-8 | `TrabajoViewModelEventosTests` (nuevo evento solo desde la vista secundaria; nombre y código repetidos se rechazan); `SelectorEventosViewModelTests` (`PermiteNuevo`) |
| RF-9, RF-12 | `SelectorEventosViewModelTests` (resaltado de un principal, de uno de "Otro evento", de Rechazo, y que un desactivado no resalta "Otro evento"); `MovimientosViewModelTests` (edición resalta `EventoId` del registro) |
| RF-10 | A verificar en la tablet (T4/T5); `ArmadorListaEventosTests` cubre la cuenta de 7 botones |
| RF-11 | `ArmadorListaEventosTests` (con todos los principales desactivados: lista vacía y "Otros" con Rechazo) |
| RF-13 | `ArmadorListaEventosTests` (un desactivado no aparece); `ConsultasCampoTests` (nombre en historial, movimientos y ficha de un evento desactivado) |
| RF-14 | `SelectorEventosViewModelTests` (cuenta de toques: principal = 2, de "Otro evento" = 3) |
| RF-15 | `TrabajoViewModelEventosTests` (lectura con la lista abierta, principal o secundaria, queda en cola y se procesa al cerrar) |
| RF-16 | `SoloEventoTests` (OK, Aborto y Revisar Nuevamente: no cambian estado ni último tacto; con observación, usuario, dispositivo, reloj; atomicidad si falla a mitad; rechazo si el evento está inactivo o es un `Estado`); `EsquemaEventosTests` (triggers de inserción) |
| RF-17 | `SoloEventoTests` (deshacer, editar entre estado ↔ solo evento ↔ Rechazo, orden compartido, aviso de repetido, historial con nombre); `TrabajoViewModelEventosTests` (Entrar ramifica por `CambiaEstado`) |
| RF-18 | las pruebas de Rechazo de la 004 siguen en verde (`RechazoTests`) con el comando renombrado |
| RF-19 | `Infrastructure.Tests › MigracionEventosTests` (base con `EsperaYRechazo` + eventos a mano + inicial desactivado + tactos: se actualiza con backup previo, nada se pierde ni se reactiva; falla simulada → base como antes); `SembradorEventosTests` (idempotencia: dos arranques no duplican) |
| RF-20 | `SembradorEventosTests` ("Aborto" a mano se adopta sin cambiar nombre ni código ni `Activo`; adopción por código de Dairy Comp; adopción de uno inactivo no lo reactiva; un estado no se adopta dos veces; el arranque no falla) |
| RF-21 | `Infrastructure.Tests` (los errores del sembrador y de los servicios no llevan caravanas ni nombres); revisión de código (sin red) |
| RF-22 | `SoloEventoTests` (fecha y hora exactas del reloj en el registro, en el nuevo de una edición y conservadas al anular) |
| Caso límite RPRO | `Application.Tests › ValidadorImportacionTests` (`RPRO` = código de un solo evento: entra sin estado y con advertencia; vacío: sin advertencia) |

## Riesgos
- **Migración sobre tablas con triggers:** EF reconstruiría `EstadoReproductivo` o `Rechazo` si se usa `AddColumn` con constraints; por eso el `Up` va a mano. Se prueba con una base real de la versión anterior y con el trigger de inmutabilidad de `Rechazo` recreado (ver "Migración `Mangas`" en `MEMORY.md`).
- **Dependencia de la 004:** si cambia su plan (nombres de comandos, `CorregirRegistroCommand`, el trigger de `Rechazo`), esta spec se ajusta al implementarla. Releer los archivos reales antes de empezar.
- **Códigos provisorios (`OK`, `ABORTO`, `REVISAR NUEVAMENTE`):** si el archivo de vuelta los escribe y Dairy Comp no los entiende, se importaría mal. Esta spec no escribe nada en el Excel; queda anotado en `MEMORY.md` como "pendiente de validar" (constitución §4) y lo resuelve la spec 007.
- **Adopción que cambia el tipo de un estado ya usado** (ver decisiones): raro, aceptado, cubierto por test.
- **Pantalla:** la altura de los 7 botones y la lista de ~25 eventos solo se confirman en la tablet (800x1340 vertical).
