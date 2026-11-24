# Plan — 005 Eventos del catálogo (segunda vuelta)

Spec: [spec.md](spec.md) (aprobada el 2026-11-23). Tareas: [tasks.md](tasks.md). Rama: `spec/005-eventos-del-catalogo`.

## Parte 1 — Primera vuelta (T1 a T5, hecha)

Implementada con los requisitos anteriores; se conserva en el historial de Git. Lo que sigue vigente y esta vuelta usa:
- **"Solo evento" = fila del catálogo con `Tipo = SoloEvento`**, guardada en la tabla `Rechazo` (spec 004), que tiene `EventoId` (nulo = Rechazo fijo). No toca el estado ni el último tacto. OK y Revisar Nuevamente siguen así.
- **Identidad estable:** columna `EstadoReproductivo.ClaveEstandar` (índice único parcial). El `Codigo` interno no cambia nunca.
- **Principal / orden:** columnas `EsPrincipal` y `Orden`; los de "Otro evento" se ordenan por nombre en C# (`OrdenAlfabetico`, Ñ entre N y O).
- **Lista de dos niveles:** `ArmadorListaEventos` (pura) + `SelectorEventosViewModel` + `SelectorEventosView`, usados por la ficha y por Editar de "Últimos movimientos". La lista secundaria es una vista dentro de la capa `Eventos` (no un `CuadroTrabajo` nuevo: RF-15).
- **Migraciones sin reconstruir tablas:** `Up` escrito a mano con `ALTER TABLE ... ADD COLUMN` (como `Mangas`); el Designer y el snapshot quedan como los generó EF.
- **Sembrador con adopción (RF-20)** en un solo `SaveChanges`, idempotente.
- **Rechazo fijo en la aplicación** (`EventoRechazo`), no en el catálogo.
- Tests, archivos y decisiones completas de esa vuelta: ver el historial de este archivo (`git log -- specs/005-eventos-del-catalogo/plan.md`).

Lo que **cambia** en la segunda vuelta: Aborto deja de ser solo evento; entran Preñada fecha correcta / fecha previa; aparecen los pasos posteriores; 7 principales + "Otro evento" (8 botones); "+ Nuevo evento" pasa a "+ Agregar evento".

## Parte 2 — Segunda vuelta (T6 a T10)

### Resumen del diseño
- **Un evento del catálogo declara qué pasos pide** (`Pasos`): Preñada fecha previa → `DiasPreniez`; Aborto → `DiasAborto,ResultadoAborto`. Los recorre un único mecanismo en `SelectorEventosViewModel` (RF-27).
- **Preñada fecha previa** es una fila propia (`PRENADA_FECHA_PREVIA`, código provisorio `PREÑADA FECHA PREVIA`) con `EstadoDestinoId` apuntando a PREÑADA. Al guardar, el `Tacto` lleva `EstadoNuevoId` = PREÑADA, `EventoId` = la fila de fecha previa y `DiasPreniezInformados`. La vaca queda PREÑADA como siempre.
- **Aborto** pasa a `Tipo = Estado` con pasos. Resultado Vacía OK / Inseminada / Seca / Fresca: es un `Tacto` normal con `EstadoNuevoId` = VACIA / INSEMIN / SECA / FRESCA, `EventoId` = Aborto y `DiasAborto`. Resultado Rechazo: **un único registro en `Rechazo`** con `EventoId` = Aborto y `DiasAborto` (no cambia estado ni último tacto). No se guarda una columna de "resultado": se **deriva** (estado del tacto → etiqueta; registro en `Rechazo` con `DiasAborto` → "Rechazo"). "Vacía OK" es solo la etiqueta que se muestra para VACIA dentro de un Aborto.
- **Identidad sin cambios:** `PRENADA` (la clave que ya existe) pasa a ser "Preñada fecha correcta"; la nueva es `PRENADA_FECHA_PREVIA`. Un tacto viejo de PREÑADA (`EventoId` nulo) se ve como fecha correcta.
- **`Tacto.EventoId` nulo = el evento es el propio `EstadoNuevoId`** (todo lo viejo). Con valor = el evento elegido, distinto del estado resultante.

