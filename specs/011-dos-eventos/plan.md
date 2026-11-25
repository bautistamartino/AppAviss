# Plan — 011 Dos eventos por tacto

Spec: [spec.md](spec.md) (hoy `borrador`: el plan se empieza a ejecutar cuando pase a `aprobada`; trabaja con la recomendación de cada pregunta abierta)

## Resumen
El Evento 2 se guarda **en el mismo registro** que el Evento 1 (el tacto, o el registro de "solo evento" de la tabla `Rechazo`) con dos columnas nuevas: `Evento2Id` y `Observaciones2`. Así Deshacer, Editar, "último registro vigente", jornada y exportación **no cambian de forma**: sigue habiendo un registro por vaca y por tacto. Una migración escrita a mano agrega las columnas con `ALTER TABLE ... ADD COLUMN` y recrea los triggers. La regla de combinación vive en una función pura del dominio que usan el servicio, la lista de eventos y la pantalla. La exportación suma una columna `Evento2` a la derecha de `Evento1` en el único lugar donde viven las columnas (`ColumnasExportacion`).

## Reglas de combinación (la verdad del plan, `Domain`)
`ReglasSegundoEvento` (pura, sin dependencias):
- `AdmiteSegundo(evento)`: activo, **sin pasos** (`ListarPasos().Count == 0`), **sin `EstadoDestinoId`**, y no es el RECHAZO fijo (que no es una fila del catálogo: lo decide quien llama). Cubre OK, Revisar Nuevamente, Preñada fecha correcta, VACIA, NOINSEM y los creados a mano. **Excluye** Aborto y Preñada fecha previa.
- `PuedeSerSegundo(evento)`: `AdmiteSegundo` **y** (`Tipo == SoloEvento` **o** `ClaveEstandar` nula). Cubre OK, Revisar Nuevamente y los creados a mano. **Excluye** los estados estándar (PREÑADA, VACIA, NOINSEM, INSEMIN, SECA, FRESCA).
- `Validar(evento1OFijo, evento2, ...)`: devuelve el motivo (`SegundoIgualAlPrimero`, `PrimeroNoAdmiteSegundo`, `SegundoNoCombinable`, `SegundoDeBaja`, `ObservacionLarga`) o nada.
Los motivos se traducen a mensajes en español en `MensajesCampo` y a un `CodigoError` ya existente (`DatosInvalidos`, `EstadoDeBaja`) para no tocar el enum de códigos.

## Base de datos

### Entidades
- `Tacto`: `Guid? Evento2Id` y `string? Observaciones2`. `Tacto.Registrar(..., Guid? evento2Id = null, string? observaciones2 = null)` valida: observaciones a 300 (`Validar.TextoOpcional` + tope), `Evento2Id` ≠ `EstadoNuevoId` y ≠ `EventoId`, sin `DiasPreniezInformados` ni `DiasAborto` (un evento con pasos no admite segundo), `observaciones2` solo con `evento2Id`. La anulación (`CrearAnulacion`) **no copia** el Evento 2 (queda nulo, como `EventoId`).
- `Rechazo`: `Guid? Evento2Id` y `string? Observaciones2`. `Rechazo.Registrar(..., Guid? evento2Id = null, string? observaciones2 = null)`: solo con `eventoId` (el RECHAZO fijo no combina), sin `diasAborto` (un Aborto con resultado Rechazo no combina), `Evento2Id` ≠ `EventoId`.
- `AvissDbContext`/configuración: las dos propiedades, FK a `EstadoReproductivo` con `Restrict`, índices `IX_Tacto_Evento2Id` e `IX_Rechazo_Evento2Id` (los usa la consulta de exportación y evita escanear al desactivar eventos).

