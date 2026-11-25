# 010 — EID (identificador electrónico) en la importación

Estado: aprobada (2026-11-24; con las recomendaciones: un EID inválido se descarta y se avisa, un EID repetido en el archivo deja a todos sus animales sin EID y se avisa, un rodeo ya importado no se actualiza: el archivo con EID se importa como rodeo nuevo; SCDAT y SCTIM del archivo real quedan fuera de esta spec)
Fecha: 2026-11-24

## Contexto
El bastón Tru-Test XRS2 lee el **EID** de la caravana (15 dígitos, por ejemplo `982 000458467xxx`), no el número corto que figura en el `ID` de Dairy Comp. Hasta hoy el Excel que se importaba no traía el EID, así que Aviss lo ignoraba: el animal se encontraba solo por el número corto, tipeado a mano.

El padre del responsable ahora pasa un CSV de Dairy Comp con una columna **EID** (la segunda, después de `ID`). Archivo de referencia (el real **no se commitea**; los tests usan una muestra inventada): separador `;`, Windows-1252, campos con espacios de relleno, `;` final, encabezado `"ID";"EID";"CVET";"LACT";"FPART";"DEL";"FCELO";"UTORO";"RPRO";"FAPAR";"LECHE";"SCDAT";"SCTIM"` y fila final `Total:;282`. Trae 282 animales; el EID tiene 15 dígitos sin ceros a la izquierda ni guiones, y 10 animales (vacas frescas recién paridas) traen `-` (todavía sin EID).

Esta spec hace que la importación **guarde el EID de cada animal** y que, al leer con el bastón, la vaca se encuentre por su EID con el mismo comportamiento que por su número. Lo usa el veterinario en la manga: apunta al bastón y la ficha abre sin tipear nada.

## Alcance
- Incluye:
  - Reconocer la columna `EID` del archivo al importar y guardarla como identificador electrónico del animal.
  - Qué pasa con EID vacíos, con `-`, inválidos o repetidos dentro del archivo.
  - Mostrar en el resumen de importación cuántos animales traen EID y cuántos no.
  - Que la lectura del bastón (o la búsqueda a mano) con un EID encuentre al animal y se comporte igual que la lectura por número.
  - Qué pasa al importar otra vez, o un rodeo con otros EID.
  - Que la ficha muestre el EID una sola vez.
  - Verificar que la exportación no se rompe por una columna EID de más.
  - Una muestra anonimizada para los tests.
- No incluye:
  - **`SCDAT` y `SCTIM` del archivo:** el archivo real ya trae esas dos columnas y la exportación de la spec 007 también agrega columnas con esos nombres. Hoy eso hace fallar la exportación de este archivo. **Se ignoran en esta spec y el conflicto queda pendiente, a resolver aparte (decisión del responsable, 2026-11-24).**
  - Completar el EID de rodeos ya importados (no hay actualización ni fusión de rodeos; ver RF-12).
  - Cargar un archivo aparte de pares ID-EID (el padre también puede pasarlo; es otra spec si hace falta).
  - Editar el EID desde una pantalla, o cambiar cómo el bastón entrega el número (spec 008).
  - Un filtro de lecturas repetidas por tiempo (spec 008).
  - Cambios en la base de datos: el animal ya tiene el campo y su regla de unicidad por rodeo (spec 001).

## Definiciones
(Las de las specs 002, 003 y 009 valen acá.)
- **EID válido:** exactamente **15 dígitos** (del 0 al 9), una vez quitados los espacios.
- **Sin EID:** celda vacía, solo espacios o un guion (`-`).
- **EID inválido:** cualquier otro valor: letras, menos o más de 15 dígitos, decimales, fechas, etc.
- **Número corto:** el `ID` de Dairy Comp, la caravana (spec 001).

## Requisitos funcionales