### Base de datos (migración `EventosConPasos`)
Va **después** de `EventosDelCatalogo`. `dotnet ef migrations add EventosConPasos -p src/Aviss.Infrastructure -s src/Aviss.Infrastructure`, y después **reemplazar el `Up` por SQL a mano** (`migrationBuilder.Sql`), sin reconstruir tablas. `Down` vacío. Al final, `dotnet ef migrations has-pending-model-changes` sin cambios (la configuración de EF debe declarar los mismos CHECK y FK que el SQL).

| Tabla | Cambio (todas las columnas nuevas admiten nulo, así las filas viejas quedan válidas) | RF |
|---|---|---|
| `EstadoReproductivo` | `Pasos TEXT NULL CHECK (Pasos IS NULL OR (length(Pasos) > 0 AND Pasos NOT GLOB '*[^A-Za-z,]*'))` (lista separada por comas; el CHECK no fija los valores, así un tercer evento con pasos no pide migración) | RF-27 |
| `EstadoReproductivo` | `EstadoDestinoId TEXT NULL REFERENCES EstadoReproductivo(Id)` + índice | RF-26 |
| `Tacto` | `EventoId TEXT NULL REFERENCES EstadoReproductivo(Id)` + índice `IX_Tacto_EventoId` | RF-24, RF-29 |
| `Tacto` | `DiasPreniezInformados INTEGER NULL CHECK (DiasPreniezInformados IS NULL OR DiasPreniezInformados BETWEEN 1 AND 400)` | RF-23, RF-24 |
| `Tacto` | `DiasAborto INTEGER NULL CHECK (DiasAborto IS NULL OR DiasAborto BETWEEN 0 AND 400)` | RF-28, RF-29 |
| `Tacto` | `CHECK (DiasPreniezInformados IS NULL OR DiasAborto IS NULL)` y, para anulaciones, `Tipo <> 'Normal' → EventoId, DiasPreniezInformados y DiasAborto nulos` (una anulación no copia datos del original, que los conserva) | RF-25, RF-30 |
| `Rechazo` | `DiasAborto INTEGER NULL CHECK (DiasAborto IS NULL OR DiasAborto BETWEEN 0 AND 400)` | RF-30 |

Los rangos 1–400 y 0–400 son provisorios (P9): quedan en **una constante** (`RangosDias`) y repetidos en los CHECK; cambiarlos es una migración chica de CHECK, no un cambio de lógica. Alternativa descartada: no poner CHECK de rango en la base (la constitución §7 pide garantizar las reglas críticas también en la base, y los rangos lo son).

