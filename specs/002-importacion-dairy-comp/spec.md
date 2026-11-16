# 002 — Importación de Dairy Comp

Estado: aprobada
Fecha: 2026-11-16

## Contexto
Hoy el veterinario depende de Dairy Comp (y de una licencia por tablet) para cargar el rodeo antes de ir al tambo. Aviss tiene que poder arrancar desde el Excel `.xls` que exporta Dairy Comp: leerlo, mostrar qué se va a importar y qué filas tienen problemas, y recién cuando el veterinario confirma, crear el rodeo con sus vacas y guardar el archivo original. Lo usa el veterinario, en la tablet, sin conexión, antes o durante la visita. Esta spec es la entrada de datos; el trabajo de campo y la exportación son specs aparte.

## Alcance
- Incluye:
  - Elegir un archivo `.xls` de Dairy Comp desde la tablet (el selector de archivos de Android entrega un flujo de lectura, no una ruta).
  - Leerlo sin modificarlo, validar su estructura y sus filas, y mostrar una vista previa con advertencias y errores por fila.
  - Confirmar la importación: crear el rodeo (en un establecimiento existente o nuevo) con sus animales y guardar el archivo original con su huella SHA-256.
  - Detección de archivo ya importado antes, filas con ID vacío, inválido o repetido, estados desconocidos y fila final `Total:N`.
  - Pantallas mínimas: elegir archivo, vista previa con elección de establecimiento y nombre del rodeo, resultado.
  - Muestras de prueba anonimizadas.
- No incluye: exportar, trabajo de campo, lector RFID, archivos `.xlsx`, editar filas desde la vista previa, actualizar o fusionar con un rodeo existente, administrar el catálogo de estados en pantalla, selección de veterinario, interpretar fechas.
- **Requisito para la spec 003 (fuera de esta spec):** durante el tacto, el veterinario debe poder agregar estados nuevos al catálogo (por ejemplo "aborto" en una vaca preñada) y registrar observaciones o tratamientos (por ejemplo, sincronización de celos). Lo registra el **implementador** en `docs/plan.md`, ítem 3.1, al cerrar esta spec.
- **Estructura (restricción de la constitución):** el formato de Dairy Comp vive en `Aviss.Excel` como un perfil detrás de `IExcelProfile`; `Domain` y `Application` no dependen de NPOI.
- **Base de datos:** no hace falta una segunda migración. El rodeo de la spec 001 ya guarda el archivo original, su huella y el perfil, y el animal ya guarda las columnas originales. No se agrega índice sobre la huella (la búsqueda de archivos repetidos recorre pocos rodeos).

## Definiciones
- **Fila de datos:** fila posterior a la de encabezados que no es la final `Total:N` y no está totalmente vacía. Una celda con solo espacios cuenta como vacía.
- **Fila válida:** fila de datos cuyo `ID` es utilizable (RF-8) y no está repetido en el archivo (RF-9).
- **Fila excluida:** fila de datos con error de fila; no se importa. (Nota para el implementador: `docs/arquitectura.md` habla de "rechazadas" y de `CantidadAnimales`; corregir a "excluidas" y quitar `CantidadAnimales`, que la spec 001 ya definió como calculada.)
- **Advertencia:** problema que no impide importar. **Error de fila:** problema que deja la fila excluida.
- **Rechazo del archivo:** no se importa nada y se informa el motivo.
- **Vista previa:** el resultado de leer y validar un archivo, que vive en memoria hasta que se confirma o se descarta. Se confirma una sola vez.

## Requisitos funcionales

