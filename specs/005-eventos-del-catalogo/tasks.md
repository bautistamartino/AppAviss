# Tareas — 005 Eventos del catálogo

Tests primero en cada tarea de lógica. Ninguna tarea se da por terminada con `dotnet build` o `dotnet test` en rojo (`dotnet test --filter "Categoria!=Rendimiento"` en las intermedias; la suite completa en T5). Una rama: `spec/005-eventos-del-catalogo`. Diseño en [plan.md](plan.md). **Máximo 5 tareas** (constitución §10). **Se implementa después de la spec 004**: antes de empezar T1, comprobar que existen `Rechazo`, `EventoRechazo`, `CorregirRegistroAsync` y la migración `EsperaYRechazo`; si no, frenar y avisar.

- [ ] **T1** — Dominio, base y siembra: tipo, principal, orden, identidad estándar, migración `EventosDelCatalogo` y adopción.
  - Qué se hace (tests primero):
    - `TipoEvento`; `EstadoReproductivo` con `Tipo`, `EsPrincipal`, `Orden`, `ClaveEstandar` (`CrearEstandar`, `AdoptarComoEstandar`; `Modificar` no toca la identidad); `EstadosIniciales` con los 9 y sus códigos provisorios; `OrdenAlfabetico` (sin tildes, Ñ entre N y O).
    - Configuración de EF y migración `EventosDelCatalogo` con el `Up` escrito **a mano** (`ALTER TABLE ... ADD COLUMN`, sin reconstruir tablas): columnas con CHECK, índice único parcial de `ClaveEstandar`, relleno de los 6 iniciales (clave, principal y orden), `Rechazo.EventoId` con FK e índice, triggers de inserción (`Rechazo` solo con evento `SoloEvento`; `Tacto` Normal no puede apuntar a un `SoloEvento`) y recreación del trigger de inmutabilidad de `Rechazo` si enumera columnas.
    - `SembradorInicial` con adopción (RF-20) en un solo `SaveChanges`, idempotente y sin fallar.
    - `ValidadorImportacion`: un `SoloEvento` no es estado inicial válido (entra sin estado y con advertencia; `RPRO` vacío sin advertencia).
    - Ajustar `BaseDeDatosDePrueba` para armar una base de la versión anterior (con `EsperaYRechazo`) con eventos a mano.
  - Archivos: crear `Domain/Enums/TipoEvento.cs` (o en `Enums.cs`), `Domain/Reglas/OrdenAlfabetico.cs`, `Infrastructure/Migrations/*_EventosDelCatalogo.cs`; modificar `Domain/Entidades/EstadoReproductivo.cs`, `Domain/Catalogo/EstadosIniciales.cs`, `Persistencia/Configuraciones/Configuraciones.cs`, `Migrations/AvissDbContextModelSnapshot.cs`, `Arranque/SembradorInicial.cs`, `Application/Importacion/ValidadorImportacion.cs`, `tests/Aviss.Infrastructure.Tests/Soporte/BaseDeDatosDePrueba.cs`; tests nuevos `OrdenAlfabeticoTests`, `EstadoReproductivoEventosTests`, `EsquemaEventosTests`, `SembradorEventosTests`, `MigracionEventosTests` y ajustes en `ValidadorImportacionTests`.
  - RF: RF-1, RF-2, RF-3, RF-5, RF-19, RF-20, RF-21 y el caso límite RPRO.
  - Hecho cuando: `dotnet ef migrations has-pending-model-changes` dice que no hay cambios; una base nueva tiene los 9 eventos con tipo, principal y orden de RF-3; una base anterior con un evento a mano, un inicial desactivado y un "Aborto" a mano se actualiza con backup previo, adopta el "Aborto" sin cambiarle nombre, código ni estado, no reactiva nada y deja los demás en "Otro evento"; una migración que falla deja la base como estaba; dos arranques seguidos no duplican nada; la base rechaza (aunque no venga de la app) un `Rechazo` con un evento que no es `SoloEvento`, un tacto Normal hacia un `SoloEvento` y un cambio de `EventoId` en un registro existente; `OrdenAlfabeticoTests` pasa con los ejemplos de RF-7.
  - Qué mirar en el emulador: nada (sin pantallas).

