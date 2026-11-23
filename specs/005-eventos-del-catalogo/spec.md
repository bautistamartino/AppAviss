# 005 — Eventos del catálogo: OK, Aborto, Revisar Nuevamente y "Otro evento"

Estado: aprobada
Fecha: 2026-11-23

> **Numeración:** esta spec es la 005 (rama `spec/005-eventos-del-catalogo`). La exportación pasa a ser la **006** y el bastón la **007**. Al aprobarla hay que actualizar: `docs/plan.md` (las etapas "005 exportación" y "006 bastón"), `MEMORY.md` (las menciones a la spec 005 como exportación) y la spec 004 (nombra "exportación 005"; pasa a 006).

> **Decisiones del responsable incorporadas:** los códigos de Dairy Comp de OK, Aborto y Revisar Nuevamente arrancan **provisorios** (el nombre en mayúsculas) hasta que el padre del responsable pase los reales; los tres son **solo evento**; principales y orden; sin precarga de eventos del tambo.

## Contexto
En el Daily (Pocket CowCard) la pantalla "Seleccione Evento" muestra como **principales** Saltada, Revisar Nuevamente, OK, Vacia, Preñada (fecha correcta / fecha previa), Aborto y No Inseminar, y un botón **"Otro Evento"** con los eventos propios del tambo (RECHAZO, REVISAR, PROSTAG, DIB, ANESTRO, ENDOM1-4, QUISTE, etc.). El veterinario resuelve la mayoría de las vacas con los principales.

Aviss hoy tiene un catálogo de 6 estados (PREÑADA, INSEMIN, SECA, VACIA, FRESCA, NOINSEM) y, al tocar el casillero **Evento** de la ficha, una lista (capa) con todos los estados activos, el evento fijo **Rechazo** (spec 004) y "+ Nuevo evento" (spec 003 RF-39). Faltan OK, Aborto y Revisar Nuevamente, y no hay forma de separar lo de todos los días de lo que se usa rara vez: si el catálogo crece, la lista deja de entrar sin desplazar (hoy los 6 estados entran en 480 px) y lo principal queda escondido.

Lo usa el veterinario en la manga, de pie y con una mano, en vertical (800x1340): un toque para el evento más común.

## Alcance
- Incluye:
  - Sumar al catálogo **OK**, **Aborto** y **Revisar Nuevamente**, con código de Dairy Comp provisorio.
  - Que cada evento del catálogo sea **principal** o **de "Otro evento"**: los principales se ven siempre en la lista de la ficha y el resto queda detrás de un botón **"Otro evento"**.
  - Cómo conviven con los 6 estados actuales, los eventos creados a mano y **Rechazo**.
  - Lo mismo en la capa de **Editar** de "Últimos movimientos" (spec 003 RF-49).
  - Que una instalación existente reciba todo esto sin perder nada.
- No incluye:
  - **Preñada con fecha correcta / fecha previa**: spec aparte.
  - **"Saltada"** del Daily: en Aviss ya existe **Saltar** (cierra la ficha sin guardar).
  - **En Espera** (spec 004).
  - **Exportación**: la define la spec 006. Regla ya fijada por el responsable que esta spec respeta: ningún evento reemplaza columnas del archivo original; al exportar se **agregan columnas nuevas** con el veredicto (el evento elegido), la fecha del tacto y la hora a la que pasó el animal. Esta spec solo se ocupa de guardar bien cada evento con su fecha y hora.
  - Una pantalla para administrar el catálogo (cambiar principales, orden, códigos, desactivar): no se agrega.
  - Precargar los eventos propios del tambo: cada tambo los agrega a mano con "+ Nuevo evento".