### Importar el EID
- **RF-1** — Cuando el archivo trae una columna de encabezado `EID` (sin distinguir mayúsculas ni espacios de los extremos, en cualquier posición), el sistema deberá guardar, en cada animal importado, el valor de esa columna como su identificador electrónico, sin espacios.
- **RF-2** — Cuando el valor de la columna `EID` es un **EID válido**, el sistema deberá guardarlo tal cual (sin espacios), tanto si la celda es texto (`982 000458467123` o `982000458467123`) como si es un número entero.
- **RF-3** — Cuando la celda `EID` está vacía, tiene solo espacios o es `-`, el sistema deberá importar el animal **sin EID y sin ninguna advertencia** (es el caso normal de una vaca recién parida).
- **RF-4** — Si el valor de `EID` es **inválido**, entonces el sistema deberá importar el animal igual **sin EID** y mostrar una advertencia agrupada con la cantidad de filas y a lo sumo 5 números de fila de ejemplo. La advertencia no muestra el valor (spec 002 RF-24). *(Ver pregunta 1.)*
- **RF-5** — Si dos o más filas válidas traen el **mismo EID**, entonces el sistema deberá importar todos esos animales **sin EID** (no se sabe cuál es el correcto) y mostrar una advertencia agrupada con la cantidad de filas y a lo sumo 5 números de fila de ejemplo. *(Ver pregunta 2.)*
- **RF-6** — El sistema **no deberá** excluir una fila ni rechazar el archivo por culpa del EID: el animal entra o no según su `ID` (spec 002 RF-8 y RF-9). Una fila excluida por su `ID` no cuenta para detectar EID repetidos.
- **RF-7** — Cuando el archivo **no trae** columna `EID`, el sistema deberá importarlo como hasta ahora: sin EID, sin advertencia nueva y sin cambiar el resumen.
- **RF-8** — Cuando el archivo trae columna `EID`, el sistema deberá mostrar en la vista previa y en el resultado de la importación **cuántos animales se importan con EID y cuántos sin EID** (los que traían `-`, vacío, inválido o repetido cuentan como sin EID).
- **RF-9** — El sistema deberá seguir guardando **todas** las columnas del archivo tal cual en los datos originales del animal, incluidas `EID`, `SCDAT` y `SCTIM`, y no deberá usar `SCDAT` ni `SCTIM` para nada ni avisar por ellas. El archivo original no se modifica (spec 002 RF-1).

### Importar de nuevo
- **RF-10** — El identificador electrónico deberá ser único **dentro de un rodeo**, no en toda la tablet: importar un archivo cuyos EID ya existen en otro rodeo (el mismo archivo otra vez, confirmando la advertencia de la spec 002 RF-16, o un rodeo nuevo con otros EID) no deberá chocar ni cambiar el otro rodeo.
- **RF-11** — Cada importación deberá crear un **rodeo nuevo** (spec 002): el sistema no deberá modificar los animales de un rodeo ya importado. Un rodeo importado antes de esta spec queda sin EID hasta que se importe el archivo con la columna `EID` como rodeo nuevo.
- **RF-12** — Cuando el veterinario necesita el EID de un rodeo ya importado, el sistema deberá permitirle importar el archivo nuevo como un rodeo nuevo, sin pedirle borrar ni dar de baja el viejo (el archivo es distinto, así que no salta el aviso de archivo repetido de la spec 002 RF-16). *(Ver pregunta 3.)*