### Migración `DosEventosPorTacto` (`Up` a mano, `Down` vacío como las anteriores)
Después de la última migración existente (verificar el nombre al empezar: hoy `Exportaciones`). Mismo estilo que `EventosConPasos`: `ALTER TABLE ... ADD COLUMN` (sin reconstruir tablas, para no llevarse triggers ni índices), con el Designer y el snapshot de EF (así `has-pending-model-changes` queda limpio).
1. `ALTER TABLE "Tacto" ADD COLUMN "Evento2Id" TEXT NULL CONSTRAINT "FK_Tacto_EstadoReproductivo_Evento2Id" REFERENCES "EstadoReproductivo" ("Id") ON DELETE RESTRICT;` + índice.
2. `ALTER TABLE "Tacto" ADD COLUMN "Observaciones2" TEXT NULL` con `CONSTRAINT "CK_Tacto_Observaciones2" CHECK (Observaciones2 IS NULL OR (Evento2Id IS NOT NULL AND length(Observaciones2) <= 300))` y, en la misma columna (van **última** porque miran otras), `CK_Tacto_Evento2Distinto` (`Evento2Id IS NULL OR (Evento2Id IS NOT EstadoNuevoId AND Evento2Id IS NOT EventoId)`), `CK_Tacto_Evento2SinDias` (`Evento2Id IS NULL OR (DiasPreniezInformados IS NULL AND DiasAborto IS NULL)`), `CK_Tacto_AnulacionSinEvento2` (`Tipo = 'Normal' OR Evento2Id IS NULL`).
3. `Rechazo`: `Evento2Id` (FK + índice) y `Observaciones2` con `CK_Rechazo_Observaciones2` (mismo criterio), `CK_Rechazo_Evento2ConEvento` (`Evento2Id IS NULL OR (EventoId IS NOT NULL AND DiasAborto IS NULL AND Evento2Id IS NOT EventoId)`).
4. **Triggers de inmutabilidad** (constitución §3 y §7): `DROP TRIGGER IF EXISTS` y `CREATE` de `TR_Tacto_NoModificar` y `TR_Rechazo_NoModificar` con **el mismo texto actual más** `AND OLD.Evento2Id IS NEW.Evento2Id AND OLD.Observaciones2 IS NEW.Observaciones2` (copiar el SQL vigente de `EventosConPasos`, que es el último que los recrea; sin esto, anular podría cambiar el Evento 2).
5. **Triggers de validez del Evento 2 en la inserción**, uno por tabla, porque los CHECK no pueden consultar otra tabla:
   - `TR_Tacto_Evento2Valido BEFORE INSERT ON Tacto WHEN NEW.Evento2Id IS NOT NULL`: aborta si el evento 2 tiene `Pasos` o `EstadoDestinoId`, o si **no** es (`Tipo = 'SoloEvento'` o `ClaveEstandar IS NULL`), o si el evento principal (`EventoId` o, si es nulo, `EstadoNuevoId`) tiene `Pasos` o `EstadoDestinoId`. Es un trigger **aparte** de `TR_Tacto_EventoValido` (no se recrea ese).
   - `TR_Rechazo_Evento2Valido BEFORE INSERT ON Rechazo WHEN NEW.Evento2Id IS NOT NULL`: mismas condiciones sobre el evento 2 y el `EventoId` debe ser de tipo `SoloEvento` (sin pasos).
   - El `TR_Rechazo_SoloEvento` existente (que acepta `SoloEvento` o Aborto con días) no cambia.
6. Sin `UPDATE` de datos: todo lo existente queda con Evento 2 nulo.
Atomicidad y backup: como siempre (EF en una transacción y backup previo del arranque).

### Historial
Sin acciones nuevas (el CHECK `CK_Historial_Accion` no se toca: evita reconstruir `Historial`, que tiene triggers). Un registro con Evento 2 agrega **una segunda fila** con la **misma acción** del registro (`Tacto` o `RechazoCreado`), `Campo = "Evento2"`, `ValorNuevo = nombre del Evento 2`, en la misma transacción. El historial de la ficha ya arma sus líneas desde los registros, no desde esas filas.

