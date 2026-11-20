# Plan — 004 En Espera y Rechazo

Spec: [spec.md](spec.md) (aprobada el 2026-11-21). Tareas: [tasks.md](tasks.md). Depende de la spec 003 (ya implementada; ver sus RF-39 a RF-52). Rama: `spec/004-en-espera-y-rechazo` (a crear desde `main` cuando la 003 esté mergeada, o desde `spec/003-flujo-de-tacto` si todavía no).

## Resumen del diseño
- **Dos tablas nuevas:** `Espera` (pendiente de veredicto) y `Rechazo` (registro de campo). Una migración: `EsperaYRechazo`.
- **Rechazo** se parece a `MarcaPasada`: solo se agrega, entra en el **orden de guardado compartido** y en el "último registro vigente", se anula con "Anulado" de "no" a "sí". No toca el estado de la vaca.
- **Espera** NO es registro de campo (no entra en el orden ni en el último vigente). Pasa de `Pendiente` a `Resuelta` o `Salteada` una sola vez.
- **Una sola espera pendiente por vaca** (= por rodeo, porque la vaca es de un rodeo): índice único parcial.
- La ficha y la lectura aprenden un caso nuevo: **vaca en espera** (se abre directo, sin marcar).

## Capa por capa

### Domain
- `Entidades/Espera.cs`: `AnimalId`, `FechaHoraUtc`, `UsuarioId`, `DispositivoId`, `Estado` (`EstadoEspera { Pendiente, Resuelta, Salteada }`), `ResueltaEnUtc`, `ResueltaPorUsuarioId`, `RegistroResolucionId` (Id del tacto o rechazo que la resolvió; nulo si se salteó). Métodos `Crear`, `Resolver(registroId, usuarioId, ahora)`, `Saltear(usuarioId, ahora)`; ambos rechazan si ya no está pendiente.
- `Entidades/Rechazo.cs`: igual a `MarcaPasada` más `Observaciones` (texto opcional recortado, hasta 300 como `Tacto`): `AnimalId`, `FechaHoraUtc`, `OrdenInsercion`, `Observaciones`, `UsuarioId`, `DispositivoId`, `Anulado`, `AnuladoEnUtc`, `AnuladoPorUsuarioId`.
- `Enums`: `EstadoEspera`; `AccionHistorial` suma `EsperaCreada`, `EsperaResuelta`, `EsperaSalteada`, `RechazoCreado`, `RechazoAnulado`.
- `Reglas/ReglasEspera.cs` (pura): `PuedePonerseEnEspera(entraEnTacto, yaEnEspera)` y `PuedeDesmarcarse(enEspera)` (RF-1, RF-9).

### Application
- `Campo/DtosCampo.cs`: `TipoRegistroCampo` suma `Rechazo`; `TipoDeshacer` suma `Rechazo`; `TipoMovimiento` suma `Rechazo`; `TipoResultadoLectura` suma **`EnEspera`** (lleva la ficha y no lleva marca); `FichaAnimalDto` suma `EnEspera` (bool) y `RechazoVigente` (observación o nulo); `EsperaDto` (`EsperaId`, `AnimalId`, `Caravana`, `Cvet`, `DesdeUtc`); `MovimientoDto` ya trae CVET y observación.
- `Campo/ComandosCampo.cs`: `PonerEnEsperaCommand(AnimalId, UsuarioId)`, `SaltearEsperaCommand(AnimalId, UsuarioId)`, `RegistrarRechazoCommand(AnimalId, Observaciones, UsuarioId)`; **`CorregirTactoCommand` se generaliza** a `CorregirRegistroCommand(RegistroId, Evento, Observaciones, UsuarioId)` donde `Evento` es un estado o el **rechazo** (`EventoRechazo`, un Guid fijo conocido en `Application`).
- `Servicios`: `ICampoService` suma `PonerEnEsperaAsync`, `SaltearEsperaAsync`, `ListarEsperasAsync(rodeoId)` y `RegistrarRechazoAsync`; `CorregirTactoAsync` pasa a `CorregirRegistroAsync`.
- `Puertos`: `IEsperaRepository` (agregar, obtener pendiente de un animal, listar pendientes del rodeo, contar), `IRechazoRepository`; `IRegistroCampoRepository.ObtenerUltimoVigenteAsync` y `ContarProcesadasAsync`/`ObtenerHorarioAsync` incluyen rechazos.
- `ConstructorFicha`: suma los rechazos al Historial (anulados marcados) y arma `RechazoVigente`.

