# Memoria del proyecto

Estado actual y decisiones importantes. La actualiza el implementador al terminar cada spec, y cualquiera que tome una decisión de diseño. Lo más reciente va arriba en cada sección.

## Estado

Última actualización: 2026-11-21. Sin tablet por ahora (el usuario ya no la tiene para probar): lo que sigue es ordenar y especificar, no programar interfaz.

- **Etapa 0 — Cimientos: hecha** (2026-11-16). Esquema SDD, constitución, requerimientos, arquitectura y plan; entorno (.NET 10, MAUI Android, JDK 21, emulador `Tablet_Aviss`); solución con 6 proyectos en `src/` y 5 de tests.
- **Spec 001 — Dominio y base local: hecha** (rama `spec/001-dominio-y-base-local`). Entidades, catálogo de estados, SQLite con migraciones, inmutabilidad por triggers, servicios de tacto con historial, backups y arranque seguro.
- **Spec 002 — Importación de Dairy Comp: hecha** (rama `spec/002-importacion-dairy-comp`). Perfil NPOI, vista previa con errores por fila, copia del `.xls` original en la base, transacción única. Desde el 2026-11-19 también lee los `.xls` que NPOI no sabe abrir (ver decisiones).
- **Spec 003 — Flujo de tacto: implementada, probada en una tablet real y ajustada** (rama `spec/003-flujo-de-tacto`, **subida a GitHub** hasta el commit `fd68471`). Los cambios posteriores a la prueba en tablet están consolidados en la spec (`specs/003-flujo-de-tacto/spec.md`, sección "Ajustes tras las pruebas en la tablet", RF-39 a RF-52). **Falta pasar el revisor y mergear a `main`.**
- **Tests (2026-11-21):** Application 376, Domain 178, Excel 58 (el de muestras ya no falla), Infrastructure 462 (sin los de rendimiento, que dependen de la PC).
- **Cómo retomar en otra PC:** [docs/puesta-en-marcha.md](docs/puesta-en-marcha.md).

## Pendientes (en este orden)

1. **Pasar el revisor** sobre la spec 003 (una sola vez; la spec ya está ordenada) y **mergear a `main`**.
2. **Probar en tablet** cuando vuelva a haber una lo que no se pudo ver: la chicharra del CVET 0, el aviso de manga completa, el cierre automático, "+ Nuevo evento", la pantalla de configuración y el engranaje (de un solo toque; si confunde, pasa a dos).
3. **Decisiones que el usuario confirma** (detalle en "Preguntas abiertas" de la spec 003): si hace falta registrar qué veterinario hace el tacto (se confirma el 2026-11-21 con su padre), dos veterinarios por tacto, dos mangas a la vez, varios lotes en un Excel.
4. **Spec 004** (`specs/004-registros-y-catalogos`, reescrita el 2026-11-21, **sin aprobar**): **En Espera** y **Rechazo**. Decididos con el usuario: rechazo = un evento más de la lista; una vaca en espera que se relee abre su ficha directo; la lista de espera se guarda en la base. Resuelto con el usuario el 2026-11-21: En Espera **sin temporizador ni vencimiento** (se sale solo con Entrar+evento o con Saltar; desmarcar se rechaza), con botonera de acceso directo (2 o 3 a la vez); Rechazo = evento de la lista con el motivo escrito a mano (sin atajos). **Sin preguntas abiertas**: falta que el usuario la apruebe;
5. **Sacar el botón "↺ A 0"** (resetear rodeo) cuando el usuario termine de probar; cómo, en RF-50 de la spec.
6. **Prueba 0.2** de reimportación en Dairy Comp (la hace el usuario): qué columnas debe cambiar un tacto, códigos de estado, y si el código de Dairy Comp de un evento creado a mano coincide.
7. **Pendientes técnicos chicos:** las ~15 advertencias CS0618 de EF Core (`HasCheckConstraint` obsoleto: cambiarlo puede alterar el modelo de las migraciones, hacerlo con una migración de prueba); advertencias NU1902/NU1903 de dependencias de NPOI (revisar en la etapa 6, antes de distribuir); fecha de la última inseminación (si se exporta de Dairy Comp, sumarla al historial por su nombre de columna).