## Definiciones
- **Evento:** lo que se elige en el casillero Evento de la ficha: los estados activos del catálogo, los eventos "solo evento" del catálogo (OK, Aborto, Revisar Nuevamente) y **Rechazo**.
- **Estado:** evento que cambia el estado reproductivo de la vaca (PREÑADA, VACIA, etc.).
- **Solo evento:** evento que se registra y queda en el historial pero **no cambia el estado reproductivo ni la fecha del último tacto** de la vaca (OK, Aborto, Revisar Nuevamente y Rechazo). Los eventos creados a mano con "+ Nuevo evento" siguen siendo estados, como hoy.
- **Rechazo:** evento **fijo de la aplicación** (no está en el catálogo, no se desactiva ni se reordena; spec 004). Es una regla: siempre está y siempre va primero en "Otro evento".
- **Principal / de "Otro evento":** dato de cada evento del catálogo. Los principales se ven siempre; los demás, detrás de "Otro evento".
- **Evento estándar:** los 9 que trae la app (los 6 estados, OK, Aborto, Revisar Nuevamente). Cada uno tiene una **identidad interna estable**, distinta de su nombre: renombrarlo o cambiarle el código no lo convierte en otro.
- **Catálogo:** los estados y eventos configurables con su código de Dairy Comp (constitución §4); no son valores fijos en el código de la pantalla.

## Requisitos funcionales

### Catálogo
- **RF-1** — El sistema deberá incluir en el catálogo, además de los 6 estados actuales, los eventos **OK**, **Aborto** y **Revisar Nuevamente**, con código de Dairy Comp **provisorio** igual al nombre en mayúsculas (`OK`, `ABORTO`, `REVISAR NUEVAMENTE`). El código provisorio deberá poder **corregirse** después sin perder los tactos ya guardados con ese evento ni cambiar su identidad (hoy sin pantalla: por una actualización del catálogo, que resuelve el plan). No se exige pantalla ni marca en la base para saber cuáles son provisorios: el riesgo queda documentado en `MEMORY.md` como "códigos pendientes de validar" (constitución §4).
- **RF-2** — El sistema deberá guardar, para cada evento del catálogo, si es **principal** o de "Otro evento" y su **orden**. Ambos datos son configurables y no están fijos en el código de la pantalla.
- **RF-3** — El sistema deberá tomar como principales de arranque, en este orden de arriba abajo: **Revisar Nuevamente, OK, VACIA, PREÑADA, Aborto, NOINSEM**. Quedan en "Otro evento" INSEMIN, SECA, FRESCA, Rechazo y los creados a mano.
- **RF-4** — Cuando se crea un evento con "+ Nuevo evento", el sistema deberá dejarlo como evento de "Otro evento" (no principal), de tipo estado, y ordenarlo por nombre (RF-6).
- **RF-5** — El sistema deberá reconocer a los principales por su **identidad interna**, no por su nombre: si se renombra uno, sigue siendo ese principal y en el mismo lugar. Si se desactiva, desaparece de la lista principal y **no lo reemplaza ningún otro**; la lista queda más corta (RF-13).