### Infrastructure
- Repositorios nuevos; `OrdenCompartido.SiguienteAsync` toma el máximo entre `Tacto`, `MarcaPasada` y `Rechazo` (+1).
- `EsperaService` (scoped): `PonerAsync` (valida CVET y que no esté ya), `ResolverEnLaMismaTransaccionAsync(animalId, registroId, usuarioId)` (lo llaman los servicios de tacto y rechazo dentro de su transacción), `SaltearAsync`, `ListarAsync`.
- `RechazoService`/núcleo en `TrabajoCampoService`: `RegistrarRechazoNucleoAsync` y anulación; reutilizan los núcleos sin transacción propia (patrón de `RegistrarNucleoAsync`/`AnularNucleoAsync` de la 003). `CorregirRegistroAsync` anula el original (tacto o rechazo) y registra el nuevo (tacto o rechazo) con órdenes consecutivos, todo en una transacción.
- `MarcaPasadaService`: la lectura de una vaca **en espera** devuelve `EnEspera` con su ficha y **no marca**; `DeshacerAsync` de una marca rechaza si la vaca está en espera (código nuevo `VacaEnEspera`, mensaje "Está en espera: primero resolvela o saltala").
- `ConsultaCampoService`: movimientos incluyen rechazos (`PuedeDeshacer` y `Editar` por último vigente), ficha con `EnEspera`, `ListarEsperasAsync`.
- `CampoService` (fachada): los métodos nuevos con `EscribirAsync`/`LeerAsync`.

### App (ViewModels)
- `TrabajoViewModel.Espera.cs` (archivo `partial` nuevo): `Esperas` (colección para la botonera), `PonerEnEsperaCommand`, `AbrirEsperaCommand` (abre la ficha con `EsEsperaAbierta`), `CargarEsperasAsync` (se llama al iniciar, tras cada lectura y tras resolver), botón "+N" y lista.
- `Saltar` (`CerrarFichaCommand`) distingue ficha normal de ficha de espera: en la de espera llama a `SaltearEsperaAsync`.
- `EntrarAsync` ramifica por evento: estado → `RegistrarTactoAsync`; `EventoRechazo` → `RegistrarRechazoAsync`. Ambos dejan la espera resuelta en la misma operación (el servicio lo hace).
- `Estados` (lista de eventos) agrega el item fijo **Rechazo** al final, antes de "+ Nuevo evento".
- Lectura `EnEspera`: se abre su ficha sin marcar ni sumar (`ProcesarLecturaAsync`).
- `Terminar el tacto`: la pregunta final suma "Hay N vacas en espera" (`TextoEsperaPendiente`).
- `MovimientosViewModel`: rechazo en la lista; `EditarAsync` ofrece el rechazo entre los eventos y llama a `CorregirRegistroAsync`.

### Pantalla (XAML)
- `TrabajoPage`: botón "En espera" en la barra de abajo de la ficha (entre Saltar y Entrar), botonera de esperas bajo la barra de arriba (oculta en 0), capa de lista de espera, cartel "en espera" en la ficha, franja de rechazo.
- `MovimientosPage`: "Rechazo · motivo" como segundo renglón.

## Base de datos (migración `EsperaYRechazo`)
- Tablas `Espera` y `Rechazo` con sus CHECK (estado válido, `Resuelta` implica fecha y usuario) y FK.
- Triggers de inmutabilidad: `Espera` no se borra ni se actualiza salvo `Pendiente` → `Resuelta`/`Salteada` completando exactamente los campos de resolución, una sola vez; `Rechazo` no se borra ni se actualiza salvo `Anulado` 0 → 1 (con quién y cuándo), y nunca vuelve a 0 (copiar de `MarcaPasada`).
- Índices: único parcial `Espera(AnimalId) WHERE Estado = 'Pendiente'`; único `Rechazo(OrdenInsercion)`; `Rechazo(AnimalId, FechaHoraUtc, OrdenInsercion)`.
- **`Historial`:** el CHECK de `Accion` se reescribe **a mano con `ReconstruirHistorial`** (como en `Mangas`: EF reconstruye las tablas al final y borraría los triggers). Recrear los triggers de `Historial`.
- Comando: `dotnet ef migrations add EsperaYRechazo -p src/Aviss.Infrastructure -s src/Aviss.Infrastructure`; después verificar con `has-pending-model-changes`. Probar el flujo de actualización desde una base con la migración `IndiceTactoFecha`.

