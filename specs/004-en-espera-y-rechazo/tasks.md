# Tareas — 004 En Espera y Rechazo

Tests primero en cada tarea de lógica. Ninguna tarea se da por terminada con `dotnet build` o `dotnet test` en rojo (cada una deja la solución en verde por sí sola). Una rama: `spec/004-en-espera-y-rechazo`. Detalle de diseño en [plan.md](plan.md). **Máximo 5 tareas** (constitución §10). Orden: la base primero, la pantalla al final.

- [ ] **T1** — Dominio y base: entidades, reglas y migración `EsperaYRechazo`.
  - Qué se hace:
    - `Espera` y `Rechazo`, `EstadoEspera`, las cinco acciones nuevas de `AccionHistorial`, `ReglasEspera`.
    - Configuraciones de EF, `DbSet`s y la migración con el SQL a mano: tablas, CHECK, triggers de inmutabilidad (`Espera` pendiente→resuelta/salteada una vez; `Rechazo` anulado 0→1), índice único parcial de espera pendiente por vaca, índices de `Rechazo`, y **reescritura a mano del CHECK de `Historial` recreando sus triggers** (`ReconstruirHistorial`).
    - Helpers de la base de pruebas para insertar esperas y rechazos por SQL crudo.
  - Archivos: crear `src/Aviss.Domain/Entidades/{Espera,Rechazo}.cs`, `Reglas/ReglasEspera.cs`, `src/Aviss.Infrastructure/Migrations/*_EsperaYRechazo.cs`; modificar `Domain/Enums/Enums.cs`, `Persistencia/Configuraciones/Configuraciones.cs`, `AvissDbContext.cs`, `Migrations/AvissDbContextModelSnapshot.cs`, `tests/Aviss.Infrastructure.Tests/Soporte/BaseDeDatosDePrueba.cs`.
  - RF: RF-6 (entidad), RF-12 (acciones), RF-13 (inmutabilidad y único parcial).
  - Hecho cuando: `dotnet ef migrations has-pending-model-changes` dice que no hay cambios; los tests de esquema comprueban que la base rechaza lo que corresponde aunque el intento no venga de la app (espera: borrar, modificar fuera de pendiente→resuelta/salteada, dos pendientes de la misma vaca; rechazo: borrar, modificar salvo anulado 0→1, volver a 0; historial nuevo no se modifica ni se borra); `ReglasEsperaTests` pasa; una base creada con `IndiceTactoFecha` se actualiza sin perder datos.
  - Qué mirar en el emulador: nada (sin pantallas).

- [ ] **T2** — Servicios de campo: espera, rechazo, corrección generalizada y lectura.
  - Qué se hace:
    - `IEsperaRepository`, `IRechazoRepository`, `EsperaService`; `OrdenCompartido` y `ObtenerUltimoVigenteAsync`/avance/horarios incluyen rechazos.
    - Núcleo de rechazo (registrar y anular) en `TrabajoCampoService`; **resolver la espera dentro de la misma transacción** al guardar un tacto o un rechazo.
    - `CorregirTactoAsync` → `CorregirRegistroAsync` (tacto o rechazo, en ambas direcciones, atómico).
    - `MarcaPasadaService`: lectura de vaca en espera → `EnEspera` sin marcar; desmarcar una vaca en espera rechazado (`VacaEnEspera`).
    - `ConsultaCampoService`: movimientos con rechazos, ficha con `EnEspera` y `RechazoVigente`, `ListarEsperasAsync`; `ConstructorFicha` con el historial de rechazos.
    - `ICampoService` y `CampoService` con los métodos nuevos; fakes de los tests de ViewModels.
  - Archivos: crear `Infrastructure/Servicios/EsperaService.cs`, repositorios; modificar `TrabajoCampoService.cs`, `MarcaPasadaService.cs`, `ConsultaCampoService.cs`, `CampoService.cs`, `OrdenCompartido`, `Repositorios.cs`, `Application/Campo/{DtosCampo,ComandosCampo,ConstructorFicha}.cs`, `Servicios/Servicios.cs`, `Puertos/PuertosCampo.cs`; tests nuevos `EsperaServiceTests`, `RechazoTests`, `CorregirRegistroTests` y ajustes en `CorregirTactoTests`, `ConsultasCampoTests`, `MarcaPasadaServiceTests`, `HistorialCampoTests`.
  - RF: RF-1, RF-3, RF-4, RF-5, RF-6, RF-7, RF-8, RF-9, RF-11, RF-12, RF-14.
  - Hecho cuando: poner en espera rechaza CVET 0 y duplicadas; resolver con tacto y con rechazo deja la espera resuelta **en la misma transacción** y, si el registro se rechaza, queda pendiente; saltear no guarda registros; nada más cierra una espera (ni tiempo, ni día, ni terminar el tacto); el rechazo no cambia el estado ni el último tacto, respeta el orden compartido y la validación de reloj, se deshace y aparece en movimientos y en el aviso de repetido; editar cambia entre tacto y rechazo sin dejar nada a medias; las esperas sobreviven a reabrir la base; el rendimiento de RF-37 se mantiene.
  - Qué mirar en el emulador: nada (sin pantallas).

