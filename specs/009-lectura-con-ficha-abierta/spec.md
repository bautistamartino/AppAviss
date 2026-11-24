# 009 — Lectura de otra vaca con una ficha abierta

Estado: aprobada (2026-11-24; con los dos cambios no pedidos confirmados por el responsable: la vaca de CVET 0 y "Seguir" del aviso de repetido dejan abierta la ficha actual; los toques explícitos con una ficha abierta se revisan tras probar en la tablet)
Fecha: 2026-11-24

> **Nota 2026-11-24 (ajuste sobre la spec aprobada):** el responsable decidió que la ficha de una vaca con CVET 0 abierta con "Ver ficha" **ya no es "solo mirar"**: se abre completa (todos los datos, Evento, Observación, Entrar y En espera), porque en el campo el veterinario puede decidir tactarla igual. Motivo: al abrir la vaca 1019 (CVET 0) no figuraba ningún dato y no se podía agregar nada. Por eso esa ficha pasa a contar como "ficha con tacto abierta" y se eliminan las excepciones de "solo mirar" (definiciones, RF-8.5, decisión 5, casos límite, tabla de efecto). Se agrega RF-15. Sigue `aprobada`; el cambio sobre la 003 RF-41 y la 004 RF-1 se resume en la tabla de efecto.

## Contexto
El veterinario está tactando una vaca (su ficha está abierta, con el evento y la observación que va cargando) y el bastón lee otra caravana: el ayudante apuntó a la vaca siguiente, o el bastón leyó de más. Hoy (spec 003, RF-19) la ficha de la vaca nueva **reemplaza a la actual sin avisar**: la vaca nueva queda marcada como pasada, la anterior no guarda nada y, si el veterinario tenía un evento elegido, se pierde. La vaca anterior ya estaba marcada, así que ni siquiera figura como pendiente: solo se la recupera buscándola a mano.

Pedido del responsable (2026-11-24): cuando hay una ficha abierta y el bastón lee **otra vaca que también entra en tacto**, la app tiene que **preguntar qué hacer**, con tres opciones claras, en vez de decidir sola. Lo usa el veterinario de pie en la manga, con una mano, en la tablet en vertical.

## Alcance
- Incluye:
  - Un aviso con tres opciones cuando llega la lectura (del bastón o la búsqueda a mano) de otra vaca que entra en tacto y hay una ficha abierta: **poner en espera y abrir la nueva**, **cancelar la lectura**, **saltar la actual y abrir la nueva**.
  - Que la vaca nueva **no se marque** hasta que el veterinario elige abrirla.
  - Qué pasa con las lecturas que no entran en ese caso (misma vaca, CVET 0, no encontrada, ya procesada, en espera) cuando hay una ficha abierta.
  - Qué pasa con las lecturas que llegan mientras el aviso está abierto.
- No incluye:
  - Preguntar cuando el veterinario abre otra ficha **con un toque** (botonera de espera, lista de espera, "Ver ficha" o "Corregir" del aviso de repetido): el toque ya es la decisión (ver pregunta abierta 1).
  - Un filtro de lecturas repetidas por tiempo del bastón (spec 008).
  - Cambios en la base de datos, en el historial o en la exportación: la spec no agrega ninguna tabla, columna ni acción nueva.
  - Cambiar cómo se pone en espera, cómo se saltea o cómo se marca: se reutilizan tal cual (specs 003 y 004).

## Definiciones
(Las de las specs 003 y 004 valen acá.)
- **Ficha con tacto abierta:** la ficha de **cualquier** vaca que está en pantalla para cargarle un evento: normal, venida de la espera o de una vaca con CVET 0 abierta con "Ver ficha" (spec 003 RF-41, modificado por esta spec: esa ficha ya no es de solo mirar, RF-15). Todas pueden tener evento, observación, "En espera" y "Entrar", así que todas tienen algo que perder.
- **Vaca actual:** la de la ficha con tacto abierta.
- **Vaca nueva:** la que acaba de leer el bastón (o de buscarse a mano), distinta de la actual.
- **Entra en tacto:** CVET distinto de 0; un CVET ausente no bloquea (spec 003 RF-40). Es la condición de la **vaca nueva** para que aparezca el aviso; la vaca actual puede tener cualquier CVET.
- **Vaca de CVET 0:** no estaba en la lista de tacto que el veterinario preparó antes de ir al campo; igual puede tactarla o analizarla si decide otra cosa (decisión 2026-11-24).