## Capas y servicios
| Capa | Qué se agrega | RF |
|---|---|---|
| Domain | `ReglasSegundoEvento` (puras), `Tacto`/`Rechazo` con `Evento2Id` y `Observaciones2`, `RangosDias` sin cambios | RF-1, RF-6, RF-8, RF-10, RF-11, RF-12 |
| Domain/Application | `EleccionEvento(..., SegundoEventoElegido? Segundo = null)` con `public sealed record SegundoEventoElegido(Guid EventoId, string? Observaciones)`; `RegistraComoRechazo` no cambia. La elección ya viaja por `RegistrarTactoCommand`, `RegistrarSoloEventoCommand` y `CorregirRegistroCommand`: **no hay comandos nuevos** | RF-10, RF-16 |
| Application | `EventoCampoDto` suma `bool PuedeSerSegundo` y `bool AdmiteSegundo` (calculados con `ReglasSegundoEvento` en `ArmadorListaEventos.Dto`); el RECHAZO fijo va con ambas en falso y `AdmiteSegundo` en falso. `MovimientoDto` suma `Guid? Evento2Id`, `string? Observaciones2`; `UltimoRegistroDto` no cambia (su `EstadoNombre` ya trae el texto). `DescripcionEvento.Armar(..., string? nombreEvento2)` une con `" + "` | RF-3, RF-16, RF-17 |
| Infrastructure | `TrabajoCampoService`: `RegistrarNucleoAsync` y `RegistrarSoloEventoNucleoAsync` leen `Eleccion.Segundo`, lo validan con `ReglasSegundoEvento` + catálogo (existe, activo, combinable, distinto, evento 1 que admite) y lo pasan a `Tacto.Registrar` / `Rechazo.Registrar`, más la fila de historial extra; el estado y `AplicarTacto` se calculan **solo con el Evento 1** (el Evento 2 nunca los toca). `CorregirRegistroAsync` reenvía `eleccion.Segundo` (el eligir entre tacto y registro sigue por el Evento 1). `ValidarDatosDePasos` rechaza un Evento 2 si el evento 1 tiene pasos. `DescriptorEventos` y `ApoyoCampo`/`ConsultaCampoService`/`MarcaPasadaService` (aviso) arman el texto con el Evento 2 desde el catálogo completo (activo o no) | RF-10 a RF-17 |
| Infrastructure | `ConsultaExportacion` lee `Evento2Id` de tactos y rechazos y arma `NombreEvento2` | RF-19, RF-20 |
| Application (exportación) | `ColumnasExportacion.Evento2` (`"Evento2"`, texto, provisoria) en `Todas` **justo después de `Evento`**; `ValoresExportacion` suma `Evento2` (y `Vacios` y `EnOrden()`); `RegistroExportable` suma `NombreEvento2`; `ArmadorValoresExportacion` lo escribe y deja `DIASPRE`/`DIASABO`/`RESABO` como hoy (siempre vacías con dos eventos porque no se combinan) | RF-19, RF-20 |
| Excel | **Sin cambios de código en los escritores ni en el plan de columnas**: `PlanColumnasExportacion`, `EscritorCsv`, `EscritorXls` y `ValidadorExportacion` recorren `ColumnasExportacion.Todas` y `ValoresExportacion.EnOrden()`; `Evento2` entra como una columna nueva más y su choque (`Choque`) rechaza un original que ya la traiga. Hay que comprobarlo con los tests de los cuatro cruces; si algún escritor tuviera un orden o un tamaño fijo, se corrige ahí | RF-21, RF-22 |
| App (ViewModels) | Ver "Pantallas" | RF-1 a RF-9, RF-16 a RF-18 |

## Pantallas

