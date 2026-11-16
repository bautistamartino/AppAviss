# Plan por etapas

Cada ítem es una spec o un paso chico. Se marca con `[x]` cuando está terminado, probado y commiteado. Detalle de diseño en [arquitectura.md](arquitectura.md).

## Etapa 0 — Cimientos y validaciones
- [x] 0.1 Entorno: .NET SDK 10, carga de trabajo MAUI, Android SDK y emulador de tablet.
- [ ] 0.2 Prueba manual de reimportación en Dairy Comp (copia sin cambios; una vaca a PREÑADA; una a VACIA). Anotar resultados en `MEMORY.md`.
- [ ] 0.3 Solución .NET vacía con los proyectos, `dotnet build` y `dotnet test` en verde, y comandos confirmados en `CLAUDE.md`.

## Etapa 1 — Dominio y base local
- [ ] 1.1 Entidades, catálogo de estados y SQLite con migraciones (spec 001): Establecimiento, Rodeo, Animal, Tacto, Historial, Usuario; triggers de inmutabilidad; backups.

## Etapa 2 — Importación
- [ ] 2.1 Importar el `.xls` de Dairy Comp con vista previa y errores por fila, y guardar el original (spec 002).

## Etapa 3 — Trabajo de campo
- [ ] 3.1 Flujo de tacto con `FakeRfidReader`: lectura → ficha → estado → guardado → siguiente; búsqueda manual, alta en campo, deshacer, avance del rodeo (spec 003).

## Etapa 4 — Exportación
- [ ] 4.1 Exportar el Excel sobre la copia del original, con validación (spec 004). Depende de 0.2.

## Etapa 5 — Bastón real
- [ ] 5.1 Spike del XRS2 (transporte y trama) y lector real con reconexión (spec 005). Requiere bastón y tablet.

## Etapa 6 — Producto
- [ ] 6.1 Varios veterinarios y tambos pulidos, backups, empaquetado, firma y distribución a las tablets.