## Requisitos funcionales

### El aviso
- **RF-1** — Cuando llega la lectura de una vaca nueva que entra en tacto y **todavía no está procesada hoy**, y hay una ficha con tacto abierta, el sistema deberá mostrar un aviso con tres opciones y **no deberá guardar nada** (ni marca ni conteo) hasta que el veterinario elija. Esto vale **siempre**: haya o no un evento o una observación elegidos en la ficha actual. La búsqueda manual se comporta igual que la lectura (spec 003 RF-23).
- **RF-2** — El aviso deberá mostrar la caravana de la vaca actual y la de la nueva (con su CVET) y tres botones grandes, en este orden: **"Poner en espera y abrir la nueva"**, **"Cancelar lectura"** y **"Saltar la actual y abrir la nueva"**. Si la ficha actual tiene un evento o una observación elegidos sin guardar, el aviso deberá decir que se perderían con la opción 1 y la 3. Deberá sonar o vibrar una vez (para que quien mira a la vaca note que la pantalla pide algo). El botón Atrás de Android deberá equivaler a "Cancelar lectura".

### Las tres opciones
- **RF-3** — Cuando el veterinario elige **"Poner en espera y abrir la nueva"**, el sistema deberá poner la vaca actual en espera (spec 004 RF-1: queda en la botonera, sin tacto, descartando el evento y la observación elegidos), marcar la vaca nueva como pasada (spec 003 RF-12: suma al conteo de la manga y al avance) y abrir su ficha. Deberá quedar listo para la siguiente lectura sin otro toque.
- **RF-4** — Cuando el veterinario elige **"Cancelar lectura"**, el sistema deberá descartar la lectura **como si no se hubiera leído**: la vaca nueva **no se marca**, no suma al conteo ni al avance y no deja ningún registro; la ficha actual sigue abierta tal cual estaba, con su evento y su observación.
- **RF-5** — Cuando el veterinario elige **"Saltar la actual y abrir la nueva"**, el sistema deberá cerrar la ficha actual como lo hace "Saltar" hoy (sin guardar ningún tacto; si venía de una espera, la deja salteada, spec 004 RF-3), marcar la vaca nueva como pasada y abrir su ficha.

### Casos de la vaca nueva y de la vaca actual
- **RF-6** — Cuando la vaca nueva **está en espera** (spec 004 RF-5) y hay una ficha con tacto abierta, el sistema deberá mostrar el mismo aviso de tres opciones. Como esa vaca ya está marcada, la opción 1 y la 3 solo abren su ficha: **no la marcan otra vez ni suman al conteo** (sigue sin poder ser "Animal repetido"). "Cancelar lectura" la deja en espera, sin tocarla.
- **RF-7** — Cuando la vaca actual **viene de una espera** (ya está pendiente), la opción 1 deberá cerrar su ficha dejándola pendiente, **sin crear otra espera**, y la opción 3 deberá saltearla como lo hace "Saltar" en esa ficha (spec 004 RF-3).

### Lecturas que no abren el aviso
- **RF-8** — Mientras hay una ficha con tacto abierta, el sistema deberá tratar así las demás lecturas:
  1. **La misma vaca de la ficha:** se ignora, sin aviso (como hoy).
  2. **Vaca nueva con CVET 0:** queda marcada y suma al conteo, suena la chicharra y **la ficha abierta no se cierra ni se toca** (cambia lo que hoy hace la spec 003: cerrarla). No hay aviso de tres opciones.
  3. **Caravana no encontrada:** se muestra "Caravana no encontrada" como siempre; al cerrarla, la ficha abierta sigue ahí.
  4. **Vaca nueva ya procesada hoy:** se muestra el aviso de "Animal repetido" o "Ya pasó" (spec 003 RF-22) sin marcar nada; **"Seguir" ya no cierra la ficha con tacto abierta**: la deja como estaba. No hay aviso de tres opciones.
  5. **Si la ficha abierta es de una vaca con CVET 0** (abierta con "Ver ficha"): cuenta como cualquier ficha con tacto abierta; una lectura de otra vaca que entra en tacto muestra el aviso de tres opciones (RF-1) y la opción 1 la pone en espera como a cualquier otra.