## Decisiones

### Prueba en tablet real y ajustes (2026-11-19 a 2026-11-21)
Samsung Galaxy Tab A9 (SM-X110, Android 16, arm64, 800x1340 **en vertical**), instalada por USB con `dotnet build src/Aviss.App -f net10.0-android -t:Run`. Cada punto está explicado en la spec 003; acá solo el porqué y dónde mirar.

- **Interfaz en vertical.** La tablet se usa así: todas las pantallas se rehicieron en una columna (elección con pasos numerados, trabajo con cabecera compacta, capas apiladas, movimientos en filas compactas).
- **Se quitó el alta en campo** (RF-25 y RF-29): los animales nuevos ya vienen en el Excel y los números se corrigen en el bastón; el cruce es **por número (caravana)**, el EID no está en el Excel. Se conservó el alta a nivel animal de la spec 001 (sin pantalla). **Por confirmar con el bastón real:** qué manda el XRS2 después de renombrar el número (el número o el EID); la búsqueda prueba caravana y después EID.
- **Ficha estilo Daily** (RF-39): se elige el evento y se guarda con **Entrar**; Saltar cierra sin guardar; pestañas Datos e Historial; Datos trae **todas** las columnas del Excel (el importador ya las guarda; DCC, DDUC y LECHE aparecen cuando se exportan de Dairy Comp). Del Daily no se hacen "Ver" ni "Control". Evento nuevo desde la lista (`CodigoEstado.DesdeNombre`).
- **CVET** (RF-40, RF-41): 0 = no entra en tacto → chicharra, queda marcada y suma, sin ficha; "Ver ficha" bajo el "Último". Regla en `ReglasCvet`. En el Excel real: 175 vacas con CVET 0 y 51 con código. El avance sigue contando todas.
- **Cabecera contraíble y horarios** (RF-42), **mangas opcionales por establecimiento** con cantidad a mano, contador N/M y cierre automático (RF-43 a RF-45), **Terminar el tacto** con pregunta final y advertencia (RF-46). Las mangas se definieron con el usuario: grupo de 10 a 20 vacas que entra al ordeñe junto; por tambo suele haber dos ordeñando a la vez (pendiente). Apagado, la app usa una manga interna (no se tocó la base).
- **Deshacer y movimientos** (RF-47 a RF-49): sin aviso de deshacer al marcar; movimientos sin manga ni veterinario y con CVET; **Editar** = anular el tacto y registrar uno nuevo en una transacción (`CorregirTactoAsync`, núcleos sin transacción propia, órdenes consecutivos).
- **Importar el Excel real** (RF-52): NPOI 2.7.6 fallaba con "Unexpected record type (SelectionRecord)"; `PerfilDairyComp` usa `LectorBiffTolerante` (modo de eventos) solo si NPOI falla. El Excel real **no se commitea** (queda en Descargas).
- **Teclado y márgenes** (RF-51): `OnBuscarTerminado` baja el teclado; en `MainActivity` un listener de insets aplica barras del sistema + teclado (`SafeAreaEdges` de MAUI no alcanzó). Se probó con `adb shell input` y `adb exec-out screencap -p`.
- **Resetear rodeo** (RF-50, temporal): reimporta el Excel original guardado como rodeo nuevo y da de baja el viejo; doble verificación; nada se borra.
- **Orden del código (2026-11-21):** `TrabajoViewModel` se partió en cuatro archivos `partial` por tema (`.cs` núcleo y encabezado, `.Ficha.cs`, `.Mangas.cs`, `.Avisos.cs`); los tests enlazan todo `ViewModels/*.cs`, no hizo falta tocar nada. `GeneradorMuestras` pone en cero las fechas del contenedor OLE2, así las muestras son deterministas.
- **Cosas que NO se hicieron a propósito:** la preferencia de mangas ya no es por veterinario (el usuario lo cambió a por establecimiento); la decisión sobre registrar al veterinario está abierta y no afecta a esa preferencia.