## Decisiones técnicas
1. **`EventoRechazo` como Guid fijo** en `Application` en vez de una fila en `EstadoReproductivo`: así no entra al catálogo (spec RF-6), no se exporta como estado ni se desactiva. La lista de eventos del ViewModel lo agrega aparte.
2. **Resolver la espera dentro del servicio de tacto/rechazo** (no desde el ViewModel): garantiza "misma operación indivisible" (RF-3); si el registro se rechaza, la unidad de trabajo revierte y la espera sigue pendiente.
3. **Editar** se generaliza a `CorregirRegistroAsync`; `CorregirTactoAsync` queda como alias hasta limpiar los tests (se borra en T5).
4. **Procesada/horarios:** el rechazo cuenta como registro de la vaca (una vaca con solo rechazo y sin marca sería rara, pero los dos contadores lo incluyen para ser coherentes con "último vigente").
5. **Espera y reseteo de rodeo:** no hace falta código: las esperas cuelgan de animales del rodeo viejo, que queda oculto.
6. **Sin nuevo vencimiento:** ningún código recorre esperas para cerrarlas (RF-4); solo `Resolver` y `Saltear` las cambian.

## Pruebas por requisito
| RF | Dónde se prueba |
|---|---|
| RF-1, RF-2 | `Domain.Tests › ReglasEsperaTests`; `Infrastructure.Tests › EsperaServiceTests` (poner, duplicada, CVET 0 rechazada, listar); `Application.Tests › TrabajoViewModelEsperaTests` (botón, botonera, "+N") |
| RF-3 | `EsperaServiceTests` (resolver con tacto y con rechazo en la misma transacción; tacto rechazado deja pendiente; saltear); VM: Saltar distingue ficha de espera |
| RF-4, RF-9, RF-10 | `EsperaServiceTests` (nada la cierra sola, desmarcar rechazado con `VacaEnEspera`); VM: Terminar muestra el aviso |
| RF-5 | `MarcaPasadaServiceTests` (lectura de vaca en espera → `EnEspera`, sin marca ni suma); VM abre la ficha |
| RF-6, RF-7 | `RechazoTests` (guarda sin cambiar estado ni último tacto, orden compartido, reloj, deshacer, repetido muestra "Rechazo · motivo"); `ConsultasCampoTests` (movimientos) |
| RF-8 | `CorregirRegistroTests` (tacto→rechazo, rechazo→tacto, rechazo→rechazo, atomicidad) |
| RF-11 | `ConstructorFichaTests` (historial con rechazos, vigente más reciente) |
| RF-12 | `HistorialCampoTests` (las cinco acciones nuevas) |
| RF-13 | `EsquemaEsperaRechazoTests` (triggers, único parcial, CHECK) |
| RF-14 | `EsperaServiceTests` (persisten al reabrir la base) |
| RF-15 | VM: lectura con la lista abierta se procesa al cerrarla; vaca en espera abre su ficha |
| RF-16 | revisión de código |
| Rendimiento | `RendimientoCampoTests` (20.000 vacas: poner, listar, resolver bajo 300 ms) |

## Riesgos
- **Migración y `Historial`:** el CHECK reescrito a mano es la parte frágil (ver "Migración `Mangas`" en `MEMORY.md`); hay que verificar triggers recreados con una prueba de base.
- **`TipoRegistroCampo` y `switch`es:** al sumar `Rechazo` hay varios `switch` que asumen dos tipos (`ConsultaCampoService`, `MarcaPasadaService.DeshacerAsync`, `CampoService`); el compilador avisa solo si se quitan los `default`: buscarlos todos.
- **Pantalla sin tablet:** la interfaz se prueba en el emulador; lo que dependa de sonido o de tocar con una mano queda "a verificar en la tablet".