### Leer el archivo
- **RF-1** — Cuando el veterinario elige un archivo, el sistema deberá leerlo por flujo de lectura (sin necesitar su ruta), una sola vez, en modo solo lectura, sin modificar el original, y calcular la huella SHA-256 de los bytes leídos tal como están. Esos mismos bytes son los que luego se guardan en el rodeo: el archivo no se vuelve a leer al confirmar.
- **RF-2** — El sistema deberá validar el archivo en este orden, y al primer fallo rechaza el archivo entero con un mensaje claro en español (sin datos de animales ni de tambos):
  1. Tamaño: más de **25 MB** (26.214.400 bytes) o archivo vacío.
  2. Formato: el formato se detecta por el contenido, no por la extensión. Debe ser un libro `.xls` legible; si es otro formato (por ejemplo `.xlsx` renombrado), está corrupto, está protegido con contraseña o no tiene hojas, se rechaza.
  3. Encabezados (RF-3).
  4. Límites: más de **20.000** filas de datos (20.001 ya se rechaza) o ninguna fila de datos.
  5. Recién entonces, la validación fila por fila (RF-7 a RF-12).
- **RF-3** — El sistema deberá leer solo la **primera hoja**. La fila de encabezados es la primera fila con contenido. Los encabezados se comparan sin distinguir mayúsculas ni espacios de los extremos y en cualquier orden. Si falta `ID` o `RPRO`, o algún encabezado (no vacío) aparece repetido, rechaza el archivo indicando cuál. Las celdas de encabezado vacías se ignoran (esa columna no se guarda).
- **RF-4** — Si falta alguna columna opcional (`CVET`, `FPART`, `LACT`, `DEL`, `FCELO`, `VINS`, `UTORO`, `COM`, `FAPAR`), entonces el sistema deberá avisarlo en la vista previa e importar igual. Las columnas desconocidas (con encabezado no vacío) se conservan en las columnas originales.
- **RF-5** — El sistema deberá reconocer la fila final `Total:N` en cualquier fila posterior a los encabezados: su primera celda es un texto que, sin distinguir mayúsculas ni espacios de los extremos, empieza con "Total", seguido de ":" y un número entero. La ignora. Si hay varias, compara con la última. Si `N` no coincide con la cantidad de filas de datos leídas, muestra una advertencia sin bloquear. Una fila totalmente vacía se saltea sin error; una fila sin `ID` pero con otros datos es un error de fila (RF-8).

- **RF-6** — El sistema deberá tratar como vacía una celda en blanco o con solo espacios, y como fila totalmente vacía a la que tiene todas sus celdas vacías. Las filas posteriores a `Total:N` se siguen leyendo como filas de datos.

### Validar filas
- **RF-7** — El sistema deberá obtener el número de caravana de la celda `ID` según su tipo:

  | Tipo de celda | Regla | Motivo si falla |
  |---|---|---|
  | Texto | Se recortan espacios, NBSP y demás espacios en blanco de los extremos. Si queda vacío: error. Si tiene espacios o caracteres de control en el medio: error. Si supera 20 caracteres: error. Si no, se guarda tal cual (ceros incluidos). | `ID_VACIO`, `ID_CARACTERES`, `ID_LARGO` |
  | Numérica entera exacta, no negativa, hasta 15 dígitos | Se toma como texto sin decimales ni notación científica (`123.0` pasa a "123"). | — |
  | Numérica no entera, negativa, de más de 15 dígitos o en notación científica | Error. | `ID_NUMERO_INVALIDO` |
  | Fecha, booleana, error o fórmula | Error. | `ID_TIPO_INVALIDO` |
  | En blanco | Error. | `ID_VACIO` |

  El resultado debe ser válido según la regla `Caravana` de la spec 001 (después de recortar).
