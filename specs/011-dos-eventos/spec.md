# 011 — Dos eventos por tacto

Estado: borrador
Fecha: 2026-11-25

> **Numeración:** esta spec es la 011 (rama `spec/011-dos-eventos`). Parte de lo que dejan la 005 (eventos del catálogo con pasos), la 007 (exportación), la 009 (con una ficha abierta las lecturas se ignoran) y la 010.

> **Pedido del responsable (2026-11-25):** hoy cada tacto guarda **un solo** evento (casilleros "Evento: Tocá para elegir" y "Observación: Opcional") y el archivo de vuelta tiene una sola columna de evento (`Evento1`). En el campo una vaca puede tener **dos eventos en el mismo tacto**, por ejemplo **OK y PROSTAG**. Propuesta de pantalla: mostrar **chica** una opción **"+ evento"**; solo si hace falta, al tocarla aparece una **segunda fila** con otro "Tocá para elegir" (Evento 2) y su "Opcional" (observación). Si no se toca, todo queda como hoy.

## Contexto
El veterinario toca a la vaca y a veces resuelve dos cosas a la vez: "está OK" y además "se le dio prostaglandina" (PROSTAG), o "Revisar nuevamente" más otro evento del tambo. Hoy tiene que elegir uno y perder el otro, o tactar dos veces a la misma vaca (la segunda lectura es una vaca repetida y el tacto se hace una vez al mes). Dairy Comp, al importar, crea un evento por cada columna de evento: el archivo de vuelta tiene que poder decir los dos.

Lo usa el veterinario en la manga, de pie y con una mano, en la tablet en vertical (800x1340). La inmensa mayoría de las vacas lleva **un** evento: la pantalla tiene que seguir siendo de **un toque por vaca** para ese caso (constitución §9).

## Alcance
- Incluye:
  - Poder agregar un **segundo evento** (con su propia observación opcional) a la ficha antes de tocar **Entrar**, con la opción chica "+ evento".
  - Qué combinaciones de eventos se aceptan y cuáles no.
  - Guardar los dos eventos **juntos, en un solo registro y una sola operación indivisible**, con su historial.
  - Que **Deshacer**, **Editar**, "Últimos movimientos", el historial de la ficha, el aviso de repetido y "Último guardado" muestren y manejen los dos eventos.
  - Exportación: una columna nueva **`Evento2`** (nombre provisorio) al lado de `Evento1`.
  - Que una instalación existente reciba el cambio sin perder nada.
- No incluye:
  - **Más de dos eventos** por tacto (pregunta 1).
  - Dos eventos que **cambian el estado reproductivo** a la vez, o un evento con **pasos posteriores** (Aborto, Preñada fecha previa) o **RECHAZO** acompañado de otro (preguntas 2 y 10).
  - Cambiar cómo se elige el Evento 1, el selector de eventos, las listas o los pasos (spec 005): se reutilizan.
  - Exportar las observaciones (siguen sin exportarse, spec 007) ni columnas de observación.
  - Cambiar que los eventos creados a mano (PROSTAG, etc.) cambian el estado de la vaca cuando van como Evento 1 (pregunta 8).
  - Probar la reimportación en Dairy Comp (prueba 0.2): queda como riesgo.

## Definiciones
(Las de las specs 003, 004, 005 y 007 valen acá.)
- **Evento 1:** el evento que se elige hoy en el casillero "Evento". Es el que decide qué se guarda (un tacto con cambio de estado o un registro "solo evento") y sale en la columna `Evento1`.
- **Evento 2:** el evento adicional. **Nunca cambia el estado reproductivo de la vaca ni la fecha de su último tacto** y no pide pasos: solo queda registrado junto al Evento 1. Sale en la columna `Evento2`.
- **Evento combinable (puede ser Evento 2):** evento activo del catálogo **sin pasos posteriores y sin destino**, que es de tipo **solo evento** (OK, Revisar Nuevamente) o **creado a mano** con "+ Agregar evento" (PROSTAG, DIB, ENDOM1…). No lo son: **RECHAZO** (el fijo), Aborto, Preñada fecha previa y los estados estándar del catálogo reproductivo (Preñada fecha correcta, VACIA, NOINSEM, INSEMIN, SECA, FRESCA).
- **Evento que admite un segundo (puede ser Evento 1 con "+ evento"):** cualquier evento activo **sin pasos posteriores y sin destino** y que no sea RECHAZO: OK, Revisar Nuevamente, Preñada fecha correcta, VACIA, NOINSEM y los creados a mano.
- **Registro con dos eventos:** un único tacto (o registro "solo evento", spec 005) que guarda además el Evento 2 y su observación. No son dos registros.