- **RF-9** — Mientras el aviso de tres opciones está abierto, el sistema deberá **guardar solo la última lectura que llegue** y procesarla al cerrar el aviso, con el resultado de la opción elegida ya aplicado (spec 003 RF-19). Una lectura del **mismo código** que el aviso (el bastón suele repetirla) se ignora y no reemplaza a la guardada.

### Errores y cortes
- **RF-10** — Si la opción 1 o la 3 no puede cerrar la ficha actual (por ejemplo, "En espera" o "Saltar" se rechaza o falla), entonces el sistema deberá mostrar el motivo en español dentro del aviso, **no cambiar nada** (la vaca nueva no se marca) y dejar el aviso abierto para elegir otra opción. Si la ficha actual se cierra bien pero después se rechaza la marca de la vaca nueva (por ejemplo, reloj mal puesto, spec 003 RF-21), entonces el sistema deberá conservar lo hecho con la actual (queda en espera o salteada, según la opción), mostrar el motivo y dejar la pantalla lista para leer de nuevo; la vaca nueva queda sin marcar.
- **RF-11** — Si la app se cierra, la tablet se apaga o se pasa a segundo plano con el aviso abierto, el sistema no deberá haber guardado nada por el aviso: la vaca nueva sigue sin marcar y la actual queda como estaba (marcada, sin tacto). Con la app viva, el aviso se conserva al rotar o volver de segundo plano (spec 003 RF-36).

### Cuentas e historial
- **RF-12** — El sistema deberá actualizar el conteo de la manga, el avance del día y el cierre automático por cantidad (spec 003 RF-45) **solo cuando una opción marca a la vaca nueva** (opciones 1 y 3, y solo si no estaba ya marcada). "Cancelar lectura" no cambia ningún número ni el texto de "Último guardado".
- **RF-13** — El sistema no deberá agregar acciones de historial ni tablas nuevas: usa las que ya existen (marca de pasada creada, espera creada, espera salteada). "Cancelar lectura" no deja rastro en la base. Lo guardado solo se agrega, nada se borra ni se modifica.

### Transversales
- **RF-14** — El aviso y sus tres botones deberán estar pensados para la tablet **en vertical** (800x1340): botones grandes de alto contraste, una sola mano, en español rioplatense (constitución §9). El sistema no deberá usar la red ni incluir caravanas ni nombres en registros de error o logs, y no deberá registrar ni mostrar qué veterinario ni qué tablet (spec 006).

### Ficha completa de una vaca con CVET 0
- **RF-15** — Cuando el veterinario toca "Ver ficha" de una vaca con CVET 0 (spec 003 RF-41), el sistema deberá abrir su ficha **completa**, igual que la de una vaca que entra en tacto: pestañas **Datos** e **Historial** con **todos los campos del Excel que tengan valor** (por ejemplo `LACT`, `DEL`, `FPART`, `FCELO`, `UTORO`, `DDUC`, `DCC`, `RPRO`, `FAPAR`, `LECHE`), los casilleros **Evento** y **Observación**, y los botones **Saltar**, **En espera** y **Entrar**. Deberá mostrar un cartel **informativo** "CVET 0: no estaba en la lista de tacto" (no bloquea nada). No deberá volver a marcar a la vaca ni sumarla al conteo (ya quedó marcada al leerla). Si el veterinario elige un evento y toca **Entrar**, el tacto se guarda como el de cualquier otra vaca (spec 003 RF-39), y con **En espera** se guarda su espera (spec 004 RF-1). La lectura o búsqueda de esa vaca no cambia: se marca, suena la chicharra y el aviso dice "no entra en tacto" (RF-8.2).