- **RF-8** — Si el `ID` de una fila no es utilizable, entonces el sistema deberá marcarla como error de fila con su número de fila del Excel y el motivo de la tabla de RF-7 (`ID_VACIO`, `ID_CARACTERES`, `ID_LARGO`, `ID_NUMERO_INVALIDO`, `ID_TIPO_INVALIDO`), y dejarla fuera. Una fila tiene a lo sumo un motivo. Precedencia: `ID_VACIO`, luego los de `ID` inválido o largo, luego `ID_REPETIDO`.
- **RF-9** — Si el mismo `ID` aparece en más de una fila, entonces el sistema deberá marcar todas esas filas como error `ID_REPETIDO`, con el número de cada una, y dejarlas fuera: no se sabe cuál es la correcta. La comparación usa `Caravana.Clave` (la misma regla que la base: no distingue mayúsculas, incluidas la Ñ y las vocales con tilde), de modo que "ñ1" y "Ñ1" se excluyen juntas. Las filas con `ID` vacío, inválido o largo no participan de la detección de repetidos.
- **RF-10** — Cuando el valor de `RPRO` (recortado, sin distinguir mayúsculas, sin normalizar acentos) coincide con el código de Dairy Comp de un estado activo del catálogo, el sistema deberá asignarlo como estado del animal.
- **RF-11** — Si el valor de `RPRO` no coincide con ningún estado activo del catálogo (un estado dado de baja cuenta como desconocido), entonces el sistema deberá importar el animal sin estado y mostrar una advertencia agrupada por valor: el valor, la cantidad de filas y a lo sumo **5** números de fila de ejemplo. No se crean estados. Si `RPRO` está vacío, el animal se importa sin estado y sin advertencia.
- **RF-12** — El sistema deberá guardar en cada animal importado sus columnas originales como un objeto JSON: la clave es el encabezado original (con su capitalización) y el valor, siempre un texto, tal como lo entrega la celda (numéricas en formato invariante sin ceros de más; fechas como el texto que la celda muestra). Las celdas vacías se omiten. Se incluyen las columnas desconocidas. No se interpreta ni corrige ningún valor. `COM` no se copia a las observaciones (quedan vacías), el identificador electrónico queda vacío y la fecha de último tacto queda vacía.

### Vista previa, establecimiento y confirmación
- **RF-13** — Cuando el archivo es aceptado, el sistema deberá mostrar una vista previa con: nombre del archivo, filas de datos leídas, filas que se importarán, filas excluidas (número de fila y motivo, sin caravanas), advertencias y el detalle por estado (cuántas vacas por estado y cuántas sin estado). Si no hay ninguna fila válida pero sí filas de datos, la vista previa se muestra igual (con los errores), pero no se puede confirmar.
- **RF-14** — El flujo es: elegir archivo, ver la vista previa, elegir o crear el establecimiento y el nombre del rodeo, confirmar. Se hace en ese orden porque la lectura y la validación no dependen del establecimiento y el veterinario ve primero si el archivo sirve. El establecimiento puede ser uno existente y activo, o uno nuevo escrito en pantalla. Si no hay establecimientos, solo se ofrece crear. El nombre del rodeo se propone como el nombre del archivo sin extensión y puede editarse.
- **RF-15** — Los nombres de establecimiento y de rodeo se recortan, son obligatorios y tienen como máximo **100** caracteres. Si el establecimiento nuevo repite el de uno existente (comparado sin distinguir mayúsculas, con `Caravana.CalcularClave`, incluidos los dados de baja), entonces el sistema deberá mostrar el error "Ya existe un establecimiento con ese nombre; elegilo de la lista" y no crearlo. Se permite repetir el nombre de un rodeo.
- **RF-16** — Mientras no haya al menos una fila válida, o falte el establecimiento o el nombre del rodeo, el sistema deberá impedir confirmar. Cuando el archivo tiene la misma huella que un rodeo ya guardado (de cualquier establecimiento), el sistema deberá avisar con el nombre y la fecha de importación de ese rodeo, y permitir cancelar o confirmar igual como rodeo nuevo. Nunca modifica el rodeo existente. El nombre del rodeo existente se muestra en pantalla, pero no se escribe en ningún registro de errores.
- **RF-17** — Cuando el veterinario confirma, el sistema deberá pedir antes de guardar una copia de seguridad a pedido (`MotivoBackup.APedido`, spec 001 RF-21). Esa copia cuenta dentro de las 10 rotativas, pero nunca desplaza a la previa a actualización más reciente. Si la copia falla o no hay espacio, no importa nada y avisa con un mensaje claro y sin datos. El tope de 25 MB por archivo acota el crecimiento de las copias, que igual se registra como riesgo.
- **RF-18** — Cuando el veterinario confirma, el sistema deberá guardar en una única operación indivisible: el establecimiento nuevo (si corresponde), el rodeo (nombre, fecha de importación, nombre del archivo de origen, huella, el archivo original completo, perfil "Dairy Comp" y estado Abierto) y todos los animales válidos con origen Importado. Si algo falla (disco lleno, error de la base, rechazo por reglas), no debe quedar nada y se informa con un mensaje claro. La importación no genera historial por animal. Como ni el rodeo ni los animales guardan un usuario, no se asigna ninguno.
- **RF-19** — El sistema deberá permitir confirmar cada vista previa una sola vez. Mientras la confirmación está en curso, el botón Confirmar queda deshabilitado, y una segunda ejecución de la misma vista previa (concurrente o posterior) se rechaza sin crear nada. Volver a importar el mismo archivo a propósito es una vista previa nueva (RF-16).
- **RF-20** — Si entre la vista previa y la confirmación el establecimiento o el rodeo no son válidos (se dieron de baja, o ya existe otro con el mismo nombre por otra importación), entonces el sistema deberá no guardar nada y mostrar un mensaje claro. Dos confirmaciones de vistas previas distintas se serializan en la base: ambas pueden crear rodeos distintos, pero un establecimiento nuevo con el mismo nombre solo lo crea una.
- **RF-21** — Cuando termina la importación, el sistema deberá mostrar el resultado: filas leídas, animales importados, filas excluidas, advertencias y nombre del rodeo creado.
- **RF-22** — Cuando el veterinario cancela en la vista previa, o la vista previa deja de existir porque el sistema operativo cerró la app, el sistema deberá descartarla sin guardar nada. Rotar la pantalla o pasar la app a segundo plano conserva la vista previa mientras la app siga viva; si la app se cerró, hay que volver a elegir el archivo.

