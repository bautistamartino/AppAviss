# Aviss

App Android (tablet) para veterinarios que hacen **tacto reproductivo** en tambos. Funciona 100 % sin conexión: importa un Excel exportado de Dairy Comp, trabaja sobre SQLite leyendo las caravanas con el bastón RFID Tru-Test XRS2, guarda todo el historial y exporta un Excel para volver a Dairy Comp. Pensada para varias tablets, varios veterinarios y varios tambos, para no pagar una licencia de Dairy Comp por tablet.

Requisitos: [docs/requerimientos.md](docs/requerimientos.md) · Arquitectura: [docs/arquitectura.md](docs/arquitectura.md) · Plan por etapas: [docs/plan.md](docs/plan.md) · Principios obligatorios: [docs/constitution.md](docs/constitution.md) · Estado y decisiones: [MEMORY.md](MEMORY.md)

## Sistema de agentes (SDD)

- `/sdd <petición>`: funcionalidades nuevas o cambios de requisitos. El agente principal actúa como **coordinador** (no edita código) y delega en los subagentes de `.claude/agents/`:
  - `planificador`: spec, plan y tareas en `specs/NNN-nombre/`. Solo escribe en `specs/` (hook).
  - `implementador`: una tarea por vez, tests primero.
  - `revisor`: QA de la spec y validación RF por RF. No edita; solo corre tests y comandos de lectura de Git (hook).
- `/feature <cambio>`: cambios chicos sin spec.
- Plantillas y formato EARS: `.claude/skills/sdd/plantillas.md`.
- Nadie hace `git push` ni lee o edita `.env` (`.claude/settings.json`).

## Comandos

Probados el 2026-11-16. Las rutas del JDK 21 y del SDK de Android las pone `Directory.Build.props`.

- `dotnet build`: compila la solución (`Aviss.slnx`), incluida la app Android.
- `dotnet test`: tests de todos los proyectos de `tests/`.
- `dotnet build src/Aviss.App -f net10.0-android -t:Run`: compila, instala y abre la app en el emulador (o dispositivo) conectado.
- `dotnet tool restore` (una vez): instala `dotnet-ef` como herramienta local del repo.
- `dotnet ef migrations add <Nombre> -p src/Aviss.Infrastructure -s src/Aviss.Infrastructure`: nueva migración (confirmado en la spec 001; funciona gracias a `AvissDbContextFactory`). Para ver si quedan cambios sin migrar: `dotnet ef migrations has-pending-model-changes` con los mismos `-p` y `-s`.
- Emulador de tablet: `& "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe" -avd Tablet_Aviss`. `adb` está en `%LOCALAPPDATA%\Android\Sdk\platform-tools`.

## Stack

- .NET MAUI (solo Android, .NET 10), MVVM con CommunityToolkit.Mvvm.
- SQLite con EF Core. Excel con NPOI (el archivo de Dairy Comp es `.xls`).
- Tests: xUnit.
- Estructura (ver [docs/arquitectura.md](docs/arquitectura.md)): `src/Aviss.Domain`, `Aviss.Application`, `Aviss.Infrastructure`, `Aviss.Excel`, `Aviss.Rfid`, `Aviss.App`; `tests/Aviss.*.Tests`.

## Reglas del dominio

- El `ID` de Dairy Comp es el número de caravana y de botón (`NumeroCaravana`). Es único dentro de un rodeo importado.
- El Excel solo se usa para **importar y exportar**; durante el trabajo todo vive en SQLite. El Excel original no se modifica nunca.
- Los tactos y el historial **solo se agregan**: corregir es anular con otro registro, nunca borrar ni sobrescribir.
- Cada tacto se guarda al instante, en una transacción (animal + tacto + historial).
- Dairy Comp crea eventos al importar: el Excel de vuelta debe expresar el tacto como Dairy Comp lo espera (reglas exactas por validar, ver `MEMORY.md`).
- Los estados reproductivos son un catálogo con su código de Dairy Comp (PREÑADA, INSEMIN, SECA, VACIA, FRESCA, NOINSEM, …), no un enum fijo.

## Seguridad y privacidad

- Los Excel reales de tambos **no se commitean**. Los tests usan muestras anonimizadas en `tests/muestras/`.
- La base vive en el almacenamiento privado de la app. No se loguean datos de animales ni de tambos.
- Secretos (keystore de firma, contraseñas) solo fuera del repo; nunca en Git. Se documentan sin valores reales.

## Convenciones

- Nombres del dominio en español (`Animal`, `Rodeo`, `Tacto`, `Establecimiento`); el resto del código técnico puede ir en inglés.
- Una rama por paso del plan (`spec/NNN-nombre`, `fix/...`), commits en español con formato `tipo: descripción` (`feat`, `fix`, `docs`, `chore`, `test`, `refactor`).
- Cada paso termina probado y con commit.

## Forma de trabajar con el usuario

El usuario es estudiante de tecnicatura, sabe C# y este es un proyecto comercial propio: explicar qué se hace y por qué antes de implementar, en español rioplatense, y planificar antes de escribir código.

### Uso eficiente

- **Claude decide y recomienda el camino** antes de empezar cualquier pedido, en una línea:
  - `/sdd` solo para funcionalidades grandes: entidades nuevas, reglas del negocio, cambios en la base o varias pantallas (por ejemplo: importación, trabajo de campo, exportación, lector RFID).
  - `/feature` o trabajo directo para lo chico: textos, estilos, bugs acotados, ajustes en un par de archivos.
- **Máximo 5 tareas por spec**, agrupando lo que va junto. Cada tarea es un subagente que relee el proyecto.
- **Una sesión por spec o tarea grande.** Al terminar: todo commiteado y `MEMORY.md` al día; después se abre una sesión nueva o `/clear`. El contexto vive en `CLAUDE.md`, `MEMORY.md`, `docs/` y `specs/`.
- Los subagentes usan Sonnet. No relanzar revisores en paralelo salvo que el usuario lo pida.
- **La interfaz de la app no se puede verificar en el navegador:** los agentes prueban la lógica y los ViewModels con tests, y la pantalla la prueba el usuario en el emulador o la tablet. Cada tarea visual termina con "qué mirar en el emulador".