## Efecto sobre las specs anteriores
Esta spec **modifica** el texto de los requisitos de abajo. Al aprobarse, se les agrega una nota "(modificado por la 009)" apuntando acá; no se reescriben.

| Spec y requisito | Qué dice hoy | Qué cambia |
|---|---|---|
| **003 RF-19** | Una lectura de otra vaca con una ficha abierta **reemplaza** la ficha (la anterior ya quedó marcada). | Con una **ficha con tacto abierta** y una vaca nueva que entra en tacto, no se reemplaza: se pregunta (RF-1 a RF-5). Se mantiene: la misma vaca se ignora; con un cuadro abierto se guarda solo la última lectura (ahora también con el aviso nuevo, RF-9). Ya no hay ficha de "solo mirar": la de CVET 0 también pregunta (RF-8.5, RF-15). |
| **003 RF-12** | La vaca no procesada hoy se marca **sin ningún toque** al leerla. | Con una ficha con tacto abierta y una vaca nueva que entra en tacto, la marca se guarda **recién cuando el veterinario elige la opción 1 o la 3**. Sin ficha con tacto abierta (o con CVET 0), no cambia nada. |
| **003 RF-40** | La vaca con CVET 0 se marca, suena la chicharra y no abre ficha; el código cierra la ficha abierta de otra vaca. | Una ficha con tacto abierta **no se cierra** (RF-8.2). |
| **003 RF-41** | "Ver ficha" de la última vaca con CVET 0 abre la ficha **solo para mirar**: sin Evento, Observación ni Entrar; "Saltar" la cierra. | La ficha se abre **completa** (Datos con todas las columnas del Excel, Historial, Evento, Observación, Entrar, En espera y Saltar) con un cartel informativo "CVET 0: no estaba en la lista de tacto" (RF-15). Sigue sin volver a marcar. El botón "Ver ficha de ..." y su aparición no cambian. |
| **004 RF-1** | "En espera" solo en fichas de vacas que entran en tacto; una vaca con CVET 0 no tiene el botón. | El botón "En espera" también está en la ficha de CVET 0 abierta con "Ver ficha" (RF-15). La lectura de CVET 0 sigue sin abrir ficha ni ponerse en espera sola. |
| **004 (Rechazo sobre CVET 0)** | "No aplica (no abre ficha); se puede mirar con 'Ver ficha' pero sin evento." | Ahora se puede elegir también "Rechazo" en esa ficha, como cualquier evento (RF-15). |
| **003 RF-22** | El aviso "Animal repetido" / "Ya pasó" tiene "Seguir", que cierra el aviso y la ficha. | Con una ficha con tacto abierta, "Seguir" deja la ficha abierta (RF-8.4). "Ver ficha" y "Corregir" siguen igual (ver pregunta 1). |
| **004 RF-5** | Leer una vaca en espera abre directamente su ficha. | Sin ficha con tacto abierta, igual. Con una abierta, se pregunta antes de abrirla (RF-6); la vaca sigue sin marcarse otra vez. |
| **004 RF-15** | Las lecturas con la lista de espera abierta se guardan (solo la última) y se procesan al cerrarla, salvo una vaca en espera, que abre su ficha. | El "abre su ficha" de la vaca en espera pasa por el aviso si hay una ficha con tacto abierta (RF-6). Lo demás no cambia. |
| **004 RF-1 y RF-3** | "En espera" y "Saltar" de la ficha. | Sin cambios: el aviso los reutiliza (RF-3, RF-5, RF-7). |
| **Diagrama de estados de la 003** (en su plan) | `Ficha --> Ficha: lectura de OTRA vaca (reemplaza la ficha)`. | Se reemplaza por un estado nuevo de confirmación; el cambio de diagrama está en el plan de esta spec. |