### Transversales
- **RF-23** — El sistema no deberá usar la red en ninguna parte de la importación.
- **RF-24** — Los mensajes y registros de error no deberán incluir datos de animales ni de tambos (caravanas, nombres de establecimiento). En pantalla solo pueden verse: números de fila, motivos, el nombre del archivo elegido, el nombre y la fecha del rodeo ya existente (RF-16) y los valores de `RPRO` de las advertencias.
- **RF-25** — El sistema deberá poder cambiar el formato soportado (otro sistema, otro perfil) sin tocar el resto de la importación; el perfil usado queda registrado en el rodeo.
- **RF-26** — Con 20.000 filas de datos, la lectura y validación deberá tardar menos de 10 segundos y la confirmación menos de 15 segundos (medido en test, no en la tablet). El pico de memoria es el del archivo (hasta 25 MB) más el modelo de lectura; el archivo se mantiene una sola vez para hash y guardado.

## Casos límite
- **Sin conexión:** todo funciona sin Internet (RF-23).
- **Corte de energía:** durante la lectura o la vista previa no se guardó nada; durante la confirmación, al reabrir el rodeo está completo o no existe (RF-18). Una copia incompleta no cuenta (spec 001).
- **Excel mal formado:** vacío, más de 25 MB, corrupto, con contraseña, `.xlsx` (incluso renombrado), sin hojas, sin `ID` o `RPRO`, encabezado duplicado, más de 20.000 filas: se rechaza con mensaje (RF-2, RF-3).
- **0 filas de datos** (solo encabezados): archivo rechazado (RF-2). **Filas de datos pero 0 válidas:** se muestra la vista previa con los errores y no se puede confirmar (RF-13, RF-16). Son dos escenarios distintos.
- **20.000 filas se aceptan; 20.001 se rechazan:** el archivo entero (RF-2).
- **Encabezados con otra capitalización o espacios:** se reconocen (RF-3).
- **Columnas opcionales ausentes o desconocidas:** se avisa e importa (RF-4).
- **Fila `Total:N`:** se ignora; si no coincide, advertencia (RF-5).
- **ID vacío, inválido, largo o repetido:** filas excluidas y listadas con su motivo; "ñ1" y "Ñ1" se excluyen juntas (RF-8, RF-9).
- **ID numérico `123.0` o con ceros (`0123`):** "123" y "0123" son caravanas distintas (RF-7).
- **Estado desconocido, dado de baja o vacío en `RPRO`:** entra sin estado; advertencia solo si el valor no está vacío (RF-11).
- **Mismo archivo importado de nuevo:** aviso y elección explícita; nunca se pisa un rodeo (RF-16).
- **Doble toque en Confirmar o confirmación concurrente:** un solo rodeo (RF-19).
- **Sin espacio para la copia previa o disco lleno al guardar:** no se importa y no queda nada (RF-17, RF-18).
- **Selector de archivos de Android:** el archivo llega como flujo (`content://`), no como ruta (RF-1).
- **El archivo original se mueve o borra después de importar:** no afecta, porque queda guardado dentro del rodeo (RF-18).
- **Cancelar en la vista previa, rotar la pantalla o pasar a segundo plano:** ver RF-22.
- **Varios veterinarios o tambos en la misma tablet:** el mismo número de caravana puede estar en rodeos distintos; el rodeo queda en el establecimiento elegido (RF-14, RF-18).
- **Lecturas repetidas, vaca fuera del rodeo:** no aplican a esta spec (son del trabajo de campo).