### Leer con el bastón
- **RF-13** — Cuando el bastón entrega (o el veterinario busca a mano) un código que no coincide con ningún número corto del rodeo en trabajo pero sí con el **EID** de un animal no anulado de ese rodeo, el sistema deberá tratarlo como la lectura de ese animal, **con el mismo comportamiento que la lectura por número**: marca de pasada automática, ficha, vaca con CVET 0 (se marca y suena la chicharra), vaca ya procesada hoy ("Animal repetido" o "Ya pasó"), vaca en espera, y el aviso de la spec 009 con una ficha abierta.
- **RF-14** — El sistema deberá encontrar al animal con el EID **con o sin espacios** en el código leído (`982 000458467123` o `982000458467123`).
- **RF-15** — Si un código coincide a la vez con el número corto de un animal y con el EID de otro del mismo rodeo, entonces el sistema deberá elegir **el animal del número corto** (regla que ya fija la spec 003 RF-11). Es un caso casi imposible con EID de 15 dígitos; queda definido y probado para que no dependa del azar.
- **RF-16** — Cuando un animal **no tiene EID** (venía con `-`, tiene el EID descartado o es de un rodeo viejo), el sistema deberá seguir encontrándolo por su número corto y por la búsqueda a mano, sin cambios. Si el código no corresponde a ningún número ni EID del rodeo, se muestra "Caravana no encontrada" como siempre (spec 003 RF-24).

### Ficha y exportación
- **RF-17** — El sistema deberá mostrar el EID del animal **una sola vez** en la pestaña Datos de su ficha (fila `EID`), y no deberá repetirlo como una columna más del Excel. Si el animal no tiene EID, no deberá mostrar esa fila.
- **RF-18** — Cuando se exporta un rodeo cuyo archivo original trae la columna `EID`, el sistema deberá conservarla sin tocar su contenido y agregar las columnas nuevas a la derecha como siempre (spec 007), sin tratar `EID` como columna repetida ni fallar la validación del archivo generado.

### Transversales
- **RF-19** — El sistema no deberá usar la red, ni incluir caravanas, EID ni nombres de tambos en registros de error o logs, ni registrar o mostrar qué veterinario ni qué tablet (spec 006). En pantalla solo pueden verse números de fila, cantidades y el nombre del archivo (spec 002 RF-24).
- **RF-20** — Con 20.000 filas, la lectura y validación deberán seguir tardando menos de 10 segundos y la confirmación menos de 15 segundos (spec 002 RF-26), contando la detección de EID repetidos.

## Efecto sobre las specs anteriores
Al aprobarse, se agrega una nota "(modificado por la 010)" a lo que sigue; no se reescribe.

| Spec y requisito | Qué dice hoy | Qué cambia |
|---|---|---|
| **002 RF-12** | "...el identificador electrónico queda vacío". | Queda vacío **salvo** que el archivo traiga la columna `EID` con un valor válido (RF-1 a RF-6). |
| **002 RF-4 y RF-13** | Columnas opcionales con advertencia si faltan; vista previa con el detalle por estado. | `EID` **no** se suma a las opcionales con advertencia (RF-7). La vista previa agrega la línea de EID (RF-8). |
| **003 RF-11 y su supuesto** | El código leído se trata como número de caravana y después como EID (a confirmar con el XRS2). | Con el EID guardado en el animal, el segundo paso ya tiene datos (RF-13). La prioridad no cambia (RF-15). Lo que el bastón entregue realmente se confirma en la spec 008. |
| **007 (exportación)** | Agrega `SCDAT`, `SCTIM`, `Evento1`, `DIASPRE`, `DIASABO` y `RESABO`. | Sin cambios por el EID (RF-18). **Pendiente aparte:** el archivo real trae `SCDAT` y `SCTIM`, que chocan con esos nombres (ver Supuestos y riesgos). |