### Lista de eventos de la ficha
- **RF-6** — Cuando el veterinario toca el casillero **Evento** de una ficha que entra en tacto, el sistema deberá mostrar los principales activos, uno por botón y en su orden, y debajo un botón **"Otro evento"**. Elegir un principal **no guarda nada**: lo deja en el casillero y habilita **Entrar** (spec 003 RF-39).
- **RF-7** — Cuando el veterinario toca **"Otro evento"**, el sistema deberá mostrar la lista secundaria con **Rechazo primero** y después todos los demás eventos activos no principales, en **orden alfabético sin distinguir mayúsculas ni tildes y con la Ñ entre la N y la O** (ej.: ANESTRO, ENDOM1, FRESCA, INSEMIN, PROSTAG, SECA), con un botón **"Volver"** a los principales. Elegir uno lo deja en el casillero, cierra la lista y vuelve a la ficha, sin guardar nada. Si dos nombres quedan iguales con esa comparación, se desempata por el orden en que se crearon.
- **RF-8** — El sistema deberá ofrecer **"+ Nuevo evento"** dentro de la lista de "Otro evento", siempre visible (fijo, sin tener que desplazar la lista), con las reglas de la spec 003 RF-39 (nombre y código repetidos se rechazan; crea el evento sin guardar ningún tacto). Los principales quedan solo con sus botones y "Otro evento".
- **RF-9** — El sistema deberá resaltar el **evento elegido en ese momento** (al abrir la ficha, el estado actual de la vaca si lo tiene; después, el que el veterinario eligió): si es principal, su botón; si es de "Otro evento" (incluido **Rechazo** u otro que no es estado), el botón "Otro evento" en la lista principal y ese evento dentro de la lista secundaria.
- **RF-10** — El sistema deberá mostrar la lista principal completa **sin desplazar** en una tablet de 800x1340 en vertical con los 6 principales de RF-3 y "Otro evento" (7 botones). Si hay más principales de los que entran, la lista deberá seguir siendo usable (desplazable con barra visible).
- **RF-11** — Como Rechazo es fijo y siempre está en "Otro evento", esa lista nunca queda vacía y el botón "Otro evento" se muestra siempre.
- **RF-12** — El sistema deberá usar la misma lista (principales, "Otro evento", resaltado y "Volver") en la capa de **Editar** de "Últimos movimientos", resaltando el evento **del registro** que se edita, sea un estado, un solo evento o Rechazo.
- **RF-13** — Si un evento del catálogo está desactivado, el sistema no deberá mostrarlo en ninguna lista; sí deberá seguir mostrando su nombre en el historial, los movimientos y la ficha de lo ya guardado.
- **RF-14** — Para llegar a cualquier evento activo el veterinario deberá necesitar, como máximo, **3 toques** desde la ficha: casillero Evento + principal (2) o casillero Evento + "Otro evento" + evento (3).
- **RF-15** — Mientras está abierta la lista de eventos (principal o secundaria), las lecturas del bastón se tratan como con la ficha abierta (spec 003 RF-19): no se pierden ni cambian de pantalla.

### Qué hace cada evento al guardarse
- **RF-16** — Cuando el veterinario guarda con **Entrar** OK, Aborto o Revisar Nuevamente, el sistema deberá registrarlo con observación opcional, veterinario activo, **fecha y hora exactas del reloj** (la hora a la que pasó el animal), dispositivo e historial, en una única operación indivisible, y **no deberá cambiar** el estado reproductivo de la vaca ni su fecha de último tacto. Esto vale también para Aborto; el padre del responsable confirmará cómo lo trata Dairy Comp y, si difiere, será un cambio de esta spec.
- **RF-17** — El comportamiento observable de un evento solo evento es el mismo que el de Rechazo (spec 004): se ve con su nombre en el Historial de la ficha, en "Últimos movimientos" y en el aviso de animal repetido; la ficha se cierra y la vaca cuenta como procesada; puede **deshacerse** (anulándose, sin borrar) y **editarse** (se anula el original y se registra uno nuevo, pudiendo pasar de estado a solo evento y al revés); y respeta el orden por fecha con los demás registros. Cómo se guarda internamente (un tacto sin cambio de estado o un registro aparte) lo decide el plan.
- **RF-18** — **Rechazo** deberá mantener todo su comportamiento (registro propio, motivo en la observación, sin cambiar el estado). Esta spec solo decide en qué lista aparece.
- **RF-22** — Todo evento guardado (estado, solo evento o Rechazo, y también el registro nuevo de una edición) deberá conservar su **fecha y hora exactas**, de modo que la exportación (spec 006) pueda agregarlas como columnas. Registrar un evento solo lo guarda con su fecha y hora: **no modifica ni reemplaza ninguna columna del archivo original importado**; el efecto sobre el archivo de vuelta es únicamente el de agregar columnas nuevas (veredicto, fecha, hora), lo define la spec 006. Un evento anulado conserva su fecha y hora originales.