- [ ] **T2** — Servicios de campo: lista de eventos, guardado de solo-evento, edición y consulta.
  - Qué se hace (tests primero):
    - `ArmadorListaEventos` (pura) y `EventoCampoDto` / `ListaEventosDto`; `ICampoService.ListarEventosAsync` reemplaza a `ListarEstadosActivosAsync` y `EstadoActivoDto`.
    - `RegistrarRechazoCommand` / `RegistrarRechazoAsync` de la 004 pasan a `RegistrarSoloEventoCommand(AnimalId, EventoId, Observaciones)` / `RegistrarSoloEventoAsync`: `EventoRechazo` guarda `Rechazo.EventoId` nulo; un evento del catálogo guarda su `EventoId`; se valida que exista, esté activo y sea `SoloEvento`. El guardado de tacto rechaza un `SoloEvento`.
    - `CorregirRegistroAsync` resuelve el tipo del evento elegido (estado → tacto; solo evento o Rechazo → registro), en ambas direcciones y atómico.
    - Movimientos con `EventoId`; historial, ficha y movimientos muestran el nombre del evento aunque esté desactivado; la fecha y hora exactas se conservan, también al anular.
    - Renombrar usos en tests, fakes y `CampoService` (el compilador avisa).
  - Archivos: crear `Application/Campo/ArmadorListaEventos.cs`; modificar `Application/Campo/{DtosCampo,ComandosCampo}.cs`, `Application/Servicios/Servicios.cs`, `Infrastructure/Servicios/{TrabajoCampoService,ConsultaCampoService,CampoService}.cs`; tests nuevos `ArmadorListaEventosTests`, `SoloEventoTests` y ajustes en `RechazoTests`, `CorregirRegistroTests`, `ConsultasCampoTests`, fakes de los tests de ViewModels.
  - RF: RF-4, RF-5, RF-6, RF-7, RF-11, RF-13, RF-16, RF-17, RF-18, RF-22.
  - Hecho cuando: OK, Aborto y Revisar Nuevamente se guardan con observación y fecha y hora del reloj sin cambiar el estado ni el último tacto de la vaca, y quedan en el historial, en movimientos y en el aviso de repetido; se deshacen y se editan (estado ↔ solo evento ↔ Rechazo) sin dejar nada a medias; el armador devuelve principales activos en su orden y "Otros" con Rechazo primero y el resto alfabético; un evento desactivado no aparece en las listas pero sí en lo guardado; renombrar un principal no lo mueve y desactivarlo no lo reemplaza; las pruebas de Rechazo de la 004 siguen en verde.
  - Qué mirar en el emulador: nada (sin pantallas).

- [ ] **T3** — ViewModels: selector de eventos compartido, ficha y edición de movimientos.
  - Qué se hace (tests primero):
    - `SelectorEventosViewModel`: principales, "Otro evento", vista secundaria, "Volver", resaltado (incluido que un desactivado no resalta "Otro evento"), `PermiteNuevo`; se abre siempre en los principales.
    - `TrabajoViewModel` (`.Ficha`, `EstadosTrabajo`): la capa de eventos usa el selector; `ElegirEventoAsync` no guarda; `EntrarAsync` ramifica por `CambiaEstado` (estado → tacto; si no → `RegistrarSoloEventoAsync`); "+ Nuevo evento" solo desde la vista secundaria y sin elegir lo creado; resaltado inicial = estado actual de la vaca; lecturas con la lista abierta (principal o secundaria) quedan en cola.
    - `MovimientosViewModel`: edición con el mismo selector resaltando el `EventoId` del registro; vale para tacto, solo evento y Rechazo.
  - Archivos: crear `App/ViewModels/SelectorEventosViewModel.cs`; modificar `TrabajoViewModel.Ficha.cs`, `EstadosTrabajo.cs`, `MovimientosViewModel.cs`; tests nuevos `SelectorEventosViewModelTests`, `TrabajoViewModelEventosTests` y ajustes en `TrabajoViewModelTests`, `MovimientosViewModelTests`.
  - RF: RF-4, RF-6, RF-7, RF-8, RF-9, RF-11, RF-12, RF-14, RF-15, RF-17.
  - Hecho cuando: los tests de ViewModel cubren cada criterio de aceptación de la lista (elegir un principal no guarda y habilita Entrar; "Otro evento" muestra Rechazo primero y "Volver"; resaltado de un principal, de uno de "Otro evento" y de Rechazo, en la ficha y en Editar; cualquier evento activo a 3 toques como máximo; crear un evento lo deja en "Otro evento" sin elegirlo; Entrar con OK guarda un solo evento y con VACIA guarda un tacto; todos los principales desactivados deja solo "Otro evento").
  - Qué mirar en el emulador: nada todavía (la pantalla va en T4).

