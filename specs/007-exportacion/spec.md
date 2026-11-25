# 007 — Exportación: el archivo de vuelta para Dairy Comp

Estado: aprobada (2026-11-24; se cierran con lo recomendado: CSV con `;` y Windows-1252, guardar en Descargas con botón Compartir, sin columna de estado final, jornada = día local del registro vigente más reciente del rodeo, fila Total conservada con las columnas nuevas vacías, nombre `<original>_aviss_AAAAMMDD_HHmm.<ext>`; los nombres de columnas siguen provisorios)
Fecha: 2026-11-24

> **Numeración:** esta spec es la 007 (rama `spec/007-exportacion`). Parte de la base que dejan la 005 (eventos del catálogo con pasos, ya en `main`) y la 006 (sin veterinario ni tablet).

> **Decisiones del responsable incorporadas (2026-11-24):**
> - El Excel original **no se modifica nunca**. La exportación genera un archivo **nuevo** con **todas las columnas del original** más **columnas nuevas** agregadas al final. **Nunca se reemplaza una columna original** (ni `RPRO`).
> - Columnas nuevas obligatorias: **SCDAT** (fecha del tacto, `dd/mm/yyyy`), **SCTIM** (hora a la que pasó el animal, hora militar `HHmm`: 15:30 es `1530`) y **una columna fija de evento** con el **nombre** del evento elegido (OK, VACIA, Aborto, Preñada fecha correcta…; decisión P5 de la spec 005: nombre, no código).
> - **Respuestas del responsable del 2026-11-24:** la columna del evento se llama provisoriamente **`Evento1`** (en cada animal va el evento registrado; el nombre real que usa el Daily se consigue después); `SCDAT` y `SCTIM` quedan como están; `DIASPRE`, `DIASABO` y `RESABO` se mantienen como nombres provisorios (no los confirmó); `SCTIM` es la hora del **registro**; la vaca con CVET 0 y registro guardado salía vacía (**revertido el 2026-11-24**: ver RF-11 y pregunta 8); y **cada exportación es por rodeo importado y trae los datos del tacto realizado sobre ese rodeo**: un rodeo nuevo es un servicio de tacto nuevo con su propia exportación, sin acumular entre rodeos.
> - Además, columnas aparte para los **días de preñez informados** (Preñada fecha previa), los **días de aborto** y el **resultado del aborto** (Vacía OK, Inseminada, Seca, Fresca, Rechazo).
> - Las vacas que no pasaron llevan esas columnas **vacías**; las de CVET 0 también, salvo que se hayan tactado a propósito (ver RF-11).
> - **Decisión del 2026-11-24 (cierra la pregunta 10):** el archivo trae **solo el tacto hecho del día** (la jornada de tacto sobre ese rodeo); **no** se exportan tactos de días anteriores del mismo rodeo. Flujo de negocio: *se importa el rodeo, se hace el tacto, se exporta, listo*. Otro tacto (el mes siguiente, o excepcionalmente a los 15 días) repite el flujo completo con un rodeo importado nuevo. Si una vaca llegara a tener más de un registro en la jornada, la app no hace nada especial: sale el último vigente y los anulados se ignoran (un registro anulado no exporta nada).
> - El formato de salida (**CSV** o **.xls**, los dos que el sistema ya lee) se elige **al final**, en la pantalla de exportar, antes de generar.
> - Los datos salen de SQLite. Fecha y hora exactas están guardadas en UTC y se muestran en la hora local de la tablet. El estado final de la vaca **no** reemplaza `RPRO`.

## Contexto
El veterinario hace el tacto con Aviss, sin conexión, sobre el listado que exportó de Dairy Comp. Al terminar, ese trabajo tiene que **volver a Dairy Comp**: el archivo de vuelta lleva, para cada vaca que pasó, qué evento se le hizo, cuándo, y los datos extra de los eventos con pasos (días de preñez, días de aborto y resultado del aborto). Dairy Comp crea sus eventos al importar, así que el archivo tiene que decir lo mismo que el veterinario decidió en la manga.