## Requisitos funcionales

### La ficha
- **RF-1** — Mientras hay una ficha abierta con un **Evento 1 elegido que admite un segundo** y todavía no hay Evento 2, el sistema deberá mostrar una opción **chica** "+ evento" debajo de los casilleros Evento y Observación. El sistema **no** deberá mostrarla sin Evento 1 elegido, con un Evento 1 que no admite un segundo (Aborto, Preñada fecha previa, RECHAZO) ni cuando ya hay un Evento 2. Para una vaca con un solo evento la pantalla y los toques son **exactamente los de hoy** (un toque más para Entrar no se agrega).
- **RF-2** — Cuando el veterinario toca "+ evento", el sistema deberá mostrar una **segunda fila** con los mismos dos casilleros que la primera: **Evento 2** ("Tocá para elegir") y su **Observación** ("Opcional"), y una forma chica de **quitarla**. Tocar "+ evento" **no guarda nada**.
- **RF-3** — Cuando el veterinario toca el casillero **Evento 2**, el sistema deberá abrir la **misma lista de eventos** (principales y "Otro evento", con "Volver" y "+ Agregar evento") mostrando **solo los eventos combinables** (ver definiciones) y **sin el Evento 1** ya elegido. Elegir uno lo deja en el casillero de Evento 2, cierra la lista y **no guarda nada**. RECHAZO no aparece. Si la lista de combinables quedara vacía, la lista lo dice y no rompe.
- **RF-4** — Cuando el veterinario toca el casillero **Observación** de la segunda fila, el sistema deberá abrir el mismo cuadro de texto que la primera (opcional, hasta 300 caracteres), y esa observación pertenece **solo al Evento 2**. Cada evento tiene **su propia observación**.
- **RF-5** — Cuando el veterinario **quita** la segunda fila, el sistema deberá descartar el Evento 2 **y su observación** y volver a mostrar solo "+ evento", sin tocar el Evento 1 ni su observación.
- **RF-6** — El sistema deberá permitir **como máximo dos eventos** por tacto: con el Evento 2 agregado no hay una tercera fila ni otro "+ evento".
- **RF-7** — Cuando el veterinario cambia el **Evento 1** a uno que ya no admite un segundo (Aborto, Preñada fecha previa o RECHAZO), o al **mismo** evento que ya es el Evento 2, el sistema deberá **descartar el Evento 2 y su observación** y avisarlo con un mensaje corto ("Ese evento no se combina con otro: se quitó el evento 2" o equivalente). El Evento 1 elegido **no** se pierde.
- **RF-8** — El sistema no deberá permitir elegir **el mismo evento dos veces**: el Evento 2 nunca puede ser igual al Evento 1 (RF-3 lo esconde de la lista; el servicio lo revalida, RF-12).
- **RF-9** — Cuando el veterinario toca **En espera** (spec 004) con dos eventos o dos observaciones cargados, el sistema deberá descartarlos todos, como hoy descarta el evento y la observación: la vaca queda en espera sin tacto. Cuando una vaca que salió de la espera se guarda con **Entrar** con dos eventos, la espera queda resuelta igual que con uno. **Saltar** cierra la ficha sin guardar y descarta ambos.

