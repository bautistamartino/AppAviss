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

1. Pasar el revisor sobre la spec 003 y mergear `spec/003-flujo-de-tacto` a `main` (la rama ya está subida a GitHub).
2. Probar en la tablet lo que quedó sin verificar (chicharra del CVET 0, aviso de manga completa, cierre automático, "+ Nuevo evento", engranaje): lista en la spec 003, "Criterios de aceptación de esta sección".
3. Confirmar con el usuario las decisiones abiertas: dos mangas a la vez (el veterinario ya no se registra: spec 006) y varios lotes (spec 003, "Preguntas abiertas").
4. La spec 004 (tratamientos, tres renglones de evento, rechazo y "En Espera") está en borrador sin aprobar.
5. Sacar el botón "↺ A 0" (resetear rodeo) cuando se termine de probar (spec 003, RF-50).
6. Sigue pendiente la prueba 0.2 de reimportación en Dairy Comp.

## 7. Probar en una tablet real

1. En la tablet: Ajustes > Acerca del dispositivo > tocar 7 veces "Número de compilación"; después Opciones de desarrollador > **Depuración USB**. Conectar por cable de **datos** (uno de solo carga no sirve) y aceptar el cartel "¿Permitir depuración USB?".
2. Comprobar que se ve: `& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices -l` (tiene que decir `device`, no `unauthorized`). Si no aparece, mirar en el Administrador de dispositivos si Windows ve la "ADB Interface".
3. Instalar y abrir: `dotnet build src/Aviss.App -f net10.0-android -t:Run` (con más de un dispositivo, el emulador o la tablet se elige desconectando el otro).
4. Llevarle un Excel: `adb push archivo.xls /sdcard/Download/` (en Git Bash, con `MSYS_NO_PATHCONV=1` delante). El Excel real de un tambo nunca se commitea.
5. Probar mirando la pantalla con capturas: `adb exec-out screencap -p > captura.png` y tocar con `adb shell input tap X Y` (la tablet de prueba mide 800x1340 y se usa en **vertical**).