### Spec 003, implementación (2026-11-17)

- **2026-11-17 — Permisos de red quitados (corrección tras validar la spec 003):** se sacaron `INTERNET` y `ACCESS_NETWORK_STATE` de `AndroidManifest.xml` (la app es 100 % offline, constitución §1). Venían de la plantilla MAUI del commit 0.3. Queda `VIBRATE`. Si algún día la recarga en caliente de Debug necesitara `INTERNET`, no se vuelve a agregar: se despliega sin recarga en caliente.

- **2026-11-17 — Resto de pantallas y cierre (spec 003, T5):**
  - **Las capas de "Finalizar manga" y "Terminar el tacto" ya estaban en `TrabajoPage` (T4)**; T5 sumó `EleccionTrabajoPage` (tres columnas: veterinario con alta, establecimiento, rodeo con avance; "Volver" y "Empezar a trabajar"), `MovimientosPage` (lista con "Deshacer" y "Al cerrar: N · Hoy: M") y el botón "Trabajar". `EleccionTrabajoViewModel` sumó `VolverAlInicioCommand` (con test).
  - **Navegación:** la elección se abre con la ruta absoluta `//Inicio/EleccionTrabajo` (así "Salir" del trabajo no apila una elección sobre otra); "Volver" de movimientos es `Shell.GoToAsync("..")` desde el code-behind (navegación pura, sin lógica). El `Shell` no muestra barra de navegación: el "Volver" es un botón grande y también anda el botón Atrás de Android.
  - **`VINS` en la muestra de 226 vacas:** ahora es un número de 0 a 6 (celda numérica), no una fecha; se regeneró solo `dairy-comp-226-vacas.xls` (las demás muestras no cambiaron). `FechasMuestraTests` comprueba que `FPART`, `FCELO` y `FAPAR` se interpretan con `FechaExcel` y que `VINS` es un entero.
  - **Supuestos y riesgos de la spec 003 que siguen abiertos:** el código leído por el bastón se trata como caravana primero y como identificador electrónico después (a confirmar con el XRS2); el formato real de las fechas del Excel se vio en una sola muestra; la marca automática puede marcar una vaca vecina si el bastón lee de más (por eso existen "Deshacer" y "Corregir"); un reloj mal puesto se rechaza (sin guardar nada) en vez de aceptarse; una manga que cruza la medianoche sigue abierta y se cierra sola, como `Automatico`, al volver a trabajar otro día.
  - **Interfaz:** el usuario pidió mejorarla (sin detalle). Queda como ajuste aparte de estilos y disposición, sin tocar la lógica.

- **2026-11-17 — Pantalla de trabajo (spec 003, T4):**
  - **Capas, no `DisplayAlert`:** aviso, no encontrada, alta, verificación, cantidad real, segunda confirmación y resumen son capas dentro de `TrabajoPage` enlazadas a `CuadroTrabajo`. Cada capa lleva un `TapGestureRecognizer` vacío para que en Android no pasen los toques a lo de abajo (a confirmar en el emulador).
  - **Aviso de 5 s con "Deshacer":** va sobre la barra de búsqueda (abajo), no sobre los botones de estado ni empujando el layout.
  - **Panel del simulador:** se arma desde el código de la página dentro de `#if DEBUG` (la XAML solo tiene un contenedor vacío y oculto); en Release no existe.
  - **Convertidores de presentación** en `src/Aviss.App/Convertidores/` (formato de fechas del historial, estado actual resaltado, invertir booleano): sin reglas de negocio.
  - **Cambiar de veterinario** desde el encabezado: lista armada por el code-behind con `ListarVeterinariosAsync` y `CambiarVeterinarioCommand`.
  - **Acceso temporal (solo Debug):** botón en `InicioPage` que elige un veterinario y el primer rodeo y abre `TrabajoPage`; **T5 lo borra** al agregar "Trabajar" y la elección.

