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

## Etapa 3 — Trabajo de campo
- [x] 3.1 **003 (núcleo) hecho, probado en una tablet real y ajustado** (spec 003, rama `spec/003-flujo-de-tacto`): lectura → marca automática → ficha estilo Daily (evento + observación, "Entrar") → siguiente; CVET (0 = no entra en tacto), mangas opcionales con cantidad y cierre automático, cabecera contraíble con horarios, movimientos con "Editar", búsqueda manual, deshacer y avance del rodeo. Se retiró el alta en campo. Falta pasar el revisor y mergear a `main` (ver `MEMORY.md`).
- [x] 3.2 **En Espera y Rechazo** (spec 004, rama `spec/004-en-espera-y-rechazo`, implementada y probada con tests; falta verla en la tablet y el revisor; depende de la 003): En Espera = poner una vaca en espera mientras se la tacta y se sigue marcando (sin temporizador; se sale con evento o Saltar; botonera de acceso directo; persistente), y el evento fijo "Rechazo" con el motivo escrito a mano (se edita y se deshace). Los tratamientos (Prostal, DIB), "solo tratamiento", los tres renglones de evento y desactivar estados quedan para una spec posterior.
  - Requisito del usuario que cubre la 004: agregar estados nuevos al catálogo y registrar observaciones o tratamientos (por ejemplo sincronización de celos) durante el tacto.
- Ideas sin spec (anotadas para más adelante): **alertas habladas y reglas de alerta** (aviso por voz y reglas configurables); **historial completo de inseminaciones** (hoy `VINS` solo trae la cantidad; la fecha de la última inseminación habría que exportarla de Dairy Comp); **dos mangas ordeñando a la vez**, a confirmar con el usuario; **varios lotes en un Excel** (¿hay columna de lote?).
- [ ] 3.3 **Eventos del catálogo** (spec **005**, aprobada, sin implementar): OK, Aborto, Revisar Nuevamente y "Otro evento" (ver `specs/005-eventos-del-catalogo/spec.md`).
- [x] 3.4 **Sin veterinario** (spec 006, rama `spec/006-sin-veterinario`): se quitó de punta a punta el veterinario (`Usuario`, `UsuarioId` y derivados) y el identificador de tablet (`DispositivoId`, `dispositivo.id`); la pantalla de inicio de trabajo elige solo establecimiento y rodeo; migración `QuitarVeterinario` que conserva todos los datos. Falta verla en el emulador y pasar el revisor. Se implementa **antes** que la 005 (la 005 parte de la base que deja esta).


## Etapa 4 — Exportación
- [ ] 4.1 Exportar el Excel sobre la copia del original, con validación (spec **007**). Depende de 0.2.

## Etapa 5 — Bastón real
- [ ] 5.1 Spike del XRS2 (transporte y trama) y lector real con reconexión (spec **008**). Requiere bastón y tablet.

> **Renumeración (hecha):** la 004 es En Espera y Rechazo, la 005 eventos del catálogo, la 006 saca al veterinario, la exportación pasa a ser la **007** y el bastón la **008**. Las referencias a "spec 004" (exportación) y "spec 005" (bastón) en `MEMORY.md` y en las specs 001 a 003 corresponden a la numeración vieja.

## Etapa 6 — Producto
- [ ] 6.1 Varios tambos pulidos (sin veterinarios: spec 006), backups, empaquetado, firma y distribución a las tablets.
  - Distribución: APK firmado por sideload para las primeras tablets (keystore fuera del repo y con backup); Google Play con pista privada cuando haya clientes externos.
  - Actualizaciones: subir `ApplicationVersion` (siempre creciente) y firmar con el mismo keystore; instalar encima con `adb install -r`, nunca desinstalar (borra la base). Script de PowerShell que compile, firme e instale en todas las tablets conectadas.
  - Datos: backup automático de la base antes de aplicar una migración de EF Core; probar la migración con una base de la versión anterior; actualizar fuera de un tacto en curso. Decidir si se permite exportar o importar la base entre tablets.