## Casos límite
- **Sin conexión:** todo funciona sin Internet (RF-14).
- **Corte de energía o app cerrada con el aviso abierto:** no se perdió nada (RF-11). Con la opción 1, si el corte llega entre poner en espera la actual y marcar la nueva, la actual queda en espera (se ve en la botonera al reabrir) y la nueva sin marcar: se la vuelve a leer (RF-10).
- **Excel mal formado:** no aplica. Una vaca con CVET ilegible o ausente se toma como que entra en tacto (spec 003 RF-40).
- **Lectura repetida de la misma vaca:** de la actual, se ignora (RF-8.1). De la nueva mientras el aviso está abierto, se ignora (RF-9). Si después de "Cancelar lectura" el bastón vuelve a leer la misma vaca, es una lectura nueva y se vuelve a preguntar; el filtro por tiempo es de la spec 008.
- **Vaca no encontrada o de otro rodeo:** como siempre, la ficha abierta no se toca (RF-8.3).
- **Vaca nueva ya procesada hoy:** aviso de repetido; sin las tres opciones (RF-8.4). Si el veterinario toca "Ver ficha" abre esa ficha con un toque explícito (pregunta 1).
- **Vaca actual con CVET 0:** si se abrió con "Ver ficha", es una ficha con tacto abierta como cualquier otra: una lectura de otra vaca que entra en tacto muestra el aviso de tres opciones (RF-8.5). Al tactarla o ponerla en espera se guarda como a cualquier vaca.
- **Vaca de CVET 0 con datos "vacíos":** la ficha debe mostrar sus datos aunque el CVET sea 0 y aunque `LECHE` sea 0; solo se omiten las columnas sin valor (RF-15). Caso de referencia: vaca 1019 del rodeo BAUTI1.
- **Vaca de CVET 0 ya tactada y exportación:** sale en el archivo como cualquier otra (spec 007 RF-11, ya modificada el 2026-11-24).
- **Lectura mientras el evento o la observación están abiertos como cuadro:** esos cuadros ya frenan las lecturas (spec 003 RF-19); la lectura se procesa al cerrarlos y recién ahí aparece el aviso (RF-1).
- **Dos lecturas seguidas de vacas distintas con el aviso abierto:** se conserva la última; la primera no se marca (RF-9). Antes, las dos quedaban marcadas.
- **Manga que se completa con la vaca nueva:** el cierre automático por cantidad corre al marcarla, como siempre (RF-12). Con "Cancelar lectura" no corre.
- **Control por manga apagado:** el aviso funciona igual (no depende de las mangas).
- **Terminar el tacto:** no se puede tocar con el aviso abierto (el aviso tapa la pantalla); no cambia lo de la spec 004 RF-10.
- **Varios veterinarios o tambos en la misma tablet:** sin efecto: la app no registra veterinario ni tablet (spec 006) y las lecturas se buscan solo en el rodeo en trabajo.