### Qué se guarda
- **RF-10** — Cuando el veterinario toca **Entrar** con un Evento 2 elegido, el sistema deberá guardar **un único registro** que contiene: el Evento 1 (con su observación y, si corresponde, su cambio de estado), el **Evento 2 con su observación**, la **fecha y hora exactas del reloj** y el historial, todo en **una sola operación indivisible**: se guarda todo o no se guarda nada. Cuando el Evento 1 cambia el estado, la vaca pasa a ese estado y se actualiza su fecha de último tacto, **exactamente como hoy**; cuando es un solo evento (OK, Revisar Nuevamente), no cambia ni el estado ni el último tacto.
- **RF-11** — El **Evento 2 no deberá cambiar** nunca el estado reproductivo de la vaca ni su fecha de último tacto, aunque el evento sea de los creados a mano (que, como Evento 1, siguen cambiando el estado como hoy: pregunta 8).
- **RF-12** — El sistema deberá **revalidar en el servicio** todo lo que valida la pantalla y rechazar con un mensaje en español, **sin guardar nada**, cuando: el Evento 2 no existe, está dado de baja, **no es combinable**, **es igual al Evento 1**, el Evento 1 no admite un segundo (pasos, destino o RECHAZO), o una observación supera los 300 caracteres. Con el rechazo la ficha queda abierta con los dos eventos y las observaciones elegidos y, si la vaca venía de una espera, esta sigue pendiente (como en la spec 003 RF-18).
- **RF-13** — Las reglas críticas deberán garantizarse **también en la base** (constitución §7): un registro guardado no puede cambiar su Evento 2 ni su observación (solo pasa a anulado, como hoy), el Evento 2 de un tacto de anulación es siempre nulo, y un Evento 2 incompatible (igual al Evento 1, con pasos, con destino, un estándar de estado o con un registro que no admite segundo) lo rechaza la base aunque falle el código.
- **RF-14** — El sistema deberá dejar en el **historial** (solo se agrega, spec 001) una línea por el Evento 2 además de la del Evento 1, con el **nombre** del evento en ese momento, en la misma operación.

### Deshacer, Editar y lo que se ve
- **RF-15** — Cuando el veterinario toca **Deshacer** (el aviso de 5 segundos o "Últimos movimientos") sobre un registro con dos eventos, el sistema deberá anularlo **entero** (los dos eventos juntos; no se deshace uno solo), sin borrar nada, y la vaca vuelve al estado que tenía como con cualquier tacto. El registro anulado **conserva** sus dos eventos y sus observaciones, con su fecha y hora originales.
- **RF-16** — Cuando el veterinario toca **Editar** un registro con dos eventos, el sistema deberá abrir la capa con **los dos eventos y las dos observaciones ya cargados** y permitir **cambiar el Evento 1, agregar, cambiar o quitar el Evento 2** con las mismas reglas de RF-3 a RF-8. Guardar anula el original y registra uno nuevo con sus propios datos, en una sola operación (spec 003 RF-49); si el nuevo se rechaza, el original sigue vigente. Editar un registro de **un** evento permite sumarle un segundo con "+ evento".
- **RF-17** — El sistema deberá mostrar los dos eventos, en este orden y unidos por **" + "**, en: el **historial** de la ficha, "**Últimos movimientos**", el aviso de **animal repetido / ya pasó**, el aviso de deshacer y "**Último guardado**" (por ejemplo `1000: OK + PROSTAG`). Un registro de un evento se ve **exactamente como hoy**. Un evento dado de baja después sigue mostrando su nombre (spec 005 RF-13).
- **RF-18** — Con una ficha abierta, las lecturas del bastón o del simulador siguen **ignorándose** (spec 009, nota del 2026-11-25) también mientras la segunda fila o su lista o su observación están abiertas; esta spec no cambia nada de la lectura ni de la búsqueda a mano.

### Exportación (spec 007)
- **RF-19** — El sistema deberá agregar al archivo de vuelta una columna nueva de nombre provisorio **`Evento2`**, **inmediatamente a la derecha de `Evento1`**, con el **nombre** del Evento 2 como `Evento1` lleva el del Evento 1 (spec 007 RF-8). La columna **siempre está** en el archivo; queda **vacía** en las vacas sin Evento 2, en las que no pasaron, en las filas excluidas y en la fila `Total` (spec 007 RF-11).
- **RF-20** — Un registro con dos eventos deberá exportar su Evento 1 en `Evento1` y su Evento 2 en `Evento2`; **`DIASPRE`, `DIASABO` y `RESABO` quedan vacías** (los eventos con pasos no se combinan, así que esas columnas siempre pertenecen al Evento 1 y no hay ambigüedad). `SCDAT` y `SCTIM` son los del registro, como hoy. Las observaciones **no** se exportan.
- **RF-21** — Si el archivo original ya trae una columna llamada **`Evento2`** (sin distinguir mayúsculas), el sistema deberá **rechazar la exportación** con el mensaje de columna repetida que nombre esa columna, sin generar ningún archivo y sin cambiar nada (spec 007 RF-14). La exportación de un registro **anulado** o corregido sigue las reglas de la spec 007 RF-12: sale el último vigente, con los dos eventos si los tiene.
- **RF-22** — El archivo con `Evento2` deberá pasar la **validación de reapertura** (spec 007 RF-18) y generarse en los **cuatro cruces** de formato (CSV y `.xls`, desde CSV y desde `.xls`), conservando todo lo del original, y en 20.000 vacas dentro del tope de tiempo de la spec 007 (RF-26).