Hasta ahora Aviss solo importa y guarda (specs 002 a 006). Esta spec cierra el ciclo. Lo usa el veterinario al terminar el trabajo (o al final de la jornada) en la tablet, de pie o sentado, en vertical (800x1340).

## Alcance
- Incluye:
  - Una pantalla "Exportar" para elegir el rodeo, ver un resumen y generar el archivo en el formato elegido (CSV o .xls).
  - Armar el archivo nuevo a partir del original guardado en el rodeo: mismas filas y mismas columnas, con los mismos valores, más las columnas nuevas.
  - Elegir, para cada vaca, su último registro vigente y volcarlo en las columnas nuevas.
  - Validar el archivo generado (reabrirlo y compararlo) antes de entregarlo.
  - Entregar el archivo: guardarlo en un lugar que el veterinario pueda encontrar y ofrecer compartirlo.
  - Dejar constancia de cada exportación (cuándo, nombre, formato, huella, cantidades) y marcar el rodeo como exportado.
- No incluye:
  - **Probar la reimportación en Dairy Comp** (prueba 0.2, a cargo del responsable; hoy bloqueada por permisos). La spec deja anotado el riesgo.
  - Reemplazar o corregir columnas del original, incluida `RPRO`. El estado final de la vaca **no** se exporta en esta spec (ver pregunta 6).
  - Exportar observaciones, marcas de pasada, esperas, mangas, cierres, historial ni el orden de las vacas.
  - Exportar quién hizo cada cosa o desde qué tablet (spec 006): no existe.
  - Agregar filas al archivo: las vacas dadas de alta a nivel animal (sin pantalla desde la 003) no figuran en el original y no se agregan.
  - Exportar tactos de días anteriores del mismo rodeo, filtrar por manga o elegir otra fecha, o juntar varios rodeos en un archivo.
  - Enviar nada por red desde la app (constitución §1). "Compartir" entrega el archivo al sistema de Android, que muestra sus opciones.
  - Sincronizar entre tablets.

## Definiciones
- **Archivo original:** el que se importó; vive completo dentro del rodeo (bytes y huella). Puede ser `.xls` o CSV/TXT.
- **Archivo de vuelta:** el archivo nuevo que genera esta spec.
- **Registro exportable:** un tacto normal vigente o un registro de evento vigente (OK, Revisar Nuevamente, Rechazo, Aborto con resultado Rechazo). No cuentan: tactos anulados, tactos de anulación, rechazos anulados, marcas de pasada ni esperas.
- **Jornada de tacto:** el día local (zona de la tablet) del registro exportable más reciente del rodeo. Solo los registros de ese día se exportan.
- **Último vigente:** dentro de la jornada, el registro exportable de la vaca con fecha y hora más recientes; si dos tienen la misma, el que se guardó después.
- **Columnas nuevas:** las que agrega la exportación (nombres en RF-4 a RF-8; los de las tres últimas son tentativos, ver preguntas).
- **Evento:** lo que el veterinario eligió en la ficha, por su **nombre** actual en el catálogo (el Rechazo fijo sale como `RECHAZO`, decisión del 2026-11-24; el resultado `Rechazo` del Aborto en su columna no cambia).
- **Resultado del aborto:** Vacía OK, Inseminada, Seca, Fresca o Rechazo (spec 005, P11).
- **Hora local:** la de la zona horaria de la tablet al momento de exportar.

## Requisitos funcionales

### Pantalla y flujo
- **RF-1** — Cuando el veterinario toca "Exportar" en la pantalla de inicio, el sistema deberá mostrar los rodeos activos, agrupados por establecimiento, con su cantidad de vacas.
- **RF-2** — Cuando el veterinario elige un rodeo, el sistema deberá mostrar un resumen antes de generar: cuántas vacas del archivo original tienen evento para exportar y cuántas no (el mismo criterio que la constancia), mostrar aparte, con una línea corta fija, las vacas con evento que no están en el archivo (altas en campo o ID repetido) cuando las haya, y, si ya se exportó antes, la fecha y hora de la última exportación y cuántas vacas llevaba.
- **RF-3** — El sistema deberá pedir el **formato (CSV o .xls) recién en la pantalla de exportar, antes de generar**, sin ninguno elegido de antemano, y no deberá generar nada hasta que el veterinario lo elija.