## Criterios de aceptación
- [x] Con la ficha de una vaca con CVET distinto de 0 abierta, leer otra vaca con CVET distinto de 0 que no pasó hoy muestra el aviso con las dos caravanas y las tres opciones, y **no marca** a la nueva (el conteo y el avance no cambian) (RF-1, RF-2).
- [x] El aviso aparece igual con y sin evento u observación elegidos, y cuando hay algo elegido lo dice (RF-1, RF-2).
- [x] "Poner en espera y abrir la nueva": la actual aparece en la botonera de En espera, la nueva queda marcada (el conteo suma uno) y su ficha se abre (RF-3, RF-12).
- [x] "Cancelar lectura" (y el botón Atrás): la nueva no se marca ni cuenta, la ficha actual sigue abierta con su evento y su observación, y no queda ningún registro (RF-2, RF-4, RF-12, RF-13).
- [x] "Saltar la actual y abrir la nueva": la actual se cierra sin tacto, la nueva queda marcada y se abre su ficha (RF-5).
- [x] Una vaca nueva en espera pasa por el aviso y, al abrirse, no se marca otra vez ni suma (RF-6).
- [x] Con la actual viniendo de una espera: la opción 1 la deja pendiente sin crear otra, y la opción 3 la deja salteada (RF-7).
- [x] Misma vaca: se ignora. CVET 0: se marca y suena la chicharra, y la ficha abierta sigue abierta. No encontrada: la ficha sigue ahí al cerrar. Ya procesada: aviso de repetido y "Seguir" no cierra la ficha. Ficha de CVET 0 abierta con "Ver ficha": cuenta como ficha con tacto abierta y una lectura de otra vaca que entra en tacto muestra el aviso (RF-8).
- [x] Con el aviso abierto, varias lecturas dejan solo la última, la del mismo código se ignora, y se procesa al cerrar con el resultado de la opción elegida (RF-9).
- [x] Si "En espera" o "Saltar" fallan, el motivo se ve en el aviso, no cambia nada y el aviso sigue abierto; si falla la marca de la nueva después de cerrar la actual, la actual conserva su espera o salto y la nueva queda sin marcar (RF-10).
- [x] Cerrar y reabrir la app con el aviso abierto no deja ninguna marca de la vaca nueva ni pierde nada (RF-11).
- [x] El cierre automático de la manga corre al marcar con las opciones 1 y 3, y no con "Cancelar" (RF-12).
- [x] No hay migración nueva; `dotnet ef migrations has-pending-model-changes` sin cambios (RF-13).
- [x] **Ficha de CVET 0 completa (RF-15):** con la vaca 1019 del rodeo BAUTI1 (datos originales `{"ID":"1019","CVET":"0","LACT":"2","DEL":"211","FPART":"23/04/26","FCELO":"10/07/26","UTORO":"WINDSTAR","DDUC":"133","DCC":"133","RPRO":"PREÑADA","FAPAR":"18/04/27","LECHE":"0"}`), "Ver ficha" abre una ficha cuya pestaña Datos muestra `LACT` 2, `DEL` 211, `FPART`, `FCELO`, `UTORO` WINDSTAR, `DDUC` 133, `DCC` 133, `RPRO` PREÑADA, `FAPAR` y `LECHE` 0, con el cartel informativo "CVET 0: no estaba en la lista de tacto", los casilleros Evento y Observación y los botones Saltar, En espera y Entrar. Un test arma la ficha con esos datos y comprueba cada campo (RF-15).
- [x] Una vaca de CVET 0 abierta con "Ver ficha" se puede tactar (Entrar guarda el tacto, sin volver a marcar ni sumar al conteo) y poner en espera (RF-15), y con esa ficha abierta, leer otra vaca que entra en tacto muestra el aviso de tres opciones (RF-8.5).
- [ ] **A verificar en la tablet** (no con tests): los tres botones se alcanzan con una mano en 800x1340 y se distinguen de un vistazo; el sonido o vibración se nota con la vaca en la manga; Atrás cancela; con el bastón real que repite la lectura, el aviso no reaparece mientras está abierto (RF-2, RF-9, RF-14).
- [x] `dotnet build` y `dotnet test` en verde.

## Decisiones
**Del responsable (2026-11-24):**
1. El aviso aparece **siempre** que haya una ficha abierta y llegue otra vaca que entra en tacto; no depende de si se eligió evento u observación.
2. **"Cancelar lectura" no marca** a la nueva: no cuenta, como si no se hubiera leído.
3. Las tres opciones y sus nombres: poner en espera y abrir la nueva; cancelar lectura; saltar la actual y abrir la nueva.

**Cerradas con criterio del planificador (revisables):**
4. **La vaca nueva se marca recién al elegir** (no se marca y se desmarca después): así Cancelar no deja marcas anuladas en el historial ni mueve el conteo, y un corte con el aviso abierto no deja nada a medias.
5. **(Reemplazada el 2026-11-24 por la decisión 10.)** Antes: la ficha de "solo mirar" de CVET 0 no contaba como ficha abierta. Ya no existe esa ficha.
6. **CVET 0 no pregunta y no cierra la ficha abierta**: es una vaca que no se tacta, así que no compite con la actual y se la deja marcada con la chicharra de siempre.
7. **La vaca nueva en espera también pregunta** (RF-6): abrir su ficha reemplazaría a la actual igual que cualquier otra.
8. **La misma lectura repetida mientras el aviso está abierto se ignora** (RF-9), para que el bastón que repite no reabra el aviso apenas se cancela.
9. **"Seguir" del aviso de repetido ya no cierra una ficha con tacto abierta** (RF-8.4), porque cerrarla sin avisar es la misma pérdida silenciosa que esta spec quiere evitar.