- [ ] **T4** — Pantalla: selector de eventos en la ficha y en "Últimos movimientos".
  - Qué se hace:
    - `SelectorEventosView` (`ContentView`) con los principales (botones de 60 dp, 7 sin desplazar en 800x1340 vertical, `ScrollView` con barra siempre visible si hay más), "Otro evento", la vista secundaria con lista desplazable, "+ Nuevo evento" y "Volver" siempre visibles, y el resaltado.
    - `TrabajoPage`: la capa de eventos usa el selector y mantiene el formulario de evento nuevo. `MovimientosPage`: la edición usa el mismo selector.
  - Archivos: crear `App/Controles/SelectorEventosView.xaml(.cs)`; modificar `Paginas/TrabajoPage.xaml`, `Paginas/MovimientosPage.xaml`.
  - RF: RF-6, RF-7, RF-8, RF-9, RF-10, RF-11, RF-12, RF-14.
  - Hecho cuando: compila (`dotnet build src/Aviss.App -f net10.0-android`), se instala y la pantalla refleja los estados de los ViewModels de T3.
  - **Qué mirar en el emulador (o en la tablet, vertical 800x1340):** (1) abrir una ficha y tocar el casillero Evento: se ven los 6 principales en el orden Revisar Nuevamente, OK, VACIA, PREÑADA, Aborto, NOINSEM y "Otro evento", sin desplazar; (2) elegir OK y que quede en el casillero y se habilite Entrar sin guardar; (3) "Otro evento": Rechazo primero y el resto alfabético, con "Volver" y "+ Nuevo evento" fijos; (4) crear un evento con tilde o Ñ y ver dónde queda ordenado; (5) el resaltado: abrir una vaca con estado INSEMIN y ver "Otro evento" resaltado y, adentro, INSEMIN; con VACIA, su botón principal; (6) Entrar con OK y mirar la vaca en "Últimos movimientos" (sin cambio de estado), probar Editar con el mismo selector y Deshacer; (7) con un par de eventos desactivados no se ven en ninguna lista; (8) cualquier evento a 3 toques como máximo con una mano.

- [ ] **T5** — Verificación final, documentación y cierre.
  - **Orden de implementación:** la spec 006 (sin veterinario) se implementa **antes** que esta; la 005 parte de la base que la 006 deja (sin `Usuario`, `UsuarioId` ni `DispositivoId`). La rama de la 005 deberá rebasearse sobre ella.
  - Qué se hace:
    - Suite completa (`dotnet build`, `dotnet test` incluidos los de rendimiento) y prueba en la tablet con ~25 eventos en "Otro evento": se recorre con la barra visible y "+ Nuevo evento" fijo (RF-10, RF-14).
    - Verificar la numeración (la renumeró la spec 006: **005 esta spec, 007 exportación, 008 bastón**): que no quede "exportación 006" ni "bastón 007" (ni "exportación 005" o "bastón 006") en `docs/`, `MEMORY.md` ni en las specs 004 y 005, y que `docs/plan.md` tenga el paso de la 005 en la etapa 3 (ya está). Esta spec no debe mencionar veterinario registrado ni dispositivo: se implementa **después** de la 006.
    - `MEMORY.md`: sección **"Códigos pendientes de validar"** con `OK`, `ABORTO` y `REVISAR NUEVAMENTE` como provisorios (cómo corregirlos: migración de datos solo si el código vale todavía el provisorio; ver plan), las preguntas P1, P2, P3 y P5 de la spec, el estado de la 005 y la decisión "solo evento = registro en `Rechazo` con `EventoId`" (el nombre de la tabla quedó pobre a propósito).
    - `docs/arquitectura.md`: `EstadoReproductivo` con `Tipo`, `EsPrincipal`, `Orden`, `ClaveEstandar`; `Rechazo.EventoId`; la lista de dos niveles en la sección 10.
    - Marcar los criterios de aceptación de la spec y completar "a verificar en la tablet" con lo observado. `CLAUDE.md` solo si cambia algún comando.
  - Archivos: modificar `docs/plan.md`, `MEMORY.md`, `docs/arquitectura.md`, `specs/004-en-espera-y-rechazo/spec.md`, `specs/005-eventos-del-catalogo/spec.md`; tests solo si aparece algo roto.
  - RF: RF-10, RF-14 (tablet) y todos los criterios de aceptación.
  - Hecho cuando: `dotnet build` y `dotnet test` en verde (también rendimiento); no queda "exportación 006" ni "bastón 007" (ni los números aún más viejos) en `docs/`, `MEMORY.md` ni en las specs 004 y 005, y la 005 no menciona veterinario registrado ni dispositivo; `MEMORY.md` tiene los códigos provisorios y la decisión; pasa el revisor.
  - Qué mirar en el emulador: crear ~25 eventos a mano y recorrer la lista secundaria; releer una base vieja (instalar encima de una versión anterior) y comprobar que los eventos a mano siguen y los 3 nuevos aparecen.