### Instalaciones existentes y transversales
- **RF-23** — Cuando la app se abre en una tablet con la base ya creada, el sistema deberá agregar el soporte del Evento 2 con una **migración** (constitución §7) y **backup previo**, **sin perder ni cambiar nada**: todos los tactos, rechazos, anulaciones e historial existentes quedan idénticos, con Evento 2 vacío; si la migración falla, la base queda como estaba. Los triggers de inmutabilidad se recrean con las columnas nuevas.
- **RF-24** — El sistema deberá seguir funcionando sin conexión y sin usar la red, no deberá incluir caravanas ni nombres de tambo en errores ni logs, y no deberá registrar ni mostrar qué veterinario ni qué tablet (spec 006).
- **RF-25** — La segunda fila, su lista y su observación deberán cumplir la constitución §9: botones y casilleros de tamaño cómodo para tocar de pie y con una mano en 800x1340 en vertical, alto contraste, en español rioplatense, y la ficha con la fila 2 abierta debe seguir mostrando **Entrar** sin tener que desplazar la pantalla (a verificar en la tablet).

## Casos límite
- **Sin conexión:** todo es local (RF-24).
- **Corte de energía o app cerrada:** con la segunda fila abierta o a medias no se guardó nada; un guardado con Entrar está completo (los dos eventos, el historial y el estado) o no existe (RF-10). Durante la migración, la base queda como estaba o ya migrada, nunca a medias (RF-23).
- **Excel mal formado:** al **importar** no cambia nada. Al **exportar**, un original con una columna `Evento2` se rechaza (RF-21); filas excluidas y `Total` salen con `Evento2` vacía (RF-19).
- **Lecturas repetidas / vaca no encontrada / CVET 0:** sin cambios. Con la ficha (y la segunda fila) abierta, las lecturas se ignoran (RF-18). La ficha de CVET 0 abierta con "Ver ficha" (spec 009 RF-15) también permite "+ evento".
- **Vaca que no está en el rodeo:** sin cambios (el alta en campo no existe desde la 003).
- **Varios veterinarios o tambos en la misma tablet:** el catálogo es de la tablet; no se registra quién (spec 006). La combinación se valida con el catálogo de la tablet (RF-12).
- **Una sola opción de combinar:** si el catálogo tiene un solo evento combinable y es el Evento 1, la lista de Evento 2 queda vacía con un texto que lo dice (RF-3).
- **Evento 2 desactivado mientras la ficha está abierta:** el servicio lo rechaza con un mensaje (RF-12); la ficha queda abierta con todo lo elegido.
- **Evento 1 con pasos o RECHAZO:** no ofrece "+ evento"; si ya había Evento 2 y se cambia a uno de esos, el Evento 2 se descarta con aviso (RF-7). Aborto con resultado Rechazo es un Aborto: tampoco admite un segundo.
- **Mismo evento dos veces:** no se ofrece en la lista y el servicio lo rechaza (RF-8, RF-12).
- **Evento 1 que cambia el estado + Evento 2:** la vaca pasa al estado del Evento 1 como hoy; el Evento 2 solo queda registrado (RF-10, RF-11). Deshacer vuelve al estado anterior (RF-15).
- **Vaca en espera con dos eventos cargados que toca En espera:** se descartan ambos y ambas observaciones (RF-9).
- **Registro viejo (un solo evento):** se ve, se deshace, se edita y se exporta como siempre; su `Evento2` sale vacío (RF-17, RF-19, RF-23). Editarlo permite sumarle un segundo (RF-16).
- **Editar un registro con dos eventos cuyo Evento 2 ya no está activo:** se conserva el nombre para mostrarlo; para guardar hay que quitarlo o elegir uno activo (spec 005 RF-13; RF-12).
- **Un tacto con dos eventos y otro tacto el mismo día (muy raro):** sale el último vigente con sus dos eventos (spec 007 RF-12).
- **Observación solo en el Evento 2:** es válida (la del Evento 1 queda vacía) y se guarda.
- **Observación del Evento 2 escrita y luego se quita la fila:** se descarta con la fila (RF-5).
- **Muchas vacas seguidas con dos eventos:** cada una es un registro independiente; no hay estado compartido entre vacas (la segunda fila arranca cerrada en cada ficha).
- **Archivo exportado grande (20.000 vacas, 25 MB):** una columna más; debe entrar en el tope de la spec 007 (RF-22).
- **Dairy Comp no acepta una columna vacía o repetida con ese nombre:** riesgo de la prueba 0.2 (pregunta 5 y 11); no bloquea la spec.