## Criterios de aceptación
- [ ] Un `.xls` con la estructura de la muestra se importa: se crea un rodeo con tantos animales como filas válidas, sin contar `Total:N`, y el archivo guardado coincide byte a byte con el elegido (RF-1, RF-5, RF-18).
- [ ] El archivo original no cambia (misma huella antes y después) (RF-1).
- [ ] Se rechaza con mensaje, sin dejar nada guardado, un archivo vacío, de más de 25 MB, corrupto, `.xlsx`, sin `ID` o `RPRO`, con encabezado duplicado, sin filas de datos o con 20.001 filas; el orden de validación es el de RF-2 (RF-2, RF-3).
- [ ] La tabla de RF-7 se cumple para cada tipo de celda, con su motivo; los motivos y su precedencia de RF-8 y RF-9 se verifican, incluido "ñ1"/"Ñ1" (RF-7, RF-8, RF-9).
- [ ] Con filas de datos pero ninguna válida, la vista previa se ve y no se puede confirmar (RF-13, RF-16).
- [ ] `Total:N` se reconoce en cualquier fila con las variantes de RF-5; fila totalmente vacía se saltea y fila sin `ID` pero con datos es error (RF-5).
- [ ] `RPRO` conocido se asigna; desconocido o de un estado dado de baja entra sin estado, con advertencia agrupada (valor, cantidad, hasta 5 filas); vacío entra sin estado ni advertencia (RF-10, RF-11).
- [ ] Las columnas originales se guardan como en RF-12 (texto, vacías omitidas, desconocidas incluidas), `COM` no va a observaciones y la fecha de último tacto queda vacía (RF-12).
- [ ] La vista previa muestra el resumen, errores, advertencias y conteo por estado (RF-13).
- [ ] Se puede elegir un establecimiento existente o crear uno; con ninguno, solo crear; nombres recortados, obligatorios, máximo 100; un establecimiento repetido da el error de RF-15; el nombre del rodeo se propone y se edita (RF-14, RF-15).
- [ ] Un archivo ya importado avisa con nombre y fecha del rodeo existente y deja cancelar o importar igual (RF-16).
- [ ] La confirmación pide la copia `APedido` primero; si falla o no hay espacio, no importa; la copia no desplaza a la previa a actualización (RF-17).
- [ ] Un fallo forzado a mitad de la confirmación no deja establecimiento, rodeo ni animales (RF-18, RF-20).
- [ ] Confirmar dos veces la misma vista previa, o a la vez, crea un solo rodeo; el botón se deshabilita mientras corre (RF-19).
- [ ] Cancelar descarta la vista previa sin guardar nada (RF-22).
- [ ] Ninguna parte de la importación usa la red: se revisan las dependencias de `Aviss.Excel` y `Application` (RF-23).
- [ ] Un test recorre todos los códigos de motivo y comprueba que sus mensajes no llevan datos de animales ni de tambos (RF-24).
- [ ] Un perfil falso demuestra que el formato se cambia sin tocar el resto (RF-25).
- [ ] Con 20.000 filas se cumplen los tiempos de RF-26 (tests con `Categoria=Rendimiento`).
- [ ] Hay muestras anonimizadas en `tests/muestras/`, generadas por código (ID inventados, mismas columnas y fila `Total`), incluidas las variantes mal formadas; no se commitea ningún Excel real.
- [ ] `dotnet build` y `dotnet test` en verde.

