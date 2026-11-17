# Memoria del proyecto

Estado actual y decisiones importantes. La actualiza el implementador al terminar cada spec, y cualquiera que tome una decisión de diseño. Lo más reciente va arriba en cada sección.

## Estado

- **Etapa 0 — Cimientos** (ver [docs/plan.md](docs/plan.md))
  - Hecho (2026-11-16): esquema de trabajo SDD, constitución, requerimientos, arquitectura y plan.
  - Hecho (2026-11-16): 0.1 entorno. .NET SDK 10.0.404 (winget), carga de trabajo `maui-android`, Android Studio 2026.2.2.9 con SDK en `%LOCALAPPDATA%\Android\Sdk` (imagen `android-37.0 google_apis_playstore x86_64`) y emulador de tablet **`Tablet_Aviss`** (2560x1600, horizontal, 4 GB). Arrancó y `adb` lo ve (`emulator-5554`).
  - Hecho (2026-11-16): 0.3 solución vacía (`Aviss.slnx`): 6 proyectos en `src/`, 5 de tests xUnit en `tests/` con las referencias entre capas de la constitución. `dotnet build` y `dotnet test` en verde y la app MAUI vacía corre en el emulador `Tablet_Aviss` (`com.aviss.app`).
  - Hecho (2026-11-16): 1.1 dominio y base local (spec 001, rama `spec/001-dominio-y-base-local`). Entidades, catálogo de estados, SQLite con migración `Inicial` (inmutabilidad por triggers, índices únicos parciales, CHECK, FK), servicios de tacto/alta/anulación con historial, backups con rotación y arranque seguro (`IArranqueService`, resultado en `IArranqueResultado`). Cableado en `MauiProgram` (carpeta de datos = `FileSystem.AppDataDirectory`; el arranque se espera antes de crear cualquier pantalla). 306 tests en verde. **Falta que el usuario lo verifique en el emulador** (ver `specs/001-dominio-y-base-local/tasks.md`, T5). Sin pantallas todavía.
  - Hecho (2026-11-16): 2.1 importación de Dairy Comp (spec 002, rama `spec/002-importacion-dairy-comp`, T1 a T5). Perfil NPOI, vista previa con errores por fila, copia del `.xls` original en la base, transacción única y pantallas mínimas (`InicioPage`, `ImportarPage`). 526 tests en verde. **Falta que el usuario pruebe las pantallas en el emulador** (ver "Qué mirar en el emulador" en `specs/002-importacion-dairy-comp/tasks.md`, T4) y, cuando pueda, importe un Excel real de un tambo (sin commitearlo).
  - Pendiente: 0.2 prueba de reimportación en Dairy Comp (la hace el usuario).

## Decisiones