- **2026-11-17 — Lector, preferencias y ViewModels de campo (spec 003, T3):**
  - **Cómo se prueban los ViewModels:** igual que en la spec 002: `Aviss.Application.Tests` compila enlazados los `src/Aviss.App/ViewModels/*.cs` (no usan MAUI) y tiene fakes en `tests/Aviss.Application.Tests/ViewModels/Soporte/` (`FakeCampoService`, `FakeLector`, `FakeTemporizador` controlado a mano, etc.). Un archivo nuevo en `ViewModels/` entra solo; si un ViewModel necesitara un tipo de MAUI, no compilaría en los tests (esa es la regla).
  - **`ColaDeLecturas`:** procesa de a una, **en orden** (dos vacas muy seguidas quedan marcadas las dos); solo con un cuadro que pide datos abierto (alta, cantidad real, verificación, segunda confirmación, confirmar "Corregir") se bloquea y conserva **solo la última**. "Aviso" y "No encontrada" **no** bloquean: una lectura nueva los cierra y se procesa (RF-22b). El lector alimenta la cola sin esperar el procesamiento.
  - **Misma vaca de la ficha:** se detecta por el resultado del servicio (llega `Repetido`/`YaPaso` con el mismo `AnimalId` que la ficha abierta) y se ignora sin aviso; así también vale para el identificador electrónico.
  - **Alta rechazada:** el formulario queda abierto con el motivo (se puede corregir la caravana y reintentar) en vez de volver a "No encontrada" como decía el diagrama del plan.
  - **Aviso de deshacer de "Terminar el tacto":** no se publica el aviso de 5 s; el resumen ya tiene su propio "Deshacer" permanente. "Finalizar manga" sí publica el aviso del cierre.
  - **No se vuelve a llamar a `IniciarTrabajoAsync` al volver de "Últimos movimientos"** (solo la primera vez por rodeo y por instancia del ViewModel): si no, una manga que cruzó la medianoche se cerraría sola al volver (RF-10). Al volver solo se recalcula.
  - **Navegación:** `NavegacionShell` ya tiene las tres rutas nuevas (`Eleccion`, `Trabajo`, `Movimientos`) pero **ninguna página registrada todavía**: ir a esas rutas falla hasta T4 y T5 (ningún botón visible llega ahí aún).
  - **Registro del lector:** `FakeRfidReader` (una instancia que es `IRfidReader` e `ISimuladorLecturas`) solo dentro de `#if DEBUG` en `MauiProgram`; en Release se registra `LectorNoDisponible` y no existe `ISimuladorLecturas` (el ViewModel lo recibe opcional y `HaySimulador` da falso). El código de Release compiló (se generó `Aviss.App.dll` en `bin/Release`); el empaquetado completo de Release no se esperó.
  - **`AvisosAndroid`:** `Vibrator` (o `VibratorManager` desde Android 12) y `ToneGenerator`; permiso `VIBRATE` en el manifiesto. Sin voz.

