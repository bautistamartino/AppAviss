# Plan por etapas

Cada ítem es una spec o un paso chico. Se marca con `[x]` cuando está terminado, probado y commiteado. Detalle de diseño en [arquitectura.md](arquitectura.md).

## Etapa 0 — Cimientos y validaciones
- [x] 0.1 Entorno: .NET SDK 10, carga de trabajo MAUI, Android SDK y emulador de tablet.
- [ ] 0.2 Prueba manual de reimportación en Dairy Comp (copia sin cambios; una vaca a PREÑADA; una a VACIA). Anotar resultados en `MEMORY.md`.
- [x] 0.3 Solución .NET vacía con los proyectos, `dotnet build` y `dotnet test` en verde, y comandos confirmados en `CLAUDE.md`.

## Etapa 1 — Dominio y base local
- [x] 1.1 Entidades, catálogo de estados y SQLite con migraciones (spec 001): Establecimiento, Rodeo, Animal, Tacto, Historial, Usuario; triggers de inmutabilidad; backups.

## Etapa 2 — Importación
- [x] 2.1 Importar el `.xls` de Dairy Comp con vista previa y errores por fila, y guardar el original (spec 002).

## Etapa 3 — Trabajo de campo
- [x] 3.1 **003 (núcleo) hecho:** flujo de tacto con `FakeRfidReader`: lectura → marca automática → ficha → estado → guardado → siguiente; mangas con cierre y reapertura, búsqueda manual, alta en campo, deshacer, últimos movimientos y avance del rodeo (spec 003). Falta que el usuario lo verifique en el emulador (ver `MEMORY.md`).
- [ ] 3.2 Registros y catálogos de campo (spec 004, **borrador sin aprobar**, depende de la 003): catálogo de tratamientos (Prostal, DIB), detalle del tacto (tratamientos, nota, días de preñez), "solo tratamiento", rechazo con observación y estados nuevos creados en el campo (por ejemplo "aborto" en una vaca preñada).
  - Requisito del usuario que cubre la 004: agregar estados nuevos al catálogo y registrar observaciones o tratamientos (por ejemplo sincronización de celos) durante el tacto.
- Ideas sin spec (anotadas para más adelante): **alertas habladas y reglas de alerta** (aviso por voz y reglas configurables); **historial completo de inseminaciones** (hoy `VINS` solo trae la cantidad).

## Etapa 4 — Exportación
- [ ] 4.1 Exportar el Excel sobre la copia del original, con validación (spec **005**). Depende de 0.2.

## Etapa 5 — Bastón real
- [ ] 5.1 Spike del XRS2 (transporte y trama) y lector real con reconexión (spec **006**). Requiere bastón y tablet.

> **Renumeración pendiente:** la spec 004 (registros y catálogos) todavía es un borrador. Cuando se apruebe como 004, la exportación pasa a ser la **005** y el bastón la **006**; hasta entonces `specs/` no tiene carpetas para esas dos. Las referencias a "spec 004" (exportación) y "spec 005" (bastón) en `MEMORY.md` y en las specs 001 a 003 corresponden a la numeración vieja.

## Etapa 6 — Producto
- [ ] 6.1 Varios veterinarios y tambos pulidos, backups, empaquetado, firma y distribución a las tablets.