### Qué mirar en el emulador
- Elegir un `.xls` desde el selector de archivos de Android y ver la vista previa con resumen, conteo por estado, advertencias y errores por fila.
- Importar un archivo con filas malas y ver que se excluyen y se listan; confirmar y ver el resultado.
- Reimportar el mismo archivo y ver el aviso con nombre y fecha del rodeo anterior.
- Crear un establecimiento nuevo escribiendo el nombre; intentar repetirlo y ver el error.
- Tocar Confirmar varias veces seguidas: un solo rodeo.
- Rotar la pantalla y pasar a segundo plano durante la vista previa: se conserva. Cancelar: no queda nada.
- Un archivo que no es `.xls` (un `.xlsx` o un texto): mensaje claro.

## Decisiones
1. **Reimportar mismo archivo:** avisa y deja elegir; siempre crea un rodeo nuevo (RF-16).
2. **Estado desconocido:** la vaca entra sin estado, con advertencia; no se crean estados; un estado dado de baja cuenta como desconocido por simplicidad (RF-11).
3. **Filas con problemas:** se excluyen y se listan; los `ID` repetidos se excluyen todos (RF-8, RF-9).
4. **Establecimiento:** se elige o se crea después de la vista previa; un nombre repetido es error, no se reutiliza solo (RF-14, RF-15).
5. **Columnas:** solo `ID` y `RPRO` obligatorias; `COM` no va a observaciones; fechas como vienen (RF-3, RF-4, RF-12).
6. **Topes:** 25 MB y 20.000 filas, validados en el orden de RF-2.
7. **Vista previa en memoria:** se pierde si el sistema operativo cierra la app; no se persiste porque no vale la complejidad (RF-22).
8. **Sin segunda migración** ni índice sobre la huella (Alcance).

## Supuestos y riesgos
Para que el implementador los copie a `MEMORY.md`:
- **Códigos de Dairy Comp = nombre del estado:** sin validar (prueba 0.2); un código distinto hará que las vacas entren sin estado con advertencia; se corrige en el catálogo. Los acentos no se normalizan en la comparación de `RPRO`.
- **Caravana sin normalizar:** si el Excel real trae ceros inconsistentes, habrá que definir equivalencias. Además, si el bastón entrega 15 dígitos, la relación con el `ID` está por verlas (spec 005).
- **Tamaño de copias de seguridad:** cada importación agrega el `.xls` (hasta 25 MB) a la base y a sus 10 copias; revisar cuánto pesan en uso real.
- **Fechas como texto:** se guardan como las muestra la celda; la fidelidad al exportar (spec 004) hay que verificarla.
- **Solo hay una muestra real** (226 vacas) con supuestos sin validar en otros tambos: columnas con el mismo nombre repetido, encabezados siempre en la primera fila con contenido, `ID` único, formato de `FPART` y forma exacta de `Total:N`.
- **A verificar en el plan:** qué hace NPOI con un `.xlsx` renombrado a `.xls` y con un archivo protegido con contraseña (que rechace con error controlado, no con una excepción sin atrapar).
- **Segunda migración:** no hace falta, pero el riesgo abierto de reverificar la actualización con EF real queda para la próxima migración.

## Preguntas abiertas
Ninguna pendiente.