## Criterios de aceptación
- [ ] Sin tocar "+ evento", guardar una vaca es idéntico a hoy: mismos toques, mismo registro, sin Evento 2 (RF-1, RF-10).
- [ ] "+ evento" no se ve sin Evento 1, ni con Aborto, Preñada fecha previa o RECHAZO como Evento 1, ni con un Evento 2 ya agregado; se ve con OK, Revisar Nuevamente, Preñada fecha correcta, VACIA, NOINSEM y un evento creado a mano (RF-1, RF-6).
- [ ] La segunda fila abre la lista solo con combinables, sin el Evento 1 y sin RECHAZO; cada fila tiene su observación; quitar la fila descarta el Evento 2 y su observación (RF-2 a RF-5).
- [ ] Cambiar el Evento 1 a un evento incompatible o igual al Evento 2 descarta el Evento 2 con aviso y conserva el Evento 1 (RF-7, RF-8).
- [ ] Entrar con dos eventos guarda **un** registro con el Evento 1, el Evento 2, las dos observaciones y la hora; el estado y el último tacto solo los cambia el Evento 1; el Evento 2 de un evento creado a mano **no** cambia el estado (RF-10, RF-11).
- [ ] El servicio rechaza (sin guardar nada y dejando la ficha y la espera como estaban) un Evento 2 inexistente, de baja, no combinable, repetido, con Evento 1 con pasos o RECHAZO, o con una observación de más de 300 caracteres; la base rechaza lo mismo con SQL directo y rechaza modificar un registro guardado (RF-12, RF-13).
- [ ] El historial tiene una línea más por el Evento 2 (RF-14).
- [ ] Deshacer anula el registro entero y deja al animal como estaba; el anulado conserva los dos eventos (RF-15).
- [ ] Editar precarga los dos eventos y observaciones, permite cambiarlos, quitarlos o sumar uno, y es una sola operación; si falla, el original sigue vigente (RF-16).
- [ ] La ficha, "Últimos movimientos", el aviso de repetido, el aviso de deshacer y "Último guardado" dicen `OK + PROSTAG`; un registro de un evento se ve igual que hoy (RF-17).
- [ ] En espera descarta los dos eventos; Entrar de una vaca que venía de espera la resuelve (RF-9).
- [ ] Las lecturas con la segunda fila o su lista abiertas se ignoran (RF-18).
- [ ] El archivo trae `Evento2` a la derecha de `Evento1`, con el nombre del Evento 2 y vacía en el resto; `DIASPRE`, `DIASABO` y `RESABO` vacías en un registro con dos eventos; un original con `Evento2` se rechaza; pasa la validación en los cuatro cruces y en 20.000 vacas (RF-19 a RF-22).
- [ ] Una base anterior se actualiza con backup, sin perder nada y con Evento 2 vacío en todo lo existente; los triggers rechazan modificar el Evento 2 de un registro guardado; `has-pending-model-changes` sin cambios (RF-13, RF-23).
- [ ] **Con tests** (dominio, servicio con base real, migración sobre una base con datos, ViewModels y exportación): RF-1 a RF-23 salvo lo que depende de ver la pantalla.
- [ ] **A verificar en la tablet (800x1340 vertical)** (no con tests): "+ evento" se ve chica pero se toca bien; la segunda fila y **Entrar** entran sin desplazar; la lista de Evento 2 es cómoda; Editar con dos filas se lee y se usa con una mano; con el bastón real, las lecturas se ignoran con la segunda fila abierta (RF-1, RF-2, RF-16, RF-18, RF-25).
- [ ] `dotnet build` y `dotnet test` en verde (con rendimiento al cierre).