### Ficha (`TrabajoPage`, `TrabajoViewModel.Ficha.cs`)
- Estado nuevo (en el parcial de la ficha): `Evento2Elegido` (`EventoCampoDto?`), `Observacion2Pendiente`, `HayFila2` (la segunda fila está abierta), `PuedeAgregarEvento2` (= hay Evento 1 elegido que `AdmiteSegundo`, y no `HayFila2`), `TextoEvento2Elegido` ("Tocá para elegir" o el nombre) y `TextoCasilleroObservacion2` ("Opcional").
- Comandos: `AgregarEvento2` (abre la fila, no guarda), `QuitarEvento2` (descarta Evento 2 y su observación), `AbrirEventos2` y `AbrirObservacion2` (los mismos cuadros que hoy, con un indicador de a qué fila pertenecen: `CuadroTrabajo.Eventos` y `CuadroTrabajo.Observacion` se reutilizan con un campo `FilaActiva` (1 o 2), así las capas ya existentes en el XAML sirven para las dos filas y las lecturas se siguen ignorando con cualquier cuadro abierto).
- Selector: **la misma clase** `SelectorEventosViewModel`, en modo "segundo evento" (`ModoSegundo` y `IdExcluido`): filtra `Principales` y `Otros` por `PuedeSerSegundo` y esconde el Evento 1; sin pasos (nunca llegan); "+ Agregar evento" sigue disponible (el evento nuevo es creado a mano, o sea combinable). La lista vacía muestra un texto.
- `ElegirEventoAsync` (Evento 1): si el nuevo Evento 1 no `AdmiteSegundo` o es el mismo que el Evento 2, descarta el Evento 2 y su observación y pone un `MensajeInfo` corto (RF-7). Elegir Evento 1 que sí admite no toca el Evento 2.
- `EntrarAsync`: arma `EleccionEvento` con `Segundo` (evento y observación) y llama a los **mismos** comandos de servicio; el texto de `UltimoGuardado` y del aviso de deshacer es `Resumen(Evento1) + " + " + nombre del Evento 2` (`1000: OK + PROSTAG`). Si el servicio rechaza, queda todo elegido y se muestra el motivo (RF-12).
- `CerrarFichaInterno`, `EnEspera` y `Saltar` limpian también el Evento 2, su observación y la fila (una vaca nunca hereda lo de la anterior).
- XAML: un `HorizontalStackLayout`/`Grid` con "+ evento" como botón de texto chico (alto cómodo para tocar, no destacado), y una segunda fila con los mismos casilleros que la primera, más un "✕ Quitar" chico. La ficha sin fila 2 queda idéntica a hoy.

### Editar de "Últimos movimientos" (`MovimientosPage`, `MovimientosViewModel`)
- `EditarAsync` precarga `EventoEditado2` y `ObservacionEditada2` desde `MovimientoDto.Evento2Id`/`Observaciones2`; la capa de edición ofrece "+ evento"/fila 2 con el mismo selector en modo segundo (regla de RF-7 aplicada igual). `GuardarEdicionAsync` manda el `Segundo` en `CorregirRegistroCommand.Eleccion`. Los textos de movimientos y del aviso de repetido salen del servicio ya unidos con " + ".

### Exportación
Sin pantalla nueva ni cambio en `ExportarViewModel`. Solo el archivo.