- **2026-11-17 — Servicios de campo (spec 003, T2):**
  - **Fachada con scope por operación:** `ICampoService` es singleton (`CampoService`) y abre un scope (un DbContext y una transacción) por operación; los servicios internos (`MarcaPasadaService`, `MangaService`, `AltaCampoService`, `ConsultaCampoService`) son scoped. Las operaciones que escriben se serializan con un `SemaphoreSlim` (SQLite admite un solo escritor). Las consultas de solo lectura no abren transacción.
  - **Orden de guardado compartido:** `OrdenCompartido.SiguienteAsync` = máximo de `OrdenInsercion` entre `Tacto` y `MarcaPasada` más uno, dentro de la transacción. `TrabajoCampoService` ya no calcula el suyo y valida/anula contra el **último registro vigente de cualquier tipo** (`IRegistroCampoRepository`); un tacto no se anula si después se guardó una marca.
  - **Procesada hoy y manga que cruza la medianoche:** procesada = marca vigente o tacto normal vigente dentro del día local. Si la manga abierta cruza la medianoche y la vaca ya está marcada **en esa manga**, se trata como "Animal repetido" (aunque la marca sea de ayer): el índice único `(MangaId, AnimalId)` no admite una segunda marca vigente y sin esto la lectura fallaba con una excepción de la base.
  - **Cierre con anulación y reapertura:** `MangaService.ReabrirAsync` aplica `ReglasManga`; el "último cierre vigente del rodeo" se ordena por fecha y hora y, con el mismo instante, por número de manga mayor. Cerrar sin manga abierta en "Terminar el tacto" devuelve `Cierre = null` y no guarda nada; en "Finalizar manga" es el error `NoHayMangaAbierta`.
  - **Anular un alta:** rechaza si hay tactos vigentes o **más de una** marca vigente (la única marca vigente se toma como la del alta y se anula junto con ella). `AnimalService.AnularAltaAsync` también rechaza con marcas vigentes (`AnimalConMarcasVigentes`); su núcleo de alta (`CrearAltaAsync`) se reutiliza sin transacción propia para el alta con marca.
  - **Búsqueda de la lectura:** caravana primero (si el código es una caravana válida) y después identificador electrónico; un código de más de 20 caracteres igual se prueba como identificador. Siempre dentro del rodeo en trabajo y entre no anulados.
  - **Ficha:** `ConstructorFicha` (puro) lee `DatosOriginalesJson` sin fallar nunca; campos `LACT`, `DEL`, `FPART`, `FAPAR`, `COM` como texto, `VINS` y `UTORO` aparte, historial cronológico (Excel + tactos de Aviss, sin marcas; fechas ilegibles al final como texto). Los días transcurridos se cuentan por día local (negativos en fechas futuras).
  - **Avance:** porcentaje entero hacia abajo (no se muestra 100 % hasta que no falte ninguna); rodeo vacío = 0.
  - **Zona horaria:** `ZonaHorariaSistema` limpia la caché de `TimeZoneInfo` antes de leer `Local`, para notar un cambio de zona con la app abierta.
  - **Rendimiento (RF-37):** con 20.000 vacas y más de 100.000 registros, el avance pasaba de 200 ms (233 ms): faltaba un índice por fecha en `Tacto`. Se agregó la migración `IndiceTactoFecha` (solo `CREATE INDEX`, no reconstruye la tabla). `RendimientoCampoTests` toma el mejor de tres mediciones por prueba. Las tres clases de rendimiento (`RendimientoCampoTests`, `RendimientoTests`, `ImportacionRendimientoTests`) comparten `[Collection("Rendimiento")]` y corren de a una: en paralelo se falseaban los tiempos y `ImportacionRendimientoTests` fallaba por carga (sin tocar sus límites).
  - **Código de error nuevo `FechaAnteriorAlUltimoRegistro`** (el plan no lo listaba): el tacto sigue usando `FechaAnteriorAlUltimoTacto`; la marca usa el nuevo, con mensaje que sugiere revisar la hora.

- **2026-11-17 — Migración `Mangas` (spec 003, T1):** EF ejecuta las reconstrucciones de tabla de SQLite **siempre al final** de la migración, después de cualquier `migrationBuilder.Sql`; por eso recrear los triggers de `Historial` "al final" no alcanzaba (quedaban borrados). El CHECK de `Historial` se reescribe con una reconstrucción **a mano** (`ReconstruirHistorial`, con índices y triggers recreados) y se quitaron de la migración las operaciones `Drop/AddCheckConstraint` de EF (el snapshot sí tiene el CHECK nuevo). El nombre de veterinario es único por un índice por expresión escrito a mano (`IX_Usuario_NombreClave`, EF no lo conoce: si una migración futura reconstruye `Usuario`, hay que recrearlo); los duplicados previos se renombran con ` (rowid)`, conservando "Sin asignar". `Manga` no tiene `ActualizadoEnUtc` (nunca cambia); `CierreManga` guarda `Diferencia` con un CHECK que la liga a confirmada − Aviss.

### Spec 001 y 002, y plataforma (2026-11-16)

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

- **Advertencia NU1903 en `System.Security.Cryptography.Xml 8.0.2`:** dependencia transitiva (probablemente de NPOI) con una vulnerabilidad alta conocida. Fuera del alcance de la spec 003; revisar en la etapa 6 junto con las demás advertencias de dependencias (ver riesgos de la spec 002).
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