## Preguntas abiertas
(Cada una con la recomendación; el plan trabaja con la recomendación como valor provisional.)

1. **¿Máximo de eventos por tacto?** *Recomendación:* **dos** (lo pedido). Tres obligaría a pensar otra estructura de datos y de exportación sin que haga falta hoy.
2. **¿Qué combinaciones valen?** *Recomendación (la que usa esta spec):* el **Evento 2 nunca cambia el estado ni lleva pasos** y es OK, Revisar Nuevamente o un evento creado a mano (PROSTAG…). El Evento 1 puede ser cualquiera salvo Aborto, Preñada fecha previa y RECHAZO. Así nunca hay dos cambios de estado en un tacto (VACIA + Preñada, Aborto + otro quedan afuera), las reglas de la 005 (pasos, resultado, un solo registro por Aborto) no se tocan y `DIASPRE`/`DIASABO`/`RESABO` no se ambiguan. Si el responsable necesita "VACIA y OK" con VACIA como segundo, se carga VACIA como Evento 1 y OK como Evento 2 (el orden en el archivo es el de la pantalla).
3. **¿Una observación por evento o una por tacto?** *Recomendación:* **una por evento** (lo que sugiere la propuesta de pantalla: cada fila trae su "Opcional").
4. **¿Qué pasa si se elige el mismo evento dos veces?** *Recomendación:* **no se permite** (la lista lo esconde y el servicio lo rechaza).
5. **Nombre de la columna del segundo evento.** *Recomendación:* `Evento2`, **provisorio** (como `Evento1`), a confirmar con el padre del responsable: cómo lee Dairy Comp / el Daily dos eventos del mismo animal en el mismo archivo y si pone el mismo `SCDAT` y `SCTIM` a los dos. Vive en un solo lugar (`ColumnasExportacion`), así que cambiarlo es una línea.
6. **¿La columna `Evento2` va siempre o solo si algún tacto la usó?** *Recomendación:* **siempre** (archivo de forma fija y predecible, igual que `DIASPRE`, `DIASABO` y `RESABO`, que también salen vacías). Si Dairy Comp rechazara una columna vacía, se revisa tras la prueba 0.2.
7. **Si el original ya trae `Evento2`:** *Recomendación:* rechazar la exportación como con las otras columnas nuevas (spec 007 RF-14); revisar si el padre confirma que Dairy Comp ya exporta una columna con ese nombre.
8. **Los eventos creados a mano (PROSTAG…) hoy cambian el estado de la vaca al elegirlos como Evento 1.** Como Evento 2 no lo hacen. *Recomendación:* dejarlo así en esta spec (no se cambia el comportamiento de la 005) y decidir aparte si esos eventos deberían ser "solo evento" (en ese caso el orden de la pantalla dejaría de importar).
9. **¿"+ evento" se ve siempre o solo cuando hay Evento 1?** *Recomendación:* **solo con un Evento 1 que admite segundo** (menos ruido en la mayoría de las vacas); no cuesta un toque más porque el Evento 1 se elige igual primero.
10. **¿Aborto, Preñada fecha previa o RECHAZO con un segundo evento?** *Recomendación:* **no** por ahora (caso raro; sumaría ambigüedad en los días y el resultado). Si el campo lo pide, es una spec chica aparte.
11. **¿Dairy Comp acepta los dos eventos con el mismo `SCDAT`/`SCTIM`?** Lo confirma la prueba 0.2 (hoy bloqueada por permisos); no bloquea la spec, queda como riesgo en `MEMORY.md`.