## Casos límite
- **Sin conexión:** no usa la red (RF-19).
- **Corte de energía a mitad de la importación:** la importación es una sola transacción (spec 002 RF-18): se guarda todo o nada, incluidos los EID.
- **Excel mal formado:** una columna `EID` repetida (`EID` y `eid`) rechaza el archivo como cualquier otra columna repetida (spec 002 RF-3). Un EID que no se entiende (celda de fecha, fórmula, error, decimal, negativo o con letras) es EID inválido (RF-4), nunca un rechazo.
- **EID con 16 o más dígitos o con letras:** inválido; el animal entra sin EID y con advertencia (RF-4).
- **EID en celda numérica de un `.xls`:** vale si es entero de 15 dígitos (RF-2). Un EID que empezara con cero perdería ese cero en una celda numérica y quedaría de 14 dígitos (inválido); el archivo real no tiene ceros a la izquierda y en un CSV el cero se conserva como texto.
- **Archivo con la columna `EID` toda en `-`:** se importa sin advertencias y el resumen dice 0 con EID.
- **Lectura repetida:** la misma etiqueta leída varias veces ya está cubierta por "ya procesada" (spec 003 RF-22), también por EID.
- **Vaca no encontrada:** un EID que no está en el rodeo en trabajo (de otra manga, de otro tambo o de un animal anulado) da "Caravana no encontrada" (RF-16).
- **Varios veterinarios o tambos en la misma tablet:** el EID es único por rodeo (RF-10) y la búsqueda mira solo el rodeo en trabajo; la app no registra veterinario ni tablet (spec 006).
- **Coincidencia número corto / EID:** gana el número corto (RF-15).

## Criterios de aceptación
- [x] Importar la muestra con EID (282 animales, 10 con `-`) deja **272 con EID y 10 sin EID**, sin advertencias por los EID (RF-1, RF-2, RF-3, RF-8).
- [x] La vista previa y el resultado dicen "con EID / sin EID" con esos números; en un archivo sin columna `EID` no aparece esa línea ni cambia nada (RF-7, RF-8).
- [x] Un EID con espacios, un EID numérico de un `.xls`, un valor con letras, uno de 14 o 16 dígitos y dos filas con el mismo EID dan lo definido en RF-2, RF-4 y RF-5, y el animal siempre se importa (RF-6).
- [x] Un EID repetido solo con una fila excluida por `ID` no genera advertencia de repetido (RF-6).
- [x] Todas las columnas del archivo (incluidas `EID`, `SCDAT` y `SCTIM`) quedan en los datos originales; `SCDAT` y `SCTIM` no generan advertencias (RF-9).
- [x] Importar el mismo archivo dos veces (confirmando) o dos rodeos con los mismos EID no choca, y el primer rodeo no cambia (RF-10, RF-11).
- [x] Con el rodeo importado, leer un animal por su EID (con y sin espacios) marca la pasada y abre la ficha igual que por número; con CVET 0 suena la chicharra y no abre ficha; ya procesada da el aviso; en espera abre su ficha; con una ficha abierta aparece el aviso de la 009 (RF-13, RF-14).
- [x] Un código que coincide con el número corto de un animal y el EID de otro abre el del número corto (RF-15).
- [x] Un animal sin EID se encuentra por número y a mano; un código desconocido da "Caravana no encontrada" (RF-16).
- [x] La ficha de un animal con EID muestra **una** fila `EID`; sin EID no muestra ninguna (RF-17).
- [x] Exportar un rodeo cuyo original trae `EID` conserva esa columna y agrega las nuevas a la derecha, y el archivo generado pasa la validación (RF-18).
- [x] Sin migración nueva: `dotnet ef migrations has-pending-model-changes` sin cambios (alcance).
- [x] Importar 20.000 filas con EID cumple los tiempos de la spec 002 (RF-20).
- [ ] **A verificar en la tablet** (no con tests): importar el CSV real, ver el resumen con EID y leer una vaca con el simulador o la búsqueda por EID (ver `tasks.md`, T3) (RF-8, RF-13, RF-14, RF-17).
- [x] `dotnet build` y `dotnet test` en verde.

## Decisiones
**Del responsable (2026-11-24):**
1. Este CSV es el archivo que se importa y el foco es el EID.
2. `SCDAT` y `SCTIM` del archivo se **ignoran** en esta spec; el conflicto con la exportación de la 007 se resuelve aparte.