### El archivo de vuelta
- **RF-4** — El sistema deberá generar un archivo **nuevo** que conserve **todas las filas y todas las columnas del original**, en el mismo orden y con los mismos valores (incluidas las filas que la importación excluyó y la fila final `Total`), y que sume las columnas nuevas **a la derecha** de las originales. Nunca deberá reemplazar, reordenar, vaciar ni quitar una columna original, `RPRO` incluida. **(modificado 2026-11-24)** Única excepción: `SCDAT` y `SCTIM`. Dairy Comp SIEMPRE las exporta y marcan el último tacto; si el original las trae (sin distinguir mayúsculas), el sistema deberá **reemplazar** su valor únicamente en las filas de las vacas con tacto del día (las que reciben valores en RF-6 a RF-10, la de CVET 0 tactada a propósito incluida) y dejar **todas las demás filas exactamente como venían**, sin agregar una segunda columna con ese nombre. Si el original NO trae `SCDAT` o `SCTIM`, el sistema deberá **crearlas** a la derecha, con valor solo en las vacas con tacto, y **avisar** en la pantalla de resultado que esa columna faltaba (el aviso nombra solo la columna, nunca datos del archivo).
- **RF-5** — El sistema deberá armar el archivo a partir de la **copia del original guardada en el rodeo**, sin volver a abrir ni modificar el archivo que eligió el veterinario (spec 002, constitución §2).
- **RF-6** *(modificado 2026-11-24: si el original ya trae `SCDAT`, se reemplaza su valor en esas vacas en vez de agregar una columna; ver RF-4)* — El sistema deberá agregar, para cada vaca con registro exportable, la columna **SCDAT** con la **fecha** del registro en formato `dd/mm/yyyy` (por ejemplo `05/11/2026`), en hora local.
- **RF-7** *(modificado 2026-11-24: igual que RF-6 para `SCTIM`)* — El sistema deberá agregar la columna **SCTIM** con la **hora** del registro en hora militar `HHmm`, con ceros a la izquierda y sin separadores (15:30 es `1530`; 09:05 es `0905`; 00:10 es `0010`), en hora local.
- **RF-8** — El sistema deberá agregar una columna fija de **evento**, de nombre provisorio **`Evento1`**, con el **nombre** del evento elegido, tal como está en el catálogo en ese momento (por ejemplo `OK`, `VACIA`, `Aborto`, `Preñada fecha correcta`, `Preñada fecha previa`, `Revisar Nuevamente`, `RECHAZO`, o un evento creado a mano). No deberá exportar el código de Dairy Comp en esa columna.
- **RF-9** — Cuando el evento es **Preñada fecha previa**, el sistema deberá agregar los **días de preñez informados** en su propia columna (número entero). En cualquier otro evento esa columna queda vacía.
- **RF-10** — Cuando el evento es **Aborto**, el sistema deberá agregar los **días de aborto** (número entero, puede ser 0) y el **resultado del aborto** (Vacía OK, Inseminada, Seca, Fresca o Rechazo) en columnas propias. En cualquier otro evento esas dos columnas quedan vacías.
- **RF-11** — El sistema deberá dejar **vacías todas las columnas nuevas** (sin ningún texto) en las vacas que no pasaron (incluidas las de **CVET 0** sin registro vigente en la jornada, spec 003 RF-40), en las filas del original que la importación excluyó y en la fila `Total`. Una vaca de **CVET 0 que se tactó a propósito** (spec 009: se puede abrir su ficha) y tiene registro vigente en la jornada **sale como cualquier otra**, con `SCDAT`, `SCTIM`, el evento y demás columnas (decisión del 2026-11-24). La jornada se calcula sobre todos los registros vigentes del rodeo y el resumen no distingue las vacas de CVET 0.
- **RF-12** — El sistema deberá exportar **solo los registros de la jornada de tacto** del rodeo (no los de días anteriores). De cada vaca exportará su **último vigente** de esa jornada y deberá **ignorar los anulados**: si el único registro de la vaca está anulado, sus columnas nuevas quedan vacías; si fue corregido con "Editar", sale el registro nuevo.
- **RF-13** — El sistema deberá exportar el **estado final** de la vaca **sin tocar `RPRO`**: la columna `RPRO` sale exactamente como estaba en el original.
- **RF-14** *(modificado 2026-11-24: no aplica a `SCDAT` ni `SCTIM`, que se reemplazan según RF-4)* — Si el original ya trae una columna con el mismo nombre que alguna columna nueva **distinta de `SCDAT` y `SCTIM`** (`Evento1`, `DIASPRE`, `DIASABO`, `RESABO`; sin distinguir mayúsculas), entonces el sistema deberá **rechazar la exportación** con un mensaje que nombre esa columna, sin generar ningún archivo y sin cambiar nada en la base.