- **Datos (SQL a mano, misma migración):** `UPDATE EstadoReproductivo SET Tipo = 'Estado', Pasos = 'DiasAborto,ResultadoAborto', EsPrincipal = 1, Orden = 6 WHERE ClaveEstandar = 'ABORTO'` y `Orden = 7` para `NOINSEM`. Esto es lo único que se decide con SQL; lo demás (fila nueva, renombre, adopción) lo hace el sembrador (necesita C#). Los registros viejos de Aborto en `Rechazo` quedan con `DiasAborto` nulo y se ven como "Aborto" a secas (RF-31); no se les inventa nada.
- **Triggers a recrear (DROP + CREATE a mano; leer los nombres y el cuerpo reales primero):**
  1. Inmutabilidad de `Tacto` (`TR_Tacto_NoModificar`): si enumera columnas, sumar `EventoId`, `DiasPreniezInformados`, `DiasAborto`.
  2. Inmutabilidad de `Rechazo` (`TR_Rechazo_NoModificar`): sumar `DiasAborto`.
  3. El de inserción de `Rechazo` de T1 ("solo con evento `SoloEvento`"): ahora acepta `EventoId` nulo, un `SoloEvento` (con `DiasAborto` nulo) o el evento con `ClaveEstandar = 'ABORTO'` (con `DiasAborto` obligatorio en inserciones nuevas).
  4. El de inserción de `Tacto` de T1 ("Normal no puede apuntar a un `SoloEvento`"): se conserva y se amplía en un trigger de validez del evento: con `EventoId` no nulo, el evento no puede ser `SoloEvento`; si tiene `EstadoDestinoId`, `EstadoNuevoId` debe ser ese destino; los días solo se aceptan si el evento declara el paso (`Pasos LIKE '%DiasPreniez%'` / `'%DiasAborto%'`) y, en un tacto Normal con `EventoId` de un evento con pasos de días, los días son obligatorios; en un Aborto, `EstadoNuevoId` debe ser un estado con `ClaveEstandar` VACIA, INSEMIN, SECA o FRESCA.
  5. Los triggers de `Historial` y los demás **no** cambian (y ninguna columna de `Historial` se toca: no hace falta `ReconstruirHistorial`).
- **Atomicidad (RF-19):** el arranque ya hace backup previo y restaura si la migración falla (`ArranqueService`). El sembrador agrega la fila nueva y reconcilia **en un solo `SaveChanges`**; si la app se corta entre migración y siembra, el próximo arranque termina (idempotente).

### Dominio
- `Enums`: `PasoEvento { DiasPreniez, DiasAborto, ResultadoAborto }`, `ResultadoAborto { VaciaOk, Inseminada, Seca, Fresca, Rechazo }`.
- `Reglas/RangosDias`: `Preniez = 1..400`, `Aborto = 0..400` (única fuente) y `Reglas/ValidadorDias.Validar(texto, rango)` (pura): solo dígitos (rechaza vacío, `-`, decimales, `1e2`, espacios internos, texto largo), rango; devuelve valor o mensaje. La usan el ViewModel (RF-23, RF-28) y los servicios (nunca confiar en la pantalla).
- `Catalogo/ReglasResultadoAborto`: mapa `ResultadoAborto` → clave estándar (`VACIA`, `INSEMIN`, `SECA`, `FRESCA`; Rechazo sin clave) y etiqueta ("Vacía OK", "Inseminada", "Seca", "Fresca", "Rechazo").
- `EstadoReproductivo` gana `Pasos` (texto; método `ListarPasos()` devuelve `IReadOnlyList<PasoEvento>`) y `EstadoDestinoId`. `CrearEstandar` / `AdoptarComoEstandar` reciben pasos y destino; **no cambian** nombre, código ni `Activo` al adoptar. `Modificar` sigue sin tocar identidad ni pasos.
- `Tacto` gana `EventoId`, `DiasPreniezInformados`, `DiasAborto` (en el método de alta; la anulación no los copia). `Rechazo` gana `DiasAborto`.
- `EstadosIniciales` pasa a **10** (clave → nombre → código → tipo → principal/orden → pasos → destino):

| Clave | Nombre | Código Dairy Comp | Tipo | Principal / orden | Pasos | Destino |
|---|---|---|---|---|---|---|
| `REVISAR_NUEVAMENTE` | Revisar Nuevamente | `REVISAR NUEVAMENTE` (provisorio) | SoloEvento | sí / 1 | — | — |
| `OK` | OK | `OK` (provisorio) | SoloEvento | sí / 2 | — | — |
| `VACIA` | VACIA | `VACIA` | Estado | sí / 3 | — | — |
| `PRENADA` | Preñada fecha correcta | `PREÑADA` | Estado | sí / 4 | — | — |
| `PRENADA_FECHA_PREVIA` | Preñada fecha previa | `PREÑADA FECHA PREVIA` (provisorio) | Estado | sí / 5 | `DiasPreniez` | `PRENADA` |
| `ABORTO` | Aborto | `ABORTO` (provisorio) | Estado | sí / 6 | `DiasAborto,ResultadoAborto` | — |
| `NOINSEM` | NOINSEM | `NOINSEM` | Estado | sí / 7 | — | — |
| `INSEMIN`, `SECA`, `FRESCA` | igual que hoy | igual que hoy | Estado | no | — | — |

### Sembrador (`SembradorInicial`, todo en un `SaveChanges`, idempotente, sin fallar)
1. **Reconciliación de los estándar ya presentes** (por `ClaveEstandar`): `Tipo`, `EsPrincipal`, `Orden`, `Pasos` y `EstadoDestinoId` se igualan a la tabla de arriba. **No** toca `Nombre`, `CodigoDairyComp` ni `Activo` (salvo el renombre del punto 3). Hoy sirve para Aborto y NOINSEM (ya los dejó la migración) y asegura bases donde el estándar se adoptó o creó después.
2. **Fila "Preñada fecha previa":** si no existe, se crea (o se adopta uno creado a mano con ese nombre o con el código `PREÑADA FECHA PREVIA`, RF-20) con `EstadoDestinoId` = Id de PREÑADA (se resuelve por clave `PRENADA`). Si crear chocara con una unicidad, se omite ese estándar sin fallar.
3. **Renombre de PREÑADA a "Preñada fecha correcta":** aunque el nombre se hubiera cambiado a mano (P13). **Se omite sin fallar** si otro evento (activo o no) ya usa ese nombre (comparación de repetidos de la spec 003, sin distinguir mayúsculas). **Supuesto del plan (pregunta Q1):** se aplica **una sola vez por instalación**, en la misma operación que crea la fila de fecha previa; si no, un nombre que el veterinario cambie después se pisaría en cada arranque.
4. Orden de arranque: primero el punto 3 (libera/ocupa el nombre), después el 2.

### Importación (`ValidadorImportacion.IndexarEstadosActivos`)
Ignora los eventos que no son estado inicial: `Tipo = SoloEvento`, los que tienen `EstadoDestinoId` (fecha previa) y los que tienen `Pasos` (cubre Aborto, que ahora es `Estado` pero no es un estado inicial: sin esto un `RPRO = ABORTO` quedaría como estado). Un `RPRO` igual al código de OK, Aborto, Revisar Nuevamente, fecha previa o a "Rechazo" entra **sin estado y con advertencia** (como un código desconocido); `RPRO` vacío, sin advertencia; INSEMIN, SECA y FRESCA se reconocen igual que hoy.

### Servicios de campo (T7)
| Pieza | Cambio | RF |
|---|---|---|
| `EventoCampoDto` | suma `Pasos` (lista de `PasoEvento`), `EstadoDestinoId` y `ClaveEstandar`; `CambiaEstado` es verdadero para fecha previa y Aborto (en Aborto se refina con el resultado, ver `EleccionEvento`) | RF-6, RF-27 |
| `ListaEventosDto` | suma `ResultadosAborto` (los de VACIA, INSEMIN, SECA y FRESCA **activos**, más Rechazo siempre; lo calcula `ArmadorListaEventos` sobre todo el catálogo, también si el estado es de "Otro evento") | RF-28, RF-13 |
| `EleccionEvento(EventoId, DiasPreniez?, DiasAborto?, ResultadoAborto?)` (Application) | lo que queda "en el casillero" tras completar los pasos; `RegistraComoRechazo` (SoloEvento, Rechazo fijo o Aborto con resultado Rechazo) | RF-27, RF-29, RF-30 |
| `RegistrarTactoCommand` / `RegistrarSoloEventoCommand` / `CorregirRegistroCommand` | llevan la `EleccionEvento` (o `EventoId` + datos). La pantalla decide cuál usa por `RegistraComoRechazo`, el servicio **revalida todo** (evento activo, pasos exigidos, días en rango con `ValidadorDias`, resultado permitido y activo, que no sobren datos) | RF-16, RF-24, RF-29, RF-30 |
| `TrabajoCampoService` | **Tacto con evento:** resuelve el estado final (fecha previa → destino PREÑADA; Aborto → estado del resultado por clave) y guarda `Tacto` con `EventoId`, días y `EstadoNuevoId` en la misma transacción que animal e historial. **Aborto con Rechazo:** `RegistrarRechazoNucleoAsync` con `EventoId` Aborto y `DiasAborto`, sin tocar el animal. Fecha y hora exactas del reloj siempre | RF-22, RF-24, RF-29, RF-30 |
| Deshacer / editar (`CorregirRegistroAsync`) | resuelve el evento elegido y escribe en `Tacto` o en `Rechazo` según `RegistraComoRechazo`, en ambas direcciones y atómico; el anulado conserva días y resultado; deshacer un Aborto con cambio de estado restaura el estado anterior como cualquier tacto | RF-25, RF-30 |
| `MovimientoDto` | suma `EventoId` efectivo (= `Tacto.EventoId ?? EstadoNuevoId`; `Rechazo.EventoId` para registros de rechazo), `DiasPreniez`, `DiasAborto`, `ResultadoAborto` (derivado) y la descripción armada | RF-12, RF-25, RF-30 |
| `DescripcionEvento` (Application, pura) | arma "Aborto, 45 días, Vacía OK", "Aborto, 45 días, Rechazo", "Preñada fecha previa, 130 días" ("1 día" en singular; un Aborto viejo sin datos: "Aborto") | RF-25, RF-30, RF-31 |
| `ConsultaCampoService` | historial de la ficha, movimientos y aviso de repetido usan `DescripcionEvento`; el nombre del evento sale de todo el catálogo (activo o no) | RF-13, RF-25, RF-30 |

### Pantallas y ViewModels (T8, T9)
- **`SelectorEventosViewModel` (mecanismo común, RF-27):** estado `PasoActual` (`PasoEvento?`), `EventoEnPasos`, `TextoDias` (cadena escrita), `MensajeError`, `ResultadoElegido`, `EleccionActual` (la ya completa, con su resumen para el casillero), `ResultadosDisponibles`. Comandos: `Elegir(evento)` (sin pasos → completa directo; con pasos → abre el primer paso con los valores actuales si ya estaba elegido, RF-27.6), `ConfirmarDias` (valida con `ValidadorDias`; si falla deja el paso abierto con mensaje; si es el último paso, completa), `ElegirResultado(r)` (completa), `VolverDePaso` (de resultado a días conservando el número; en el primer paso no existe), `Cancelar` (cierra los pasos y deja el casillero como estaba). Nada de esto guarda. El comando `Volver` existente (de "Otro evento" a principales) no se renombra para no romper T3. Se abre siempre en los principales.
- **Toques (RF-14):** el último paso de Aborto es el botón de resultado, que completa sin un "confirmar" extra. Preñada fecha previa: casillero + principal + escribir + Confirmar. Aborto: casillero + principal + escribir + Siguiente + resultado.
- **`TrabajoViewModel`:** `EntrarAsync` guarda con la `EleccionEvento`; el casillero muestra el resumen; con un paso abierto no se puede entrar. Los pasos viven dentro de la capa `Eventos` (RF-15: las lecturas siguen quedando en cola). Resaltado inicial: estado actual de la vaca; si es PREÑADA, **Preñada fecha correcta**. Los textos de "+ Nuevo evento" pasan a "+ Agregar evento" (spec 003 RF-39 conserva sus reglas).
- **`MovimientosViewModel`:** Editar usa el mismo selector con `PermiteNuevo = false`, resalta `item.Dto.EventoId` y **precarga** días y resultado (RF-25, RF-27.5). Un registro con días anulado no se edita (como hoy).
- **`SelectorEventosView`:** lista principal con **8 botones de 60 dp** (8 × 60 + 7 × 8 = 536) y `MaximumHeightRequest` ≈ **560** (hay lugar en 1340; si en la tablet no entra se baja a botones de 56, 504), `ScrollView` con barra visible por si hay más; lista secundaria con "Volver" y "+ Agregar evento" fuera del scroll; **panel de días** (título del evento, `Entry` con teclado numérico, mensaje de error, "Cancelar" y "Confirmar"/"Siguiente", "Volver" desde el paso 2); **panel de resultado** con 5 botones de 60 dp (5 × 60 + 4 × 8 = 332), más "Volver" y "Cancelar". Alto contraste y botones grandes (constitución §9).

### Archivos
- Crear: `src/Aviss.Infrastructure/Migrations/*_EventosConPasos.cs` (+ Designer), `src/Aviss.Domain/Reglas/{RangosDias,ValidadorDias}.cs`, `src/Aviss.Domain/Catalogo/ReglasResultadoAborto.cs`, `src/Aviss.Application/Campo/{EleccionEvento,DescripcionEvento}.cs`; tests nuevos: `Domain.Tests › ValidadorDiasTests, EstadoReproductivoPasosTests`; `Application.Tests › DescripcionEventoTests, SelectorEventosPasosTests, TrabajoViewModelPasosTests`; `Infrastructure.Tests › EsquemaPasosTests, MigracionEventosConPasosTests, SembradorPasosTests, EventosConPasosServiceTests`.
- Modificar: `Domain/Enums/Enums.cs`, `Domain/Entidades/{EstadoReproductivo,Tacto,Rechazo}.cs`, `Domain/Catalogo/EstadosIniciales.cs`, `Infrastructure/Persistencia/Configuraciones/Configuraciones.cs`, `Migrations/AvissDbContextModelSnapshot.cs`, `Arranque/SembradorInicial.cs`, `Servicios/{TrabajoCampoService,ConsultaCampoService,CampoService}.cs`, `Application/Campo/{DtosCampo,ComandosCampo,ArmadorListaEventos}.cs`, `Application/Importacion/ValidadorImportacion.cs`, `App/ViewModels/{SelectorEventosViewModel,TrabajoViewModel.Ficha,EstadosTrabajo,MovimientosViewModel}.cs`, `App/Controles/SelectorEventosView.xaml(.cs)`, `App/Paginas/{TrabajoPage,MovimientosPage}.xaml`, fakes y tests existentes que usan los DTOs o comandos, `tests/Aviss.Infrastructure.Tests/Soporte/BaseDeDatosDePrueba.cs`.
- Documentación (T10): `MEMORY.md`, `docs/arquitectura.md`, `docs/plan.md`, esta spec (casillas).

## Decisiones (segunda vuelta)
- **Aborto con resultado Rechazo se guarda en `Rechazo` (con `EventoId` y `DiasAborto`), no en `Tacto`.** Elegida porque Rechazo no cambia el estado y la tabla ya resuelve eso (RF-30: un único registro). Descartada: un `Tacto` que "no cambia el estado" (obligaría a revisar "último tacto vigente", la restauración al anular y los CHECK de `Tacto`) o dos registros (prohibido por RF-30).
- **El resultado del Aborto no tiene columna propia: se deriva** del estado del tacto o de que sea un registro de `Rechazo`. Elegida porque un dato duplicado puede quedar inconsistente con `EstadoNuevoId`. Descartada: columna `ResultadoAborto` (más CHECK y triggers para mantenerla alineada). Costo: "Vacía OK" es una etiqueta de pantalla que depende de la clave VACIA.
- **Fecha previa y Aborto declaran sus pasos en el catálogo (`Pasos`)**, no en un `if` por nombre en la pantalla (RF-27, constitución §4). Descartada: dos pantallas hechas aparte.
- **`Tacto.EventoId` aparte de `EstadoNuevoId`.** Elegida porque el estado que queda (PREÑADA, VACIA…) y el evento elegido (fecha previa, Aborto) son cosas distintas, y los tactos viejos (`EventoId` nulo) no cambian. Descartada: crear estados "PREÑADA FECHA PREVIA" distintos para el animal (cada consulta de estado actual, la importación y la exportación tendrían que tratarlos como PREÑADA).
- **Claves estándar:** se conserva `PRENADA` para "fecha correcta" (la spec dice "clave `PRENADA`" en RF-26 y `PRENADA_FECHA_CORRECTA` como nombre tentativo); la nueva es `PRENADA_FECHA_PREVIA`. Cambiar la clave existente obligaría a un UPDATE de identidad sin ganancia.
- **La reconciliación de tipo/orden/pasos la hace el sembrador** (y la migración solo lo que se decide con SQL para Aborto y NOINSEM). Consecuencia: el catálogo estándar es del código hasta que haya pantalla de administración; un ajuste manual de principal/orden en la base se pisa en el próximo arranque. Descartada: dejarlo solo en SQL (no cubre las filas que el sembrador crea o adopta después).
- **Último paso de Aborto = tocar un resultado (sin "confirmar" extra)**, para ahorrar un toque (RF-14). Descartada: pedir confirmar después de elegir (un toque más sin ganar seguridad: el casillero igual muestra el resumen y Entrar es lo que guarda).
- **Rangos como constante única más CHECK en la base** (ver arriba).
- **Importación: se ignoran los eventos con `Pasos` o `EstadoDestinoId` además de los `SoloEvento`.** Necesario porque Aborto ya es `Estado`.

## Estrategia de tests (segunda vuelta)
| RF | Test (proyecto › clase › caso) |
|---|---|
| RF-1, RF-2, RF-3, RF-26 | `SembradorPasosTests` (base nueva: 10 estándar activos, provisorios, 7 principales en el orden de RF-3, INSEMIN/SECA/FRESCA no principales y activos; fecha previa con código propio y destino PREÑADA); `EstadoReproductivoPasosTests` (`Modificar` conserva `Id`, clave, pasos) |
| RF-4, RF-8 | `TrabajoViewModelPasosTests` ("+ Agregar evento" solo en la secundaria; el creado queda de tipo estado, no principal, sin pasos) |
| RF-5, RF-11, RF-13 | `ArmadorListaEventosTests` (renombrar no mueve, desactivar no reemplaza, `ResultadosAborto` sin los estados desactivados, Rechazo siempre); `ConsultasCampoTests` (nombre de evento desactivado en lo guardado) |
| RF-6, RF-7, RF-9, RF-10 | `SelectorEventosPasosTests` (8 botones en orden; "Otro evento" con Rechazo, INSEMIN, SECA, FRESCA; resalta "Otro evento" si la vaca está en INSEMIN/SECA/FRESCA; PREÑADA → fecha correcta; fecha previa por `EventoId`); RF-10 en la tablet |
| RF-12 | `MovimientosViewModelTests` (Editar resalta `EventoId` y precarga días y resultado) |
| RF-14 | `SelectorEventosPasosTests` (cuenta de toques: sin pasos 2 o 3; fecha previa y Aborto con sus pasos) |
| RF-15 | `TrabajoViewModelPasosTests` (lectura con un paso abierto queda en cola) |
| RF-16, RF-17 | `EventosConPasosServiceTests` y `SoloEventoTests` (OK y Revisar Nuevamente: sin cambio de estado ni último tacto; Aborto ya no está acá) |
| RF-18 | las pruebas de Rechazo de la 004 siguen en verde |
| RF-19 | `MigracionEventosConPasosTests` (base con `EventosDelCatalogo` + eventos a mano + inicial desactivado + Abortos guardados + tactos: backup previo; nada se pierde ni se reactiva; falla simulada → base como antes; Abortos viejos sin días); `SembradorPasosTests` (dos arranques no duplican) |
| RF-20 | `SembradorPasosTests` ("Preñada fecha previa" y "Aborto" a mano se adoptan sin cambiar nombre, código ni `Activo`; no se adopta dos veces; el arranque no falla) |
| RF-21 | `Infrastructure.Tests` (errores sin caravanas ni nombres); revisión de código (sin red) |
| RF-22 | `EventosConPasosServiceTests` (fecha y hora exactas del reloj en el registro, en el de una edición y conservadas al anular) |
| RF-23 | `ValidadorDiasTests` (1 a 400; rechaza vacío, 0, negativo, decimal, 401, texto); `SelectorEventosPasosTests` (campo abierto se mantiene con mensaje; cancelar deja el casillero como estaba) |
| RF-24 | `EventosConPasosServiceTests` (Entrar con fecha previa: un tacto PREÑADA con `EventoId` y días, atómico; fecha correcta sin días); `EsquemaPasosTests` (CHECK y triggers) |
| RF-25 | `EventosConPasosServiceTests` (editar/anular fecha previa; el anulado conserva días; el nuevo guarda los suyos o ninguno); `DescripcionEventoTests` |
| RF-26 | `SembradorPasosTests` (renombre aunque ya estuviera cambiado a mano; omitido sin fallar si el nombre está tomado; una sola vez); `ValidadorImportacionTests` (fecha previa no es estado inicial) |
| RF-27 | `SelectorEventosPasosTests` (mismo mecanismo para los dos: nada se guarda con un paso abierto, Cancelar, Volver conserva lo escrito, validación, resumen del casillero, reabrir con valores actuales, evento de "Otro evento" con pasos) |
| RF-28 | `SelectorEventosPasosTests` (días 0 a 400, 0 válido; 5 resultados; un estado desactivado no se ofrece; Volver conserva el número) |
| RF-29 | `EventosConPasosServiceTests` (Abortos con Vacía OK, Inseminada, Seca y Fresca: un tacto con `EventoId` Aborto, días y el estado final, fecha de último tacto actualizada, atómico; Vacía OK sobre una vaca ya VACIA; servicio rechaza días fuera de rango y resultado desactivado) |
| RF-30 | `EventosConPasosServiceTests` (Rechazo: **un único** registro en `Rechazo` con días, sin cambiar estado ni último tacto; deshacer restaura el estado anterior; editar de Aborto a otro evento y al revés; historial, movimientos y repetido con la descripción completa) |
| RF-31 | `MigracionEventosConPasosTests` (Abortos viejos se ven "Aborto" a secas, sin estado cambiado ni datos inventados) |
| Caso límite RPRO | `ValidadorImportacionTests` (RPRO = OK, Aborto, Revisar Nuevamente, Rechazo o fecha previa: sin estado y con advertencia; vacío: sin advertencia; INSEMIN/SECA/FRESCA como hoy) |

## Riesgos
- **Código de T1 a T5 no visible al planificar** (ver aviso): los nombres de triggers, comandos y DTOs se confirman leyendo el código.
- **Migración sobre tablas con triggers:** `Tacto` y `Rechazo` tienen triggers de inmutabilidad; si se olvida sumar las columnas nuevas, un UPDATE las modificaría sin que la base lo impida. Test explícito por columna en `EsquemaPasosTests`.
- **Códigos provisorios** (`OK`, `ABORTO`, `REVISAR NUEVAMENTE`, `PREÑADA FECHA PREVIA`) y **exportación del Aborto** (P6, P8): a `MEMORY.md` como "pendiente de validar" (T10).
- **Rangos de días provisorios (P9):** cambiar uno toca la constante y los CHECK (migración chica).
- **Pantalla:** los 8 botones sin desplazar, el teclado numérico y los 5 botones de resultado solo se confirman en la tablet (800x1340 vertical); ~25 eventos en "Otro evento".
- **Descripción del historial:** si `Historial` guarda un texto fijo (no lo deriva), hay que armar la descripción completa al guardar; el implementador lo confirma leyendo cómo se arma hoy.