- [ ] **T3** — ViewModels: En Espera, Rechazo y movimientos.
  - Qué se hace:
    - `TrabajoViewModel.Espera.cs`: colección de esperas, "En espera", abrir una espera (botonera, "+N", lectura de una vaca en espera), Saltar que distingue ficha de espera, aviso en "Terminar el tacto".
    - `EntrarAsync` ramifica por evento (estado o `EventoRechazo`); el item fijo "Rechazo" en la lista de eventos; las lecturas con la lista de espera abierta siguen la regla de la cola.
    - `MovimientosViewModel`: rechazo en la lista y en "Editar".
  - Archivos: crear `src/Aviss.App/ViewModels/TrabajoViewModel.Espera.cs`; modificar `TrabajoViewModel.cs` y `.Ficha.cs`, `EstadosTrabajo.cs`, `MovimientosViewModel.cs`; tests nuevos `TrabajoViewModelEsperaTests` y ajustes en `TrabajoViewModelTests`, `MovimientosViewModelTests`.
  - RF: RF-1, RF-2, RF-3, RF-4, RF-5, RF-6, RF-8, RF-10, RF-15.
  - Hecho cuando: los tests de ViewModel cubren cada caso de los criterios de aceptación de En Espera y Rechazo (botón solo con CVET distinto de 0, botonera con hasta 4 y "+N", abrir sin marcar ni sumar, Entrar resuelve, Saltar de espera saltea y Saltar normal solo cierra, vaca en espera abre su ficha, aviso al terminar, Rechazo con motivo libre, editar entre tacto y rechazo).
  - Qué mirar en el emulador: nada todavía (la pantalla va en T4).

- [ ] **T4** — Pantalla de trabajo: botón, botonera, lista, rechazo.
  - Qué se hace:
    - `TrabajoPage`: botón "En espera" entre Saltar y Entrar; botonera chica con la caravana y el tiempo de cada espera bajo la barra de arriba (oculta en 0); capa de la lista completa ("+N"); cartel "en espera" y franja de rechazo en la ficha; "Rechazo" en la lista de eventos.
    - Todo pensado para vertical y una mano; botones de espera de 40 dp o más, la botonera no tapa los botones de la ficha.
  - Archivos: modificar `src/Aviss.App/Paginas/TrabajoPage.xaml` (y `.xaml.cs` solo si hace falta ciclo de vida).
  - RF: RF-1, RF-2, RF-3, RF-5, RF-6, RF-11, RF-15, RF-16.
  - Hecho cuando: compila y se instala; la pantalla refleja los estados de los ViewModels de T3.
  - **Qué mirar en el emulador (o en la tablet):** (1) poner una vaca en espera y que aparezca su botón con la caravana; (2) leer otras vacas mientras tanto y que las esperas no se muevan; (3) tocar el botón de la espera y ver la ficha con el cartel; (4) Entrar con un evento y que el botón desaparezca; (5) Saltar y que desaparezca sin guardar nada; (6) releer una vaca en espera y que abra su ficha; (7) con más de 4 esperas, el "+N" y la lista; (8) el evento "Rechazo" con el motivo escrito a mano; (9) que los botones se alcancen con una mano.

- [ ] **T5** — Movimientos, historial y cierre documental.
  - Qué se hace:
    - `MovimientosPage`: "Rechazo · motivo" como segundo renglón y Editar con "Rechazo" en la lista.
    - Quitar el alias `CorregirTactoAsync` y limpiar lo que quedó sin uso; suite completa y de rendimiento.
    - Cerrar la documentación: marcar los criterios de aceptación de la spec, `docs/arquitectura.md`, `docs/plan.md` (3.2 hecho), `MEMORY.md` y `CLAUDE.md` si cambian comandos.
  - Archivos: modificar `MovimientosPage.xaml`, servicios y tests que usaban el alias; docs.
  - RF: RF-7, RF-8, RF-12 y todos los criterios de aceptación.
  - Hecho cuando: `dotnet build` y `dotnet test` en verde, incluidos los de rendimiento; spec, plan y `MEMORY.md` al día; pasa el revisor.
  - Qué mirar en el emulador: "Últimos movimientos" con un rechazo (motivo visible, Deshacer y Editar) y cambiar un tacto por un rechazo y al revés con Editar.