### Formatos
- **RF-15** — Cuando el formato elegido es **CSV**, el sistema deberá generar un archivo de texto que, si el original también es CSV/TXT, conserve cada línea original **tal como estaba** (codificación, separador, comillas, espacios de relleno, decimales, saltos de línea y el separador final) agregando solo los campos nuevos; y, si el original es `.xls`, lo escriba con las convenciones del CSV de Dairy Comp conocido (ver preguntas 3 y 9).
- **RF-16** — Cuando el formato elegido es **.xls**, el sistema deberá generar un libro con una hoja y los mismos valores del original (si el original es CSV, los valores tal como los lee la importación). Las columnas nuevas de fecha y de hora se escriben como **texto**, para que `05/11/2026` y `0905` salgan exactamente así.
- **RF-17** — El sistema deberá poder generar **cualquiera de los dos formatos** a partir de **cualquiera de los dos tipos de original** (CSV a .xls, .xls a CSV, y el mismo formato).

### Validación y entrega
- **RF-18** — Antes de darlo por bueno, el sistema deberá **reabrir el archivo generado** con las mismas reglas con que se lee un listado de Dairy Comp y comprobar que: tiene las mismas filas que el original; cada columna original tiene los mismos valores; están las columnas nuevas; y cada fila con registro tiene los valores esperados y cada fila sin registro las columnas nuevas vacías. Si algo no coincide, deberá **no entregar** el archivo, avisar con un mensaje claro y no registrar la exportación.
- **RF-19** — Cuando el archivo está validado, el sistema deberá **guardarlo** en un lugar del dispositivo que el veterinario pueda encontrar, con un **nombre nuevo** derivado del original (nombre original, marca de Aviss, fecha y hora local), que **no pise** ningún archivo existente, y mostrar ese nombre y dónde quedó.
- **RF-20** — El sistema deberá ofrecer **compartir** el archivo generado (mandarlo a otra aplicación del dispositivo) desde la pantalla de resultado. La app no deberá hacer ninguna llamada de red.
- **RF-21** — Cuando se entregó el archivo, el sistema deberá **dejar constancia** de la exportación (rodeo, fecha y hora exactas, nombre, formato, huella SHA-256 del archivo, cantidad de filas y de vacas con evento) y **marcar el rodeo como exportado**. Esa constancia solo se agrega: nunca se modifica ni se borra.
- **RF-22** — Cada exportación es **por rodeo importado** y trae el tacto de la **jornada** de ese rodeo (RF-12); no mezcla ni acumula datos de otros rodeos ni de otras jornadas (un rodeo importado nuevo es un servicio de tacto nuevo, con su propia exportación). El sistema no deberá borrar ni cambiar nada del trabajo al exportar (spec 001) y deberá permitir volver a exportar el rodeo si hiciera falta.