## Archivos
- Crear: `src/Aviss.Domain/Reglas/ReglasSegundoEvento.cs`; `src/Aviss.Application/Campo/SegundoEventoElegido.cs` (o dentro de `EleccionEvento.cs`); migración `src/Aviss.Infrastructure/Migrations/<ts>_DosEventosPorTacto.cs` y `.Designer.cs`; tests nuevos (ver estrategia).
- Modificar: `Domain/Entidades/Tacto.cs`, `Rechazo.cs`; `Infrastructure/Persistencia/AvissDbContext` y su configuración, snapshot `AvissDbContextModelSnapshot.cs`; `Application/Campo/EleccionEvento.cs`, `DtosCampo.cs`, `ArmadorListaEventos.cs`, `DescripcionEvento.cs`, `MensajesCampo.cs`; `Infrastructure/Servicios/TrabajoCampoService.cs`, `DescriptorEventos.cs`, `ApoyoCampo.cs`, `ConsultaCampoService.cs`, `MarcaPasadaService.cs`, `ConsultaExportacion.cs`; `Application/Exportacion/ColumnasExportacion.cs`, `ValoresExportacion.cs`, `RegistroExportable.cs`, `ArmadorValoresExportacion.cs`; `App/ViewModels/SelectorEventosViewModel.cs`, `TrabajoViewModel.Ficha.cs` (y los parciales que limpian la ficha), `MovimientosViewModel.cs`, `EstadosTrabajo.cs` si hace falta un campo; `App/Paginas/TrabajoPage.xaml` y `MovimientosPage.xaml`; fakes de `tests/Aviss.Application.Tests/ViewModels/Soporte`.
- Documentos: `docs/arquitectura.md` (sección del tacto y de la exportación), `MEMORY.md`, notas "(modificado por la 011)" en 005 (RF-6, RF-16) y 007 (RF-8, RF-14), y tilde de criterios.

## Decisiones
- **El Evento 2 va como columnas del mismo registro (`Evento2Id`, `Observaciones2`)** — elegida porque un tacto sigue siendo **un registro**: Deshacer lo anula entero, Editar lo reemplaza entero, "último vigente" y la jornada de exportación siguen eligiendo un registro por vaca, y la migración es `ADD COLUMN` sin reconstruir tablas. Alternativa descartada: **un segundo registro** (en `Rechazo`) ligado al primero, porque el segundo pasaría a ser el "último vigente" (Deshacer deshacería solo ese, la exportación elegiría uno solo de los dos y Editar tendría que anular ambos). Alternativa descartada: **tabla hija `EventoDelTacto` con N filas**, porque la relación sería polimórfica (apunta a `Tacto` o a `Rechazo`, sin FK única), pide joins en la exportación y en cada pantalla, y solo hace falta un máximo de dos; si algún día fueran más, se reabre.
- **El Evento 2 nunca cambia el estado y no lleva pasos** — elegida porque evita dos cambios de estado en un tacto (VACIA + Preñada, Aborto + otro), mantiene intactas las reglas de la 005 (un Aborto es un solo registro con sus días y resultado) y hace que `DIASPRE`/`DIASABO`/`RESABO` siempre pertenezcan al Evento 1 (sin ambigüedad al exportar). Alternativa descartada: **cualquier combinación con "el último gana el estado"**, porque el resultado depende del orden de los toques y es difícil de explicar en la manga.
- **Evento 2 solo puede ser OK, Revisar Nuevamente o creado a mano (no los estados estándar)** — elegida porque un estado estándar como Evento 2 (VACIA, NOINSEM) *parecería* aplicarse pero la vaca no cambiaría de estado en Aviss, mientras Dairy Comp sí lo aplicaría: una inconsistencia silenciosa. Alternativa descartada: **ofrecer todos y no aplicar el estado**, por eso mismo.
- **La regla vive en una función pura de dominio y la pantalla recibe banderas en el DTO (`AdmiteSegundo`, `PuedeSerSegundo`)** — elegida porque la regla se escribe y se prueba una sola vez y el ViewModel no consulta el catálogo ni duplica la regla; la base la repite en triggers (constitución §7). Alternativa descartada: **que el ViewModel decida por nombre o por tipo**, porque ya pasó con otros eventos y rompe al renombrar.
- **Historial: una fila más con la misma acción y `Campo = "Evento2"`** — elegida porque no cambia el CHECK de `Accion` (que obligaría a reconstruir `Historial` con sus triggers). Alternativa descartada: **acción nueva `SegundoEventoCreado`**, por el costo y el riesgo de esa reconstrucción para un dato que la propia tabla del registro ya conserva inmutable.
- **La elección viaja en `EleccionEvento.Segundo` y no en parámetros nuevos de los comandos** — elegida porque la elección ya llega a `RegistrarTacto`, `RegistrarSoloEvento` y `CorregirRegistro` y ya se revalida en el servicio; los fakes y los tests de ViewModel cambian poco. Alternativa descartada: **`Evento2Id` y `Observaciones2` en cada comando**, que duplica la firma tres veces.
- **Una sola clase de selector con un "modo segundo"** — elegida porque la lista, el resaltado, "Otro evento" y "+ Agregar evento" ya están probados y se ven igual en las dos filas; los cuadros `Eventos` y `Observacion` se reutilizan con `FilaActiva`. Alternativa descartada: **un selector y un cuadro nuevos** (más XAML y más estados bloqueantes para la cola de lecturas).
- **`Evento2` en la exportación va justo después de `Evento1` y siempre está** — elegida porque queda legible y de forma fija, y los escritores ya son por `PosValores`. Alternativa descartada: **agregarla al final solo si algún tacto la usó**, porque la forma del archivo cambiaría según el día y el validador tendría dos caminos.
- **`Evento2` repetida en el original rechaza la exportación** — elegida por coherencia con `Evento1`, `DIASPRE`, `DIASABO` y `RESABO` (spec 007 RF-14). Alternativa descartada: **reemplazarla como SCDAT y SCTIM**, que solo se justifica con columnas que Dairy Comp siempre exporta.