### Instalaciones existentes
- **RF-19** — Cuando la app se abre en una tablet con la base ya creada, el sistema deberá agregar los eventos nuevos (RF-1) y los datos de principal / orden (RF-2) **sin perder ni cambiar nada**: los 6 estados conservan nombre, código, estado activo o desactivado y tactos; los creados a mano se conservan y quedan en "Otro evento", ordenados por nombre; los iniciales desactivados a mano no se reactivan. Todo cambio de estructura es una migración (constitución §7), con **backup previo**, y si falla a medias la base queda **como estaba antes**, sin datos parciales.
- **RF-20** — Si al agregar un evento estándar ya existe uno con el **mismo nombre o el mismo código** (comparados sin distinguir mayúsculas; ej.: un "Aborto" creado a mano), el sistema deberá **adoptarlo** como ese evento estándar en vez de crear otro: conserva su nombre, su código y si está activo o no (no se reactiva), y pasa a tener la identidad interna, el tipo y la marca de principal / orden del estándar. Así la lista principal no queda con menos botones sin explicación. El arranque no puede fallar por esto.
- **RF-21** — El sistema deberá seguir funcionando sin conexión y sin usar la red (spec 003 RF-32), y no deberá incluir caravanas, nombres de tambo o de veterinarios en errores ni logs (spec 003 RF-33).

## Casos límite
- **Sin conexión:** no cambia nada; todo es local (RF-21).
- **Corte de energía:** durante la migración y el sembrado, la base queda en el estado anterior o en el nuevo, nunca a medias, con backup previo (RF-19). Un evento elegido sin "Entrar" se pierde sin consecuencias; un guardado con "Entrar" está completo o no existe (RF-16).
- **Excel mal formado / RPRO (solo lectura al importar):** esto afecta únicamente cómo se **lee** el `RPRO` para el estado inicial; al exportar, el `RPRO` original no se reemplaza (RF-22). Un `RPRO` que coincide con el código de un evento (ej. "ABORTO") se interpreta con el mismo catálogo que hoy. Los eventos **solo evento** (OK, Aborto, Revisar Nuevamente, Rechazo) **no son un estado inicial válido**: si el `RPRO` coincide con uno, la vaca entra **sin estado y con advertencia**, como un código desconocido. Un `RPRO` **vacío** nunca se resuelve a un evento: entra sin estado y sin advertencia, como hoy.
- **Vaca leída varias veces / no encontrada / CVET 0:** sin cambios; solo las vacas que entran en tacto abren la ficha con la lista.
- **Varios veterinarios o tambos en la misma tablet:** el catálogo es de la tablet; todos ven los mismos principales y los mismos eventos de "Otro evento".
- **Evento principal desactivado o renombrado a mano:** desactivado, desaparece y la lista es más corta; renombrado, sigue siendo el mismo principal (RF-5).
- **Todos los principales desactivados:** la lista muestra solo "Otro evento" (RF-11).
- **Evento creado a mano con el nombre o código de un estándar (antes o después de actualizar):** antes de actualizar se adopta (RF-20); después de actualizar, el nombre y el código del estándar ya están ocupados y la regla de repetidos de la spec 003 RF-39 lo rechaza.
- **Código provisorio equivocado:** se pueden usar los tres eventos en la manga normalmente; el evento queda guardado con su fecha y hora y, al corregirse el código, no se pierde nada. Si el código llega a escribirse en el archivo de vuelta, y qué hacer con uno aún sin validar, lo define la spec 006 (P5; RF-1).
- **Evento guardado y exportación:** guardar un evento nunca toca columnas del archivo original; la exportación solo agrega columnas (RF-22). Un evento anulado o editado no pierde su fecha y hora.
- **Muchos eventos en "Otro evento" (~25):** la lista secundaria debe poder recorrerse con barra visible y "+ Nuevo evento" fijo; a verificar en la tablet.
- **Editar un movimiento cuyo evento ya no está activo:** se conserva el nombre; para corregirlo hay que elegir uno activo (RF-13, RF-12).
- **Nombres iguales con tildes o mayúsculas distintas:** no pueden coexistir (spec 003 RF-39 compara sin distinguir mayúsculas); el orden de RF-7 solo afecta cómo se ordenan.