### Errores y límites
- **RF-23** — Si el original guardado en el rodeo ya no se puede leer, o el rodeo no existe o está dado de baja, entonces el sistema deberá avisar con un mensaje claro, sin datos del tambo, y no generar nada.
- **RF-24** — Si no se puede guardar el archivo (sin espacio, sin permiso, falla de escritura), entonces el sistema deberá avisarlo con un mensaje claro, **no dejar un archivo incompleto visible** y no registrar la exportación.
- **RF-25** — El sistema deberá armar el archivo con los datos de **un mismo momento** (no mezclar lo guardado antes y después de empezar) y no deberá impedir ni frenar que se siga guardando trabajo en otra pantalla.
- **RF-26** — El sistema deberá generar el archivo de un rodeo de 20.000 vacas con sus registros dentro del tope de tiempo que fija el plan (el mismo que ya se usa para importar), mostrando que está trabajando y sin congelar la pantalla.
- **RF-27** — El sistema deberá funcionar sin conexión (spec 003 RF-32), no deberá incluir caravanas, nombres de tambo ni datos de animales en errores ni logs (spec 003 RF-33), y no deberá poner en el archivo ningún dato de veterinario ni de tablet (spec 006 RF-17).
- **RF-28** — La pantalla deberá cumplir la constitución §9: botones grandes, alto contraste, vertical, una mano; y deberá poder volver atrás sin generar nada.

## Casos límite
- **Sin conexión:** todo es local; compartir solo entrega el archivo a Android (RF-20, RF-27).
- **Corte de energía o cierre de la app mientras se genera o se guarda:** el archivo se arma y valida en memoria y se guarda recién después; mientras se escribe no es visible, así que no queda un archivo a medias que parezca bueno. La constancia se agrega solo cuando el archivo ya quedó guardado. Un corte antes de eso no cambia nada en la base; al reintentar, se genera de nuevo (RF-21, RF-24).
- **Corte justo entre guardar el archivo y dejar la constancia:** puede quedar un archivo en la tablet sin constancia en la base. No se pierde trabajo; al exportar de nuevo sale otro archivo (RF-22).
- **Excel mal formado:** el original se validó al importar. Si hoy no se pudiera leer (copia dañada), se avisa y no se genera nada (RF-23). Filas excluidas al importar (ID vacío, inválido o repetido) salen **tal cual** con las columnas nuevas vacías (RF-4, RF-11).
- **ID repetido en el original:** la importación excluyó **todas** sus filas; en el archivo de vuelta salen todas con las columnas nuevas vacías (RF-11).
- **Lecturas repetidas / registro corregido:** un registro corregido con "Editar" (anulado + nuevo) exporta el nuevo; los anulados no exportan nada (RF-12). Sin lógica extra para más de un registro de la misma vaca en la jornada: sale el último vigente.
- **Vaca que no está en el rodeo (alta a nivel animal):** no figura en el original y no se agrega fila (alcance). La pantalla puede avisar cuántas quedaron afuera si tienen registro.
- **CVET 0 con un registro guardado** (la spec 009 permite abrir su ficha y tactarla a propósito): sale con sus columnas como cualquier otra vaca (RF-11, decisión del 2026-11-24, que reemplaza a la de dejarla vacía); la de CVET 0 sin registro sale vacía.
- **Vaca marcada pero sin evento (Saltar, En Espera sin resolver):** sale sin evento, columnas vacías (RF-11).
- **Aborto con resultado Rechazo:** sale con evento Aborto, los días y el resultado `Rechazo`; `RPRO` no se toca (RF-10, RF-13).
- **Hora y zona horaria:** se convierte de UTC a la zona de la tablet **al exportar**; el cambio de horario de verano no duplica ni pierde un registro (RF-6, RF-7).
- **Columna con nombre repetido:** si el original ya trae `Evento1`, `DIASPRE`, `DIASABO` o `RESABO`, se rechaza antes de generar (RF-14). *(modificado 2026-11-24)* `SCDAT` y `SCTIM` ya no se rechazan: se reemplazan (RF-4).
- **Varios tambos en la misma tablet:** la exportación es por rodeo; el nombre del archivo no mezcla tambos (RF-19).
- **Varias tablets o veterinarios sobre el mismo tambo:** cada tablet exporta su propio archivo con lo que tiene; el archivo no dice de cuál tablet vino ni quién tactó (spec 006 RF-18).
- **Otro tacto (otro mes o a los 15 días):** se repite el flujo con un rodeo importado nuevo; su exportación no trae nada del rodeo anterior (RF-12, RF-22).
- **Rodeo reseteado (RF-50 de la 003, temporal):** el rodeo viejo queda dado de baja y no se ofrece para exportar; se exporta el nuevo.
- **Rodeo muy grande (20.000 vacas, 25 MB):** debe entrar en el tope de tiempo y de memoria del plan (RF-26).
- **Archivo con campos entre comillas y saltos de línea dentro de un campo (CSV):** se conservan sin romper la fila (RF-15).