## Estrategia de tests
Tests primero (constitución §8). Cada tarea corre solo los proyectos que toca (`--filter "Categoria!=Rendimiento"`); la suite completa con rendimiento, una vez, al final de la T5.

| RF | Test (proyecto › clase › caso) |
|---|---|
| RF-1, RF-3, RF-6, RF-8, RF-11 (reglas) | Domain.Tests › `ReglasSegundoEventoTests` (OK, Revisar, estándar de estado, creado a mano de tipo Estado, Aborto, Preñada fecha previa, desactivado, igual al primero, RECHAZO) |
| RF-10, RF-11, RF-13 | Domain.Tests › `TactoSegundoEventoTests` y `RechazoSegundoEventoTests` (guardan, rechazan días, igual, observación larga, anulación sin Evento 2) |
| RF-13, RF-23 | Infrastructure.Tests › `MigracionDosEventosTests` (base con datos de la versión anterior conserva todo; triggers rechazan UPDATE del Evento 2, INSERT incompatible y la anulación que cambia el Evento 2; falla a medias vuelve atrás) y el test del esquema contra el modelo |
| RF-10, RF-11, RF-12, RF-14 | Infrastructure.Tests › `TrabajoCampoDosEventosTests` (tacto con Evento 1 estado + Evento 2: estado y último tacto solo del 1; solo evento OK + creado a mano: no cambia estado; los 7 rechazos del servicio sin guardar nada; historial con 2 filas; una sola transacción) |
| RF-9, RF-12 | Infrastructure.Tests › igual con vaca en espera: queda resuelta; con rechazo del servicio, sigue pendiente |
| RF-15 | Infrastructure.Tests › Deshacer de un registro con dos eventos (tacto y solo evento): anula entero, animal vuelve al estado anterior, conserva los dos |
| RF-16 | Infrastructure.Tests › `CorregirRegistroAsync` con dos eventos (agregar, cambiar y quitar el 2; pasar de tacto a solo evento; si el nuevo se rechaza, el original sigue) |
| RF-17 | Infrastructure.Tests › `ConsultaCampoDosEventosTests` (historial de la ficha, movimientos, aviso de repetido: `OK + PROSTAG`; evento dado de baja; registro de un evento sin cambios) |
| RF-1 a RF-9, RF-17, RF-18 | Application.Tests › `TrabajoViewModelDosEventosTests` (con `FakeCampoService`): "+ evento" visible o no, fila 2, lista filtrada, cambio de Evento 1 que descarta el 2 con aviso, Observación por fila, Quitar, En espera y Saltar limpian, Entrar manda `Segundo`, `UltimoGuardado` `1000: OK + PROSTAG`, rechazo del servicio conserva todo, lecturas ignoradas con la fila o la lista abiertas |
| RF-3 | Application.Tests › `SelectorEventosViewModelSegundoTests` (modo segundo filtra, esconde el Evento 1, lista vacía, "+ Agregar evento" suma un combinable) y `ArmadorListaEventosTests` (banderas del DTO) |
| RF-16 | Application.Tests › `MovimientosViewModelDosEventosTests` (precarga, agregar, quitar, guardar manda `Segundo`) |
| RF-19, RF-20 | Application.Tests › `ArmadorValoresExportacionTests` (Evento2 y vacíos); Infrastructure.Tests › `ConsultaExportacionDosEventosTests` (jornada, anulados, corregidos, rechazos con dos eventos, vacía sin Evento 2) |
| RF-19 a RF-22 | Excel.Tests › exportador y validador: columnas `... Evento1, Evento2, DIASPRE ...`, los cuatro cruces (CSV a CSV con las líneas originales byte por byte, CSV a .xls, .xls a CSV, .xls a .xls), original con `Evento2` rechazado, filas excluidas y `Total` vacías |
| RF-22 | Infrastructure.Tests › `ExportacionRendimientoTests` (Categoria=Rendimiento): 20.000 vacas con la columna más, dentro del tope de la 007 |
| RF-24 | Revisión de mensajes: ningún error ni log lleva caravana ni nombre de tambo (los tests existentes de mensajes se amplían a los códigos nuevos) |
| RF-1, RF-2, RF-16, RF-18, RF-25 (parte visual) | **Solo en la tablet**: tamaño y discreción de "+ evento", que la ficha con la fila 2 y **Entrar** entren sin desplazar, comodidad de la lista y de Editar, bastón real con la fila abierta |