## Criterios de aceptación
- [ ] Una base nueva tiene los 6 estados de siempre más OK, Aborto y Revisar Nuevamente con código provisorio, y su marca de principal / orden (RF-1, RF-2, RF-3).
- [ ] Una base anterior, con eventos creados a mano, un inicial desactivado y un "Aborto" creado a mano, se actualiza con backup previo: se agregan los eventos nuevos, el "Aborto" a mano se adopta como principal sin cambiarle nombre ni código, no se reactiva nada y los demás creados a mano quedan en "Otro evento" (RF-19, RF-20).
- [ ] La lista del casillero muestra los principales en su orden y "Otro evento"; este abre la lista secundaria (Rechazo primero, alfabética con Ñ y tildes bien) con "Volver" y "+ Nuevo evento" fijo; elegir un evento no guarda nada y habilita Entrar (RF-6, RF-7, RF-8).
- [ ] Renombrar un principal no lo mueve; desactivarlo no lo reemplaza (RF-5).
- [ ] El resaltado funciona para un principal, para uno de "Otro evento" y para Rechazo, en la ficha y en Editar (RF-9, RF-12).
- [ ] Guardar OK, Aborto y Revisar Nuevamente no cambia el estado ni la fecha de último tacto, y deja historial, deshacer, editar y aviso de repetido (RF-16, RF-17).
- [ ] Un evento desactivado no aparece en ninguna lista pero sí en lo ya guardado (RF-13).
- [ ] Un `RPRO` vacío o de un solo evento entra sin estado (vacío, sin advertencia) (caso límite RPRO).
- [ ] Cada evento guardado (estado, solo evento, Rechazo y edición) conserva fecha y hora exactas, también al anularse (RF-22).
- [ ] **Con tests** (ViewModel, catálogo, importación y migración sobre una base con datos): RF-1 a RF-9, RF-11 a RF-13, RF-16 a RF-20, RF-22.
- [ ] **A verificar en la tablet (800x1340 vertical)**: la lista principal entra sin desplazar, cualquier evento a 3 toques como máximo con una mano, y la secundaria con ~25 eventos se recorre bien (RF-10, RF-14).
- [ ] `dotnet build` y `dotnet test` en verde.

## Preguntas abiertas
- **P1 — Códigos reales de OK, Aborto y Revisar Nuevamente (los trae el padre del responsable).** Mientras tanto rigen los provisorios (RF-1), anotados en `MEMORY.md` como pendientes de validar. Recomendación: no agregar columna ni pantalla para marcarlos; cuando lleguen, corregirlos con una actualización del catálogo.
- **P2 — ¿"REVISAR" de la grilla "Otro Evento" del Daily es lo mismo que "Revisar Nuevamente"?** Recomendación: tratarlos como uno solo hasta que el padre confirme; si son dos, el segundo se crea a mano.
- **P3 — ¿Cómo trata Dairy Comp a Aborto?** Hoy es solo evento (RF-16). Si el padre dice que cambia el estado, se modifica esta spec.
- **P5 — Para la spec 006: ¿qué dato del evento va en la columna "veredicto" del archivo de vuelta, el nombre o el código de Dairy Comp?** No se decide acá. Recomendación: el **nombre** del evento (legible para el veterinario y no depende de códigos aún provisorios), y que el código quede disponible para una columna aparte si Dairy Comp lo necesita. Se resuelve cuando el padre del responsable confirme los códigos (P1).
- **P4 — ¿Un `RPRO` coincidente con OK/Aborto/Revisar Nuevamente debe entrar sin estado?** Recomendación: sí (caso límite RPRO), porque no son un estado reproductivo.