## Criterios de aceptación
- [x] La pantalla "Exportar" lista los rodeos activos por establecimiento, muestra el resumen y pide el formato recién al final (RF-1 a RF-3, RF-28).
- [x] El archivo de vuelta de la muestra de 226 vacas (`.xls`) y el de la de 282 (CSV) tienen todas las filas y columnas del original con los mismos valores, `RPRO` incluida, y las columnas nuevas a la derecha (RF-4, RF-13).
- [x] SCDAT sale `dd/mm/yyyy` y SCTIM `HHmm` con ceros (`0905`), en hora local, y con un registro cerca de la medianoche la fecha es la local (RF-6, RF-7).
- [x] La columna de evento lleva el nombre; Preñada fecha previa lleva los días de preñez; Aborto lleva días y resultado con sus cinco variantes (RF-8 a RF-10).
- [x] Vacas sin registro (CVET 0 incluidas), filas excluidas y la fila Total salen con las columnas nuevas vacías; una vaca de CVET 0 con registro vigente sale con sus valores (RF-11).
- [x] Solo salen los registros de la jornada del rodeo (los de un día anterior no); un registro anulado no exporta nada y uno corregido con "Editar" exporta el nuevo (RF-12).
- [x] Un original con una columna llamada igual que una nueva se rechaza sin generar nada (RF-14).
- [x] Se generan los cuatro cruces (CSV a CSV, CSV a .xls, .xls a CSV, .xls a .xls) y cada uno pasa la validación; en CSV a CSV cada línea original queda byte por byte igual (RF-15 a RF-18).
- [x] Un archivo que no pasa la validación no se entrega ni se registra (RF-18).
- [x] El archivo queda guardado con nombre nuevo sin pisar otro y se puede compartir; queda la constancia y el rodeo figura exportado; exportar de nuevo funciona y se puede seguir tactando (RF-19 a RF-22).
- [x] Sin espacio o con falla de escritura: mensaje claro, sin archivo visible a medias, sin constancia (RF-24).
- [x] 20.000 vacas en el tope de tiempo del plan (RF-26).
- [x] Ningún log ni mensaje de error trae caravanas ni nombres de tambo; el archivo no trae veterinario ni tablet (RF-27).
- [ ] **A verificar en el emulador o la tablet (800x1340 vertical):** la pantalla, el guardado en el lugar elegido, el compartir y abrir el archivo generado en otra aplicación (RF-19, RF-20, RF-28).
- [x] `dotnet build` y `dotnet test` en verde.
- [ ] **Pendiente de la prueba 0.2:** que Dairy Comp acepte el archivo con las columnas nuevas (ver pregunta 2). No bloquea la spec.

## Preguntas abiertas
(Sin decidir; cada una con la recomendación. El plan trabaja con la recomendación como valor provisional, aislada en un solo lugar para cambiarla sin tocar el resto.)

