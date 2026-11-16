# Constitución de Aviss

Principios que **ninguna spec, plan o implementación puede romper**. Si una petición choca con alguno, se frena y se consulta al responsable del proyecto. Cambiar esta constitución requiere su aprobación explícita.

## 1. Funciona sin conexión
- Todo el trabajo de campo funciona sin Internet. No hay llamadas de red en el flujo de trabajo.
- El único intercambio con el exterior es **importar** y **exportar** archivos Excel.

## 2. El Excel no es la base de datos
- Durante el trabajo solo se usa SQLite. El Excel es un medio de entrada y salida.
- El archivo original **nunca se modifica**: se trabaja sobre una copia y se guarda el original (con su hash SHA-256) dentro del rodeo importado.
- La exportación parte de esa copia, cambia solo lo necesario y se **reabre y valida** antes de darla por buena.

## 3. No se pierde nada
- Cada cambio se confirma en SQLite al instante, sin botón "Guardar". Si la tablet se apaga o la app se cierra, no se pierde información.
- Un tacto es **una transacción**: actualizar el animal, insertar el tacto e insertar el historial, o nada.
- Tactos e historial solo se **agregan**. Corregir es anular con otro registro. Un trigger de la base rechaza UPDATE y DELETE sobre el historial.
- Los animales no se borran.

## 4. Fidelidad con Dairy Comp
- El `ID` de Dairy Comp es la caravana y el botón (`NumeroCaravana`).
- Los estados y sus códigos salen de un catálogo configurable, no de valores fijos en el código.
- Lo que Dairy Comp acepta o rechaza al importar se prueba con archivos reales de muestra; no se supone. Mientras no esté validado, queda anotado como riesgo en `MEMORY.md`.

## 5. Arquitectura por capas
- Las dependencias apuntan al dominio: `Domain` no depende de nada; `Application` solo de `Domain`; `Infrastructure`, `Excel` y `Rfid` implementan los puertos de `Application`; `App` arma todo con inyección de dependencias.
- El lector RFID y el formato Excel se esconden detrás de interfaces (`IRfidReader`, `IExcelProfile`) para poder cambiarlos.
- El código específico de Android vive solo en `Aviss.App/Platforms/Android`.

## 6. Datos y privacidad
- Los Excel reales de tambos no se commitean; los tests usan muestras anonimizadas en `tests/muestras/`.
- La base vive en el almacenamiento privado de la app. No se loguean datos de animales.
- Secretos (keystore de firma, contraseñas) fuera del repo, documentados sin valores reales.

## 7. Base de datos
- Todo cambio de estructura es una migración de EF Core, versionada en Git, aplicada al iniciar con backup previo.
- Las reglas críticas (unicidad `(RodeoId, NumeroCaravana)`, claves foráneas, inmutabilidad del historial) se garantizan **también en la base**.
- IDs GUID; fechas en UTC y mostradas en hora local; todas las tablas con `CreadoEnUtc` (y `ActualizadoEnUtc` las editables). Esto deja la puerta abierta a una sincronización futura.

## 8. Calidad
- La lógica nueva se escribe con **tests primero** (xUnit).
- Nunca se da una tarea por terminada con tests en rojo o con errores de compilación (`dotnet test`, `dotnet build`).
- Importación y exportación se prueban con archivos de muestra; el lector RFID, con tramas grabadas y un `FakeRfidReader`.
- La pantalla se verifica en el emulador o en la tablet; cada tarea visual indica qué mirar.

## 9. Interfaz de campo
- En español rioplatense, pensada para trabajar de pie, con una mano y al sol: botones grandes, alto contraste, mínima escritura.
- Meta: **un toque por vaca** una vez leída la caravana. Siempre hay una búsqueda manual por número como respaldo.
- Toda acción de campo se puede deshacer (con un registro de anulación).

## 10. Simplicidad
- Se construye lo que pide la spec, no más. Sin dependencias nuevas sin justificarlo en el plan.
- Nombres del dominio en español (`Animal`, `Rodeo`, `Tacto`). Código claro y comentado donde la intención no sea obvia: el responsable del proyecto está aprendiendo.

## 11. Git
- Una rama por spec o cambio. Commits en español con formato `tipo: descripción`.
- Nadie hace `git push` sin que el responsable lo pida.