Estimación: de los 25 RF, **tres (RF-1 y RF-2 en lo visual, RF-25) y la parte de pantalla de RF-16 y RF-18 solo se miran en la tablet**; el resto lo cubren tests.

## Riesgos
- **Reconstruir tablas en SQLite por error:** si EF generara una reconstrucción de `Tacto` o `Rechazo`, se llevaría los triggers de inmutabilidad (lección de las migraciones `Mangas` y `QuitarVeterinario`). Mitigación: `Up` a mano con `ADD COLUMN`, test que revisa que los triggers existen tras migrar, y `has-pending-model-changes` limpio.
- **`ADD COLUMN` con varios CHECK que miran otras columnas:** SQLite los comprueba contra las filas existentes (todas con las columnas nuevas en nulo, válidas). Las columnas con CHECK de varias columnas van al final, como hizo `EventosConPasos`.
- **Triggers con subconsultas:** un error en `TR_Tacto_Evento2Valido` podría bloquear tactos comunes; el `WHEN NEW.Evento2Id IS NOT NULL` los deja inactivos para el caso de un evento y hay tests de un evento antes y después.
- **Dairy Comp** puede no aceptar dos columnas de evento ni una columna vacía (prueba 0.2, bloqueada): nombre y posición viven en un solo lugar; anotar en `MEMORY.md` como "pendiente de validar".
- **Eventos creados a mano que cambian el estado como Evento 1** y no como Evento 2: puede sorprender; queda como pregunta 8 y se prueba en la tablet con PROSTAG real.
- **Ficha apretada en vertical:** una fila más puede empujar **Entrar** fuera de la pantalla; se mira en la tablet (800x1340) y, si hace falta, la fila 2 reemplaza un espacio ya existente o se compacta.
- **Tamaño:** entra en 5 tareas, con el cierre dentro de la última.