1. **Nombres exactos de las columnas nuevas.** *Resuelta en parte (2026-11-24):* SCDAT y SCTIM fijos; la del evento es provisoriamente `Evento1` (el nombre real del Daily se consigue después). **Sigue abierto:** el nombre definitivo del evento y los de días de preñez, días de aborto y resultado del aborto, que se mantienen provisorios como `DIASPRE`, `DIASABO` y `RESABO` (cortos, estilo Dairy Comp) sin estar confirmados.
2. **¿Dairy Comp acepta cabeceras adicionales** que no son campos suyos, o las ignora, o rechaza el archivo? *Recomendación:* probarlo en la prueba 0.2 apenas haya permiso. La spec no cambia si las ignora; si las rechaza hay que decidir otra forma de entregar esos datos (otra spec).
3. **CSV: codificación y separador.** *Recomendación:* los del CSV real de Dairy Comp: `;`, Windows-1252, encabezados entre comillas, un `;` al final de cada línea y saltos de línea de Windows. Si el original ya es CSV, se respeta tal cual lo que traía.
4. **Dónde se guarda y cómo se comparte en Android.** *Recomendación:* guardar en la carpeta **Descargas** (sin pedir permisos especiales) y ofrecer un botón **Compartir** que abre las opciones de Android (Drive, mail, WhatsApp, cable).
5. **Vacas exportadas dos veces / alcance.** *Resuelta (2026-11-24), textual: "que cada exportación traiga la información del tacto que se realizó; cuando se carga otro rodeo se hace el servicio de tacto y se carga la info nueva de ese tacto".* Interpretación: exportación por rodeo importado, sin acumular entre rodeos; la pantalla avisa de la última exportación del mismo rodeo. El alcance por día quedó cerrado en la pregunta 10.
6. **¿El estado final (VACIA, INSEMIN, PREÑADA…) va en otra columna?** `RPRO` no se toca. *Recomendación:* **no** en esta spec: el evento ya dice qué pasó y Dairy Comp calcula el estado. Si hace falta, se suma después una columna más (sin cambios en la base).
7. **Resuelta (2026-11-24):** "hora a la que pasó el animal" = la hora del **registro** (cuando el veterinario confirmó con Entrar), no la de la marca de pasada.
8. **Resuelta (2026-11-24), con revisión el mismo día:** primero la vaca con CVET 0 y registro guardado salía vacía y el resumen contaba cuántas había. **Nota 2026-11-24:** como la spec 009 permite tactar a propósito una vaca de CVET 0, el responsable decidió que **sale como cualquier otra** (con SCDAT, SCTIM, Evento1 y demás); el resumen ya no tiene la línea "CVET 0 con registro" y la de CVET 0 sin registro sigue vacía.
9. **.xls a CSV: números decimales y fechas.** Cuando el original es `.xls` y se pide CSV, ¿coma o punto decimal? *Recomendación:* punto, y las fechas como las muestra la celda. A confirmar con una vaca real (el CSV de Dairy Comp conocido trae enteros y fechas `dd/mm/aa`).
10. **Resuelta (2026-11-24):** el archivo trae solo el tacto hecho del día (la jornada); no se exportan tactos de días anteriores del mismo rodeo (ver la decisión al principio).
11. **Fila `Total`.** *Recomendación:* se conserva tal cual, con las columnas nuevas vacías.
12. **Nombre del archivo.** *Recomendación:* `<nombre original sin extensión>_aviss_AAAAMMDD_HHmm.<csv|xls>`.

## Cambios posteriores 2026-11-24
El Rechazo fijo sale en la columna de evento como `RECHAZO` (mayúsculas, igual que en pantalla; `EventosCampo.NombreRechazo`). El resultado `Rechazo` de un Aborto en su columna de resultado queda como estaba (es una etiqueta de resultado, junto a Vacía OK, Inseminada, Seca y Fresca).
