# Plan por etapas

Cada ítem es una spec o un paso chico. Se marca con `[x]` cuando está terminado, probado y commiteado. Detalle de diseño en [arquitectura.md](arquitectura.md).

## Etapa 0 — Cimientos y validaciones
- [x] 0.1 Entorno: .NET SDK 10, carga de trabajo MAUI, Android SDK y emulador de tablet.
- [ ] 0.2 Prueba manual de reimportación en Dairy Comp (copia sin cambios; una vaca a PREÑADA; una a VACIA). Anotar resultados en `MEMORY.md`.
- [x] 0.3 Solución .NET vacía con los proyectos, `dotnet build` y `dotnet test` en verde, y comandos confirmados en `CLAUDE.md`.

## Etapa 1 — Dominio y base local
- [x] 1.1 Entidades, catálogo de estados y SQLite con migraciones (spec 001): Establecimiento, Rodeo, Animal, Tacto, Historial (y Usuario, que la spec 006 quitó después); triggers de inmutabilidad; backups.

## Etapa 2 — Importación
- [x] 2.1 Importar el `.xls` de Dairy Comp con vista previa y errores por fila, y guardar el original (spec 002).
- [x] 2.2 Guardar el EID de cada animal al importar, buscarlo con el bastón y mostrar "Con EID / Sin EID" en la importación (spec 010; falta mirarla en la tablet).

## Etapa 3 — Trabajo de campo
- [x] 3.1 **003 (núcleo) hecho, probado en una tablet real y ajustado** (spec 003, rama `spec/003-flujo-de-tacto`): lectura → marca automática → ficha estilo Daily (evento + observación, "Entrar") → siguiente; CVET (0 = no entra en tacto), mangas opcionales con cantidad y cierre automático, cabecera contraíble con horarios, movimientos con "Editar", búsqueda manual, deshacer y avance del rodeo. Se retiró el alta en campo. Falta pasar el revisor y mergear a `main` (ver `MEMORY.md`).
- [x] 3.2 **En Espera y Rechazo** (spec 004, rama `spec/004-en-espera-y-rechazo`, implementada, aprobada por el revisor, probada en la tablet y mergeada a `main`; depende de la 003): En Espera = poner una vaca en espera mientras se la tacta y se sigue marcando (sin temporizador; se sale con evento o Saltar; botonera de acceso directo; persistente), y el evento fijo "Rechazo" con el motivo escrito a mano (se edita y se deshace). Los tratamientos (Prostal, DIB), "solo tratamiento", los tres renglones de evento y desactivar estados quedan para una spec posterior.
  - Requisito del usuario que cubre la 004: agregar estados nuevos al catálogo y registrar observaciones o tratamientos (por ejemplo sincronización de celos) durante el tacto.
- Ideas sin spec (anotadas para más adelante): **alertas habladas y reglas de alerta** (aviso por voz y reglas configurables); **historial completo de inseminaciones** (hoy `VINS` solo trae la cantidad; la fecha de la última inseminación habría que exportarla de Dairy Comp); **dos mangas ordeñando a la vez**, a confirmar con el usuario; **varios lotes en un Excel** (¿hay columna de lote?).
- [ ] 3.3 **Eventos del catálogo** (spec **005**, rama `spec/005-eventos-del-catalogo`, implementada T1 a T10 e integrada sobre la 006; falta la prueba en la tablet y el revisor): OK y Revisar Nuevamente como solo registro, Preñada fecha correcta / previa y Aborto con pasos (días y resultado), 8 botones principales y "Otro evento" (ver `specs/005-eventos-del-catalogo/spec.md`). Se desarrolló en paralelo con la 006; su migración va después de `QuitarVeterinario`.
- [x] 3.4 **Sin veterinario** (spec 006, rama `spec/006-sin-veterinario`): se quitó de punta a punta el veterinario (`Usuario`, `UsuarioId` y derivados) y el identificador de tablet (`DispositivoId`, `dispositivo.id`); la pantalla de inicio de trabajo elige solo establecimiento y rodeo; migración `QuitarVeterinario` que conserva todos los datos. Falta verla en el emulador y pasar el revisor. La 005 se integró encima de esta (su migración `EventosDelCatalogo` va después de `QuitarVeterinario`).
- [x] 3.5 **Dos eventos por tacto** (spec 011, rama `spec/011-dos-eventos`, T1 a T5 implementadas el 2026-11-25): un tacto puede llevar un segundo evento (por ejemplo OK + PROSTAG) con su observación, en el mismo registro; migración `DosEventosPorTacto`; columna `Evento2` en la exportación (provisoria). **Falta:** la prueba en la tablet (lista "Qué mirar en la tablet" en `specs/011-dos-eventos/tasks.md`, T5), la prueba 0.2 con Dairy Comp y el revisor.


