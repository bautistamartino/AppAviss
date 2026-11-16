# Memoria del proyecto

Estado actual y decisiones importantes. La actualiza el implementador al terminar cada spec, y cualquiera que tome una decisión de diseño. Lo más reciente va arriba en cada sección.

## Estado

- **Etapa 0 — Cimientos** (ver [docs/plan.md](docs/plan.md))
  - Hecho (2026-11-16): esquema de trabajo SDD, constitución, requerimientos, arquitectura y plan.
  - Hecho (2026-11-16): 0.1 entorno. .NET SDK 10.0.404 (winget), carga de trabajo `maui-android`, Android Studio 2026.2.2.9 con SDK en `%LOCALAPPDATA%\Android\Sdk` (imagen `android-37.0 google_apis_playstore x86_64`) y emulador de tablet **`Tablet_Aviss`** (2560x1600, horizontal, 4 GB). Arrancó y `adb` lo ve (`emulator-5554`).
  - Hecho (2026-11-16): 0.3 solución vacía (`Aviss.slnx`): 6 proyectos en `src/`, 5 de tests xUnit en `tests/` con las referencias entre capas de la constitución. `dotnet build` y `dotnet test` en verde y la app MAUI vacía corre en el emulador `Tablet_Aviss` (`com.aviss.app`).
  - Hecho (2026-11-16): 1.1 dominio y base local (spec 001, rama `spec/001-dominio-y-base-local`). Entidades, catálogo de estados, SQLite con migración `Inicial` (inmutabilidad por triggers, índices únicos parciales, CHECK, FK), servicios de tacto/alta/anulación con historial, backups con rotación y arranque seguro (`IArranqueService`, resultado en `IArranqueResultado`). Cableado en `MauiProgram` (carpeta de datos = `FileSystem.AppDataDirectory`; el arranque se espera antes de crear cualquier pantalla). 306 tests en verde. **Falta que el usuario lo verifique en el emulador** (ver `specs/001-dominio-y-base-local/tasks.md`, T5). Sin pantallas todavía.
  - Pendiente: 0.2 prueba de reimportación en Dairy Comp (la hace el usuario).

## Decisiones

- **2026-11-16 — Migraciones EF (spec 001):** comando confirmado `dotnet ef migrations add <Nombre> -p src/Aviss.Infrastructure -s src/Aviss.Infrastructure` (herramienta local, `dotnet tool restore`). Anda por `AvissDbContextFactory`; con `-s src/Aviss.App` no se probó. Las garantías (inmutabilidad, unicidad, FK) están en SQL a mano dentro de la migración `Inicial`.
- **2026-11-16 — Reglas de datos (spec 001):** reglas puras en Domain (`ReglasTacto`, `Caravana`); rechazos de negocio como `Resultado` con código, no excepciones; fechas en UTC como texto ISO con `Z`; el arranque nunca muestra rutas ni datos (mensajes fijos en español); ante la duda no se toca la base (dañada o más nueva queda intacta); el arranque se ejecuta bloqueando en `MauiProgram` (con `Task.Run`), antes de las pantallas.

- **2026-11-16 — Plataforma:** .NET MAUI solo Android, SQLite + EF Core, MVVM. Sin backend ASP.NET local (app offline de un usuario por tablet). Kotlin nativo queda como plan B si el lector falla en MAUI.
- **2026-11-16 — Excel con NPOI:** el archivo de Dairy Comp es `.xls` (formato viejo); ClosedXML solo lee `.xlsx`. La exportación parte de una copia del original guardada en la base y cambia solo las celdas necesarias.
- **2026-11-16 — Datos de Dairy Comp:** el `ID` es la caravana y el botón; Dairy Comp cruza por `ID` y crea eventos con historial al importar. Columnas de la muestra: `ID, CVET, FPART, LACT, DEL, FCELO, VINS, UTORO, COM, RPRO, FAPAR`; fechas como texto con espacios; fila final `Total:N`.
- **2026-11-16 — Multi-tambo y multi-veterinario:** cada tablet es independiente (varios establecimientos, veterinario elegido al abrir). Sin sincronización central en la primera versión.
- **2026-11-16 — Sin hardware por ahora:** no hay bastón ni tablet. Se desarrolla con emulador y `FakeRfidReader`; el Bluetooth real se valida al final (etapa 5).

- **2026-11-16 — Emulador:** el AVD `Tablet_Aviss` se creó a mano (no hay `cmdline-tools`/`avdmanager`), copiando la config del AVD `Medium_Phone`. Se lanza con `%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe -avd Tablet_Aviss`. Si en algún momento falla, recrearlo desde Android Studio › Device Manager con el perfil "Pixel Tablet".

- **2026-11-16 — Compilar Android en esta PC:** .NET 10 exige **JDK 21** (el de Android Studio es el 25 y en Windows hay un Java 8: ninguno sirve). Se instaló Microsoft OpenJDK 21 (`C:\Program Files\Microsoft\jdk-21.0.13.21-hotspot`) y la plataforma Android **36** con `dotnet build -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True -p:AndroidInstallJavaDependencies=false` (también bajó `cmdline-tools`, así que ya hay `sdkmanager` y `avdmanager`). Las rutas van en `Directory.Build.props` (solo si existen). La app MAUI es solo Android (`net10.0-android`); se borraron las plataformas iOS, Mac y Windows de la plantilla.

## Riesgos abiertos

- **Supuestos de la spec 001 a validar con datos reales:**
  - El código de Dairy Comp de cada estado es igual al nombre que aparece en el Excel (PREÑADA, INSEMIN, ...); si Dairy Comp usa otros códigos al reimportar, se corrige en el catálogo (el código es editable).
  - La caravana se guarda tal cual, sin normalizar ceros ("0123" y "123" son animales distintos). Si el bastón o Dairy Comp los tratan igual, hará falta una regla de equivalencia en la importación/búsqueda.
  - El identificador electrónico es único por rodeo (entre animales no anulados), no global.
  - "Último tacto" es el de esta tablet: cada tablet es independiente y sin sincronización, así que la validación de orden temporal (RF-8b) solo ve lo que hay en su propia base.
  - Tamaño de las copias de seguridad: crecerá con los `.xls` originales guardados en la base (spec 002); se mantienen 10 copias más la previa a actualización, y el espacio libre se chequea antes de copiar. Revisar cuando se sepa cuánto pesan.
- **Espacio libre en Android:** `DriveInfo` puede comportarse distinto en el emulador/tablet; está detrás de `IEspacioLibreProvider`. Se confirma en la verificación manual.
- **SQLite nativo en Android:** que la app abra la base con el paquete nativo se confirma en el emulador (verificación manual de la spec 001).
- **Flujo de actualización con EF real:** con una sola migración (`Inicial`) solo se probó con un migrador falso; reverificar cuando llegue la segunda migración (spec 002).
- **Tests de rendimiento** (`Categoria=Rendimiento`) dependen de la PC; se pueden excluir con `--filter "Categoria!=Rendimiento"`.
- **Fecha futura de un tacto (RF-8b):** como la hora sale del mismo reloj que valida, un reloj adelantado y luego corregido aparece como "anterior al último vigente"; el mensaje sugiere revisar la hora.

- Falta saber si Dairy Comp mapea columnas, si acepta `.xls` y `.xlsx`, y qué columnas debe cambiar un tacto para generar el evento correcto (prueba 0.2).
- Falta confirmar con el XRS2: transporte (Bluetooth clásico, supuesto), formato de la trama y relación del número leído con el `ID`.
