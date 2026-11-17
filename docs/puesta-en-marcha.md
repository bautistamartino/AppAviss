# Puesta en marcha en otra PC

Guía para dejar una PC nueva (Windows) lista y retomar el trabajo donde quedó. Los comandos son los de [CLAUDE.md](../CLAUDE.md).

## 1. Qué instalar

1. **Git**.
2. **.NET SDK 10** (en la PC original: 10.0.404, instalado con `winget`).
3. **Carga de trabajo MAUI para Android**: `dotnet workload install maui-android`.
4. **JDK 21** (Microsoft OpenJDK 21). Ojo: .NET 10 no compila con otras versiones (el JDK 25 de Android Studio y un Java 8 viejo no sirven).
   - `Directory.Build.props` tiene una ruta fija: `C:\Program Files\Microsoft\jdk-21.0.13.21-hotspot` (solo se usa si existe). Si tu JDK 21 está en otra ruta, pasala en cada comando con `-p:JavaSdkDirectory="C:\ruta\al\jdk-21"` o editá esa línea de `Directory.Build.props`.
5. **Android SDK + emulador**: lo más simple es instalar Android Studio; el SDK queda en `%LOCALAPPDATA%\Android\Sdk`. Después se puede completar con:
   `dotnet build src/Aviss.App -f net10.0-android -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True -p:AndroidInstallJavaDependencies=false`
   (en la PC original instaló la plataforma Android 36 y `cmdline-tools`).
6. **Emulador de tablet `Tablet_Aviss`**. Datos conocidos (de `MEMORY.md`): imagen `android-37.0 google_apis_playstore x86_64`, pantalla 2560x1600 en horizontal, 4 GB de RAM. En la PC original se creó a mano copiando la config de otro AVD. En la nueva, creala desde Android Studio > Device Manager con el perfil "Pixel Tablet", esa imagen de sistema y el nombre `Tablet_Aviss`. La densidad (dpi) no está documentada: dejá la que traiga el perfil.

## 2. Clonar

```
git clone https://github.com/bautistamartino/AppAviss.git
cd AppAviss
git switch spec/003-flujo-de-tacto
```

(En la PC anterior el remoto se llama `AppAviss`; en el clon nuevo será `origin`.)

## 3. Compilar y probar

```
dotnet tool restore
dotnet build
dotnet test
```

Se esperan **957 tests en verde**. Los de rendimiento (`Categoria=Rendimiento`) dependen de la PC y pueden ser lentos; para saltearlos: `dotnet test --filter "Categoria!=Rendimiento"`.

## 4. Correr la app en el emulador

1. Levantar el emulador (PowerShell): `& "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe" -avd Tablet_Aviss`. `adb` está en `%LOCALAPPDATA%\Android\Sdk\platform-tools`.
2. Compilar, instalar y abrir: `dotnet build src/Aviss.App -f net10.0-android -t:Run`.

Problemas conocidos:

- **La app se cierra al abrir** (error de `ThemeEnforcement` en `BottomNavigationView`): borrar las carpetas `src/Aviss.App/bin` y `src/Aviss.App/obj`, desinstalar la app del emulador y rehacer el build limpio.
- **En Git Bash**, `adb push` convierte las rutas y falla: anteponer `MSYS_NO_PATHCONV=1` (por ejemplo `MSYS_NO_PATHCONV=1 adb push archivo.xls /sdcard/Download/`).

## 5. Qué NO viene en Git

- **Excel reales de tambos**: nunca se commitean. La muestra anonimizada sí está: `tests/muestras/dairy-comp-226-vacas.xls`. Para probar la importación en el emulador, subila con `adb push`.
- **Secretos y keystore de firma**: viven fuera del repo.
- **Memoria local de Claude**: no se copia. El contexto vive en `CLAUDE.md`, `MEMORY.md`, `docs/` y `specs/`.
- La base SQLite del emulador es local: en el emulador nuevo arranca vacía.

## 6. Cómo retomar con Claude

Abrir una sesión en la carpeta del repo y pedir: **"leé CLAUDE.md, MEMORY.md, docs/plan.md y specs/003-flujo-de-tacto"**.

Pendientes al retomar:

1. Probar en el emulador las pantallas de T5: `EleccionTrabajoPage`, "Finalizar manga", "Terminar el tacto" y `MovimientosPage` (lista de "Qué mirar" en `specs/003-flujo-de-tacto/tasks.md`).
2. Mejorar la interfaz (el usuario todavía tiene que decir qué cambiar).
3. Pasar el revisor y mergear `spec/003-flujo-de-tacto` a `main`.
4. La spec 004 (tratamientos, rechazo, estados nuevos) está en borrador sin aprobar.
5. Sigue pendiente la prueba 0.2 de reimportación en Dairy Comp.