- **2026-11-16 — NPOI 2.7.6 (spec 002):** se usa NPOI **2.7.6** (Apache-2.0, sin cuota). El usuario rechazó 2.8.1 por la cuota de OSMF. `NPOI.Core` no sirve: no soporta `net10.0`. Un `.xlsx` renombrado a `.xls` y un archivo con contraseña se rechazan con un motivo controlado (el perfil detecta el formato por contenido y atrapa las excepciones de NPOI). `ImportacionService` corta en 25 MB (`LimitesImportacion.MaxBytes`) leyendo hasta MaxBytes+1 antes de llamar al perfil, y `ValidadorImportacion` rechaza el archivo entero si supera 20.000 filas (`LimitesImportacion.MaxFilas`).
- **2026-11-16 — Importación (spec 002):** el Excel original se guarda byte a byte en `Rodeo.ArchivoOrigenBlob` y nunca se modifica; la importación es una sola transacción (establecimiento nuevo + rodeo + animales con `AgregarVarios`). Se cuentan los animales del rodeo, no se guardan (la spec 001 decidió contarlos). Las filas con problemas se **excluyen** (no "rechazan") con número de fila y motivo. Una vista previa a la vez en memoria (`AlmacenVistasPrevias`). No hubo migración nueva.
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
- **Espacio libre en Android:** resuelto con `StatFs`. `DriveInfo` medía la raíz `/` (partición del sistema, casi llena) y rechazaba la copia con `SinEspacioParaCopia` aunque `/data` tuviera GB libres. Ahora `src/Aviss.App/Platforms/Android/EspacioLibreAndroid.cs` mide con `StatFs` el directorio de backups y se registra en `MauiProgram` antes de `AddAvissInfrastructure` (que usa `TryAdd`). Pendiente confirmar en el emulador.
- **SQLite nativo en Android:** que la app abra la base con el paquete nativo se confirma en el emulador (verificación manual de la spec 001).
- **Flujo de actualización con EF real:** verificado en T3 de la spec 002 con el `MigradorEf` real (crea la base con `Inicial`, la reabre sin pendientes y permite importar). La spec 002 no agregó migración, así que la actualización entre dos migraciones distintas se reverifica cuando llegue la segunda.
- **Riesgos de la spec 002 (importación):**
  - **Licencias y vulnerabilidades de dependencias (revisar en la etapa 6, antes de distribuir la APK):** NPOI 2.7.6 trae transitivamente **NSax (LGPL-3.0)**: revisar si es compatible con la distribución. Hay advertencias NU1902/NU1903 de vulnerabilidad en **ImageSharp 2.1.11**, **System.Security.Cryptography.Pkcs 8.0.1** y **System.Security.Cryptography.Xml 8.0.2**; no se ejecutan al leer celdas, pero hay que revisarlas.
  - **Cuelgues de NPOI con archivos corruptos:** NPOI se cuelga con un `.xls` cortado a mitad de sector; el perfil lo evita completando el último sector sobre una copia. Otros tipos de corrupción no están cubiertos contra cuelgues: evaluar un tiempo máximo de lectura.
  - **NPOI 2.8.1 y SkiaSharp:** su `DataFormatter` necesita SkiaSharp; relevante para la spec 004 si se sube de versión.
  - **Fidelidad de fechas (spec 004):** las fechas se guardan como texto tal como las muestra la celda; verificar que al exportar Dairy Comp las interprete igual.
  - **Copia `APedido` antes de la transacción:** un intento rechazado por una regla también deja una copia en la rotación de 10.
  - **Botón Atrás de Android en plena vista previa:** la vista queda en memoria hasta la próxima importación (no se descarta).
  - **Selector de archivos filtra por MIME** (xls, xlsx, octet-stream): un archivo de texto puede no poder elegirse.
  - **Supuestos de Dairy Comp sin validar:**
    - Códigos de estado de Dairy Comp = nombre del estado (sin validar, prueba 0.2); un código distinto hará que las vacas entren sin estado con advertencia, y se corrige en el catálogo. Los acentos no se normalizan en la comparación de `RPRO`.
    - Caravana sin normalizar: si el Excel real trae ceros inconsistentes, habrá que definir equivalencias. Si el bastón entrega 15 dígitos, la relación con el `ID` se ve en la spec 005.
    - Tamaño de las copias de seguridad: cada importación agrega el `.xls` (hasta 25 MB) a la base y a sus 10 copias; revisar cuánto pesan en uso real.
    - Fechas como texto: fidelidad al exportar por verificar (spec 004).
    - Solo hay una muestra real (226 vacas): supuestos sin validar en otros tambos sobre columnas con el mismo nombre repetido, encabezados siempre en la primera fila con contenido, `ID` único, formato de `FPART` y forma exacta de `Total:N`.
    - NPOI en Android: que lea bien en la tablet se confirma en la verificación manual.
- **Tests de rendimiento** (`Categoria=Rendimiento`) dependen de la PC; se pueden excluir con `--filter "Categoria!=Rendimiento"`.
- **Fecha futura de un tacto (RF-8b):** como la hora sale del mismo reloj que valida, un reloj adelantado y luego corregido aparece como "anterior al último vigente"; el mensaje sugiere revisar la hora.

- Falta saber si Dairy Comp mapea columnas, si acepta `.xls` y `.xlsx`, y qué columnas debe cambiar un tacto para generar el evento correcto (prueba 0.2).
- Falta confirmar con el XRS2: transporte (Bluetooth clásico, supuesto), formato de la trama y relación del número leído con el `ID`.