**Cerradas con criterio del planificador (revisables):**
3. **EID válido = 15 dígitos exactos.** Es el formato ISO del archivo real (`982 000...`). Se acepta con espacios porque algunos lectores lo muestran agrupado y el campo ya se guarda sin espacios.
4. **`-` y vacío son "sin EID", no un error:** así lo manda Dairy Comp para las vacas recién paridas; avisar por cada una llenaría la vista previa de ruido.
5. **El EID nunca excluye al animal:** el `ID` manda; el EID es un dato extra (RF-6).
6. **Sin EID, y no el EID raro:** un EID inválido o repetido se descarta pero el valor original queda en los datos originales del animal, así no se pierde y se puede revisar (preguntas 1 y 2).
7. **El EID no se suma a las columnas opcionales con advertencia:** los archivos viejos (sin EID) siguen importándose como antes y sin ruido (RF-7).
8. **Un rodeo viejo no se actualiza** (RF-11): coherente con la spec 002 (no hay fusión) y con "los animales no se modifican" salvo por tacto.

## Supuestos y riesgos
- **Exportación del archivo real (pendiente, fuera de esta spec):** el archivo trae `SCDAT` y `SCTIM` y la exportación de la 007 también los agrega. Con el archivo real, la exportación avisa de columna repetida (spec 007 RF-14) y no genera nada. Hay que resolverlo (por ejemplo, completar esas columnas en lugar de agregar nuevas, o renombrar las de Aviss) antes de usar la exportación con este archivo. Esta spec no lo toca ni lo prueba.
- **Qué entrega el bastón (spec 008):** el supuesto es que el XRS2 manda el EID de 15 dígitos, con o sin espacios. Si manda otra cosa (por ejemplo, el EID con otro formato o con el número corto), se ajusta en la spec 008. **(modificado por la 008, 2026-11-24)** Confirmado con el spike: el bastón entrega el EID como `982 000458467499` (15 dígitos con un espacio después de `982`) más CRLF. El lector de la 008 le quita los espacios y entrega los 15 dígitos; todo lo que no sea una línea de 15 dígitos se ignora en silencio. Lo que falte (otro formato en algún botón) se ajusta en el intérprete con una lectura real.
- **EID de 16 o más dígitos en un CSV:** el lector del CSV convierte los enteros sin cero a la izquierda en números; hasta 15 dígitos no pierde nada. Con 16 o más pierde precisión y queda inválido (con advertencia). El archivo real tiene 15.
- **Archivos reales:** el CSV real no se copia al repo; los tests usan una muestra con números inventados (prefijo `999`, sin relación con ningún animal real).
- **Tamaño (constitución §10, máximo 5 tareas):** entra en 3. Orden: (1) reglas y validación del EID con la muestra; (2) guardado, lectura por EID, ficha y exportación; (3) resumen en pantalla y cierre.

## Preguntas abiertas
1. **EID inválido (RF-4):** ¿se guarda igual o se descarta? **Recomendación: descartar el EID (el animal entra siempre) y avisar con advertencia.** Un valor que no tiene 15 dígitos casi seguro es un error de carga y, si se guardara, podría chocar con el EID bueno de otra vaca y anular a las dos; el valor original queda en los datos originales. Alternativa: guardarlo igual (hasta 32 caracteres sin espacios, lo que admite la base) con advertencia; solo conviene si algún tambo usa caravanas electrónicas con otro formato.
2. **EID repetido (RF-5):** **Recomendación: ningún animal del grupo queda con EID** (como pasa con el `ID` repetido, spec 002 RF-9) y se avisa. Alternativa: que quede con el EID la primera fila; descartada porque no hay forma de saber cuál es la correcta y una lectura abriría la vaca equivocada.
3. **Rodeo viejo sin EID (RF-11, RF-12):** el rodeo ya importado (por ejemplo BAUTI1) no se actualiza; se importa el archivo nuevo como rodeo nuevo y el viejo se usa o se deja de usar. **Recomendación: así** (sin fusionar, que no existe en la spec 002). Los tactos ya guardados quedan en el rodeo viejo y no pasan al nuevo; sirve mientras el rodeo viejo sea de prueba o no tenga trabajo por conservar. Alternativa: una spec aparte para "completar EID" en un rodeo existente, solo si hay rodeos con trabajo hecho que no se puedan dejar atrás.