**Del responsable (2026-11-24, ajuste posterior):**
10. **La ficha de una vaca con CVET 0 abierta con "Ver ficha" es completa** (datos, evento, observación, Entrar, En espera) y el cartel "CVET 0: no estaba en la lista de tacto" es solo informativo. Texto del responsable: "no importa si el CVET es 0, debería aparecer la información igual... cuando se lee o busca un animal con CVET 0, que aparezca lo que venía apareciendo 'no entra en tacto' pero que cuando quiero ver la ficha me aparezcan los datos de ese animal [...] y al mismo tiempo que aparezca si quiero agregar un evento. [...] el veterinario decide qué animal se tacta antes de ir al campo pero en el campo puede cambiar de opinión o ver de tactarla igual o analizarla igual". Consecuencia: esa ficha cuenta como "ficha con tacto abierta" (sin excepciones de "solo mirar").

## Supuestos y riesgos
- **Lecturas repetidas sin filtro por tiempo:** hasta la spec 008, después de "Cancelar lectura" el bastón puede volver a entregar la misma caravana y volver a preguntar. Es aceptable y se mira en la tablet.
- **Dos transacciones, no una:** poner en espera la actual y marcar la nueva son dos operaciones seguidas (RF-10). Si se corta la luz en el medio, no se pierde ni se duplica nada: la actual queda en espera y la nueva se vuelve a leer.
- **El sonido es el de siempre:** se reutiliza el aviso sonoro existente; si en la tablet no alcanza para notarlo, se ajusta aparte.
- **Tamaño (constitución §10, máximo 5 tareas):** entra en 3. Orden: (1) servicio y resultado de la lectura sin marcar; (2) ViewModel y sus tests (incluye la ficha completa de CVET 0, RF-15); (3) capa de la pantalla y cierre documental.
- **Problema reportado en la vaca 1019:** el responsable vio que la ficha de CVET 0 no mostraba ningún dato. Hay que reproducirlo con un test y corregirlo (ver plan, T2); si la causa es otra (por ejemplo, datos que no llegan a la ficha por el CVET 0), RF-15 sigue siendo el criterio.
- **Pantalla sin verificar en navegador:** la lógica y el ViewModel se prueban con tests; la capa se mira en la tablet.

## Preguntas abiertas
1. **Toques explícitos con una ficha abierta** (botonera de espera, lista de espera, "Ver ficha" y "Corregir" del aviso de repetido): hoy reemplazan la ficha actual sin preguntar. **Recomendación:** dejarlos así (el veterinario eligió esa vaca con el dedo, no por un error del bastón) y revisarlo después de usar la app en la tablet. Si se prefiere que también pregunten, el aviso se reutiliza tal cual y es una tarea más chica en otra spec.
2. **"Seguir" del aviso de repetido deja abierta la ficha con tacto** (RF-8.4, decisión 9): es un cambio chico al comportamiento de hoy que no estaba en el pedido. **Recomendación:** confirmarlo; si no, se vuelve a cerrar la ficha como hoy y se quita ese punto de RF-8.4. *(Confirmado por el responsable al aprobar.)*
3. **Exportación de una vaca de CVET 0 que se tactó:** *(Resuelta el 2026-11-24, fuera de esta spec.)* La spec 007 RF-11 ya quedó modificada y la exportación implementada: una vaca de CVET 0 con registro vigente (tacto o rechazo de hoy) sale en el archivo como cualquier otra; las de CVET 0 sin registro siguen saliendo vacías. Esta spec no toca la exportación.
4. **Specs 005 (eventos del catálogo) y 007:** la 005 solo dice que las vacas de CVET 0 no abren ficha con la lista de eventos (sin cambios: la lectura sigue sin abrirla; "Ver ficha" abre la lista completa). No hay otro efecto.