## Etapa 4 — Exportación
- [ ] 4.1 Exportar el archivo de vuelta sobre la copia del original, con validación (spec **007**, rama `spec/007-exportacion`, implementada T1 a T4 el 2026-11-24; falta mirarla en el emulador o la tablet y pasar el revisor): pantalla "Exportar", CSV o .xls elegido al final, columnas nuevas a la derecha (`SCDAT`, `SCTIM`, `Evento1`, `DIASPRE`, `DIASABO`, `RESABO`), solo la jornada del rodeo, guardado en Descargas y Compartir, constancia en la tabla `Exportacion`. **Pendiente de la prueba 0.2:** que Dairy Comp acepte el archivo con las columnas nuevas y sus nombres reales.

## Etapa 5 — Bastón real
- [ ] 5.1 Spike del XRS2 y lector real con reconexión (spec **008**, rama `spec/008-lector-baston`, implementada T1 a T5 el 2026-11-24; **falta probarla en la tablet con el bastón y pasar el revisor**). Spike de diagnóstico hecho y corrido con el bastón real el 2026-11-24 (Bluetooth clásico SPP, texto `982 000458467499` + CRLF; el socket inseguro conecta al primer intento, el seguro falla seguido; instructivo en [prueba-baston-en-casa.md](prueba-baston-en-casa.md)). Lector real `LectorBastonXrs2` con bastón elegido y recordado por tablet, reconexión automática (esperas 1, 2, 4, 8, 15 s, límite de 8 s por intento), filtro de repetidas de 3 s, chip y cartel de estado en la pantalla de trabajo y pantalla "Elegir el bastón"; sin lectura en segundo plano; Release sin simulador ni Diagnóstico. Valores 3 s, 8 s y esperas provisorios.

> **Renumeración (hecha):** la 004 es En Espera y Rechazo, la 005 eventos del catálogo, la 006 saca al veterinario, la exportación pasa a ser la **007** y el bastón la **008**. Las referencias a "spec 004" (exportación) y "spec 005" (bastón) en `MEMORY.md` y en las specs 001 a 003 corresponden a la numeración vieja.

## Etapa 7 — Aviss de escritorio (idea, 2026-11-24; arranca después de cerrar la tablet)
- [ ] 7.0 Descubrimiento sin código: relevar qué funciones de Dairy Comp usan hoy el padre del responsable y los tambos, y ordenarlas por valor. Resultado en `docs/escritorio/`.
- Objetivo: reemplazar de a poco a Dairy Comp con una app propia, hecha a la medida del negocio de Aviss. Al ser la misma app, la compatibilidad con la tablet queda garantizada (se reemplaza el puente por Excel).
- Se avanza función por función, no todo junto. Primero lo que ya genera la tablet (tactos), después módulos nuevos.
- Consecuencia: la fuente de verdad pasa a ser Aviss, así que hay que decidir cómo viajan los datos entre la PC y las tablets (sincronización) y cómo se migran los tambos desde Dairy Comp (el importador de Excel sirve para eso).
- Más datos y más sensibles que en la tablet: antes de escribir código definir backups, cifrado en disco, accesos y qué se guarda (constitución, sección de seguridad).
- Interfaz candidata: Avalonia (o WPF si es solo Windows); `Domain`, `Application`, `Infrastructure` y `Excel` se reutilizan.

## Etapa 6 — Producto
- [ ] 6.1 Varios tambos pulidos (sin veterinarios: spec 006), backups, empaquetado, firma y distribución a las tablets.
  - Distribución: APK firmado por sideload para las primeras tablets (keystore fuera del repo y con backup); Google Play con pista privada cuando haya clientes externos.
  - Actualizaciones: subir `ApplicationVersion` (siempre creciente) y firmar con el mismo keystore; instalar encima con `adb install -r`, nunca desinstalar (borra la base). Script de PowerShell que compile, firme e instale en todas las tablets conectadas.
  - Datos: backup automático de la base antes de aplicar una migración de EF Core; probar la migración con una base de la versión anterior; actualizar fuera de un tacto en curso. Decidir si se permite exportar o importar la base entre tablets.
