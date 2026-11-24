# 004 — En Espera y Rechazo (nuevos registros de campo)

Estado: **implementada y mergeada** (2026-11-23; revisor aprobado y probada en la tablet por el usuario)
Fecha: 2026-11-16 (reescrita: 2026-11-21)

> **Spec hermana de `specs/003-flujo-de-tacto/spec.md`, que ya está implementada.** Esta amplía la ficha, el "último registro vigente" y la lista de últimos movimientos de la 003 (ver sus RF-39 a RF-52). Al aprobarse como 004, la exportación pasa a 007 y el bastón a 008 (`docs/plan.md`; la 005 son los eventos del catálogo y la 006 saca el veterinario). 

## Contexto
**En Espera.** Al escanear una vaca el veterinario empieza a tactarla y, a veces, demora. Mientras tanto la manga no puede frenarse: se sigue escaneando a las demás. La comodidad (palabras del usuario) es que **mientras se tacta un animal se puede seguir marcando a los otros y ver si van a tacto o no** (el CVET y la chicharra), y así saber si el siguiente va a tacto o si directamente ya se **libera a los animales de esa manga**. Agiliza el trabajo. Cuando el veterinario tiene el veredicto, **vuelve a la vaca que estaba tactando, abre su ficha y pone el evento**. Hoy una lectura nueva reemplaza la ficha abierta y la vaca anterior solo se recupera buscándola a mano. En el Daily, el botón "En Espera" (rosa) hace esto. Una espera suele durar unos minutos, pero **no tiene temporizador**: la vaca queda en espera hasta que se le agrega un evento o se la saltea a mano. Habrá 2 o 3 a la vez.

**Rechazo.** El veterinario marca una vaca como **rechazo** (que no sirve para el servicio). Los motivos son muchos y personales (pata, ubre y otros), así que **se elige "Rechazo" y el motivo se escribe a mano en la observación**. En la ficha actual (Evento + Observación + Entrar) encaja como **un evento más de la lista**.

Todo debe seguir siendo muy rápido y pensado para una mano, en vertical.

## Alcance
- Incluye:
  - **En Espera:** poner una vaca en espera desde su ficha, tener a mano las que están esperando y resolver cada una (con un evento o salteándola).
  - **Rechazo:** el evento fijo "Rechazo" en la lista de eventos, con la observación libre como motivo; deshacerlo y editarlo; mostrarlo en ficha, historial y movimientos.
  - Ampliar de la 003 el "último registro vigente", el orden de guardado compartido, el historial y la lista de últimos movimientos al rechazo.
- No incluye (**diferido a una spec posterior de tratamientos**, ver el final):
  - Catálogo de tratamientos (Prostal, DIB), panel "Detalle" con nota y días de preñez, "solo tratamiento" y los **tres renglones de evento + observación** del Daily.
  - Desactivar estados y deshacer un estado recién creado (hoy "+ Nuevo evento" de la 003 ya crea el estado).
  - Motivos de rechazo fijos o un catálogo de motivos (es observación libre).
  - Exportar a Excel: cómo se expresa el rechazo para Dairy Comp lo define la spec de exportación (depende de la prueba 0.2). Esta spec solo lo **guarda**.
  - Sincronización entre tablets, voz y alertas habladas, y el lector real.

## Definiciones
(Las de la spec 003 valen acá; estas las amplían.)
- **Espera:** el registro de que una vaca quedó **pendiente de veredicto** en un rodeo: de qué vaca, cuándo, quién y cómo terminó. Está **pendiente** hasta que se **resuelve** (se guardó un tacto o un rechazo de esa vaca) o se **saltea** (el veterinario entró a su ficha y la saltó porque no hacía falta evento). **No vence sola.**
- **Marca de rechazo:** el evento fijo "Rechazo" de la vaca, con una observación libre opcional (el motivo, escrito a mano). **No cambia el estado de la vaca ni su fecha de último tacto** y no genera un evento de estado hacia Dairy Comp.
- **Registro de campo:** en esta spec, una marca de pasada, un tacto o una marca de rechazo. **La espera no es un registro de campo**: no cuenta como "último registro vigente" ni bloquea deshacer otros registros.
- **Último registro vigente de una vaca:** el registro de campo vigente con fecha y hora más reciente entre los **tres** tipos juntos; si empatan, gana el que se guardó después (orden de guardado compartido, spec 003 RF-21).
- **Vaca que entra en tacto:** la de CVET distinto de 0 (spec 003 RF-40). Solo estas pueden ponerse en espera o tener rechazo.

## Requisitos funcionales

### En Espera
- **RF-1** — Cuando hay una ficha abierta de una vaca que entra en tacto, el sistema deberá ofrecer en la barra de abajo, junto a "Saltar" y "Entrar", el botón **"En espera"**. Al tocarlo deberá guardar la espera de esa vaca en el rodeo en trabajo (con el veterinario, la fecha y hora del reloj y el dispositivo, en una única operación indivisible, con su registro de historial), **cerrar la ficha sin guardar ningún tacto** y dejar la pantalla lista para la siguiente lectura casi de inmediato. Si la vaca ya estaba en espera, no deberá crear otra. El evento y la observación que se habían elegido **no se guardan**: al volver se empieza de cero. Una vaca con CVET 0 no puede ponerse en espera (no tiene botón). *(modificado por la 009: ver [spec 009](../009-lectura-con-ficha-abierta/spec.md))*
- **RF-2** — El sistema deberá mostrar, en la pantalla de trabajo y sin cambiar de pantalla, **una botonera chica con la caravana de cada vaca en espera** del rodeo en trabajo (por ejemplo "7712 · 3 min"), de la más antigua a la más reciente, para abrir cualquiera con **un toque**. Como se espera que haya 2 o 3 a la vez, la botonera debe alcanzar para unas 4 vacas; con más, deberá mostrar las primeras y un botón **"+N"** que abre la **lista de espera** completa (caravana, CVET, hora en que se puso en espera y hace cuánto espera, con un botón para abrir cada una). Sin vacas en espera, la botonera no aparece y no ocupa lugar. El tiempo que se muestra es solo informativo: **no hay temporizador ni vencimiento**.
- **RF-3** — Cuando el veterinario abre una vaca en espera (desde la botonera, desde la lista o al leerla, RF-5), el sistema deberá mostrar su ficha normal (con Evento, Observación, Datos, Historial, Saltar y Entrar) **sin volver a marcarla ni sumarla al conteo** y con un cartel visible "en espera". Hay **dos formas de sacarla de la espera**, y ninguna otra:
  1. **Entrar con un evento:** al guardar con **Entrar** un tacto (spec 003 RF-39) o un rechazo (RF-6), la espera deberá quedar **resuelta** en la misma operación indivisible que guarda el registro (si el registro se rechaza, la espera sigue pendiente y se muestra el motivo).
  2. **Saltar:** en una ficha que viene de una espera, **Saltar** deberá marcar la espera como **salteada** (quién y cuándo) sin guardar ningún registro de campo, porque no hacía falta cargar un evento. *(En una ficha normal, Saltar sigue solo cerrándola, spec 003 RF-39.)*
- **RF-4** — **No habrá** botón aparte para sacar una vaca de la espera ni se sacará sola: no vence con el tiempo, ni al cambiar el día, ni al terminar el tacto, ni al desmarcar. Si una vaca queda en espera de más (por ejemplo, de un día anterior), sigue en la botonera y se sale de ella como en RF-3, con el tiempo esperado a la vista para notarlo.
- **RF-5** — Cuando se lee (o se busca a mano) una vaca que está **en espera**, el sistema deberá **abrir directamente su ficha** como en RF-3, sin el aviso de "Animal repetido" ni "Ya pasó" y sin marcarla otra vez. Una vaca ya resuelta o salteada vuelve a comportarse como cualquier vaca ya procesada (spec 003 RF-22).
- **RF-9** — Cuando el veterinario intenta **deshacer la marca de pasada** de una vaca que está en espera (desde "Últimos movimientos"), el sistema deberá **rechazarlo** con el motivo "Está en espera: primero resolvela o saltala", sin cambiar nada (la espera solo se sale como en RF-3).
- **RF-10** — Cuando el veterinario toca "Terminar el tacto" y hay vacas en espera, el sistema deberá avisarlo en la pregunta final ("Hay N vacas en espera"), junto a la advertencia de manga abierta de la 003 RF-46, **sin impedir terminar y sin sacarlas de la espera**.

### Rechazo
- **RF-6** — El sistema deberá incluir en la lista de eventos de la ficha (spec 003 RF-39) el evento fijo **"Rechazo"**, que no pertenece al catálogo de estados, aparece **siempre** y no puede desactivarse. Al elegirlo, el veterinario escribe a mano el **motivo en la observación** (texto libre opcional, el mismo campo y límite de la observación de la 003). Con **Entrar** deberá guardar la marca de rechazo de esa vaca con la observación (si hay), el veterinario, la fecha y hora del reloj, el dispositivo y su registro de historial, en una única operación indivisible, **sin cambiar el estado de la vaca ni su fecha de último tacto**. Una vaca puede tener varias marcas de rechazo (la ficha muestra la vigente más reciente). Es un registro que solo se agrega (no se modifica ni se borra, también en la base), con la única modificación permitida de pasar su marca "Anulado" de "no" a "sí" (con quién y cuándo) al deshacerse, junto con un registro de historial. Su observación no se modifica una vez guardada.
- **RF-7** — El sistema deberá tratar la marca de rechazo como un registro de campo en todo lo que la spec 003 define sobre el "último registro vigente": el **orden de guardado compartido** (RF-21), la **validación de reloj** (RF-21), **deshacer** el último registro de la vaca (RF-26; para el rechazo marca el registro como anulado con su registro de historial), el aviso de animal repetido o ya pasó (RF-22, que puede mostrar "Rechazo · <motivo>" como último registro y ofrece "Corregir" cuando lo último no es la marca de pasada) y **"Últimos movimientos"** (RF-48: hora, caravana con CVET y debajo "Rechazo · <motivo>"; con **Deshacer** y **Editar** cuando es el último registro vigente de la vaca).
- **RF-8** — El sistema deberá permitir **editar** una marca de rechazo desde "Últimos movimientos" con las mismas reglas de la 003 RF-49: se anula el registro y se guarda uno nuevo con el evento y la observación elegidos, en una sola transacción. Se puede **cambiar un tacto por un rechazo y viceversa** (el evento elegido decide qué tipo de registro se guarda); si el nuevo se rechaza, no cambia nada y el original sigue vigente.
- **RF-11** — El sistema deberá mostrar en la ficha, además de lo de la 003, si la vaca está marcada como **rechazo** (la marca vigente más reciente con su observación, en una franja visible) y sumar al **Historial** de la ficha los rechazos con su observación, con los anulados marcados como tales.

### Historial y transversales
- **RF-12** — El sistema deberá registrar en el historial (solo agregar, spec 001 RF-10), con el veterinario y la fecha y hora, estas acciones nuevas: **espera creada**, **espera resuelta**, **espera salteada**, **rechazo creado** y **rechazo anulado**. La base rechaza modificar o borrar cualquier registro de historial nuevo.
- **RF-13** — La espera deberá ser un registro que solo se agrega: no se modifica ni se borra (también en la base), salvo pasar de **pendiente** a **resuelta** o **salteada** (una sola vez, con quién y cuándo, y qué registro la resolvió). Una vaca puede tener **una sola espera pendiente por rodeo**, y esto deberá garantizarse también en la base. Una vaca puede ponerse en espera más de una vez (por ejemplo, después de resolverla).
- **RF-14** — Cada registro nuevo de esta spec deberá guardarse en una única operación indivisible, confirmarse al instante en el almacenamiento permanente sin otro botón "Guardar" que "Entrar", y si la tablet se apaga o la app se cierra, existe completo o no existe. **Las esperas pendientes deberán sobrevivir** a cerrar la app y a apagar la tablet (spec 003 RF-35).
- **RF-15** — Las lecturas que llegan mientras hay abierta la **lista de espera** se tratan como las de un cuadro que pide datos: se guarda solo la última y se procesa al cerrarla (spec 003 RF-19), salvo que sea de una vaca en espera, que abre su ficha (RF-5).
- **RF-16** — El sistema no deberá usar la red ni incluir caravanas, nombres de tambo o de veterinarios en registros de error ni en logs (spec 003 RF-32 y RF-33). La interfaz cumple la constitución §9 y las pantallas se piensan **en vertical** (spec 003 RF-51).

## Casos límite
- **Poner en espera con evento u observación elegidos:** se descartan; al volver se elige de nuevo (RF-1).
- **Seguir marcando mientras hay vacas en espera:** cada lectura nueva se procesa normal (marca, ficha o chicharra de CVET 0); las esperas no se afectan. Si la nueva también va a tacto, se puede tactar o ponerla en espera también (RF-1).
- **Vaca en espera que se lee otra vez:** abre su ficha directo, sin aviso de repetida (RF-5).
- **Resolver una espera guardando un tacto rechazado (reloj, estado dado de baja):** el tacto no se guarda, la espera sigue pendiente y se muestra el motivo (spec 003 RF-18, RF-3).
- **Saltar una ficha que no viene de una espera:** solo la cierra; no toca ninguna espera (RF-3).
- **Mirar la ficha de una vaca en espera sin resolverla:** no se puede "solo mirar": al entrar, o se carga un evento o se saltea (RF-3). Cerrar la ficha por otro camino (por ejemplo, el botón Atrás) **no la saca** de la espera.
- **Deshacer el tacto con el que se resolvió una espera:** el tacto se anula y **la espera sigue resuelta** (no vuelve a pendiente); si hace falta, se pone en espera de nuevo.
- **Desmarcar una vaca en espera:** se rechaza (RF-9).
- **Espera olvidada de un día anterior:** sigue en la botonera con su tiempo a la vista y se sale de ella como siempre (RF-4).
- **Cierre automático de la manga mientras hay esperas:** no las afecta (la espera es del rodeo, no de la manga).
- **Cambio de veterinario con esperas pendientes:** las esperas siguen; las resuelve o saltea el veterinario activo.
- **Resetear un rodeo (prueba) con esperas:** las del rodeo viejo quedan con él, ocultas.
- **Control por manga apagado:** En Espera y Rechazo funcionan igual (no dependen de las mangas).
- **Rechazo sobre una vaca de CVET 0:** no aplica (no abre ficha); se puede mirar con "Ver ficha" pero sin evento.
- **Rechazo repetido en la misma vaca:** válido; la ficha muestra el más reciente (RF-6).

## Criterios de aceptación
- [x] Desde una ficha con CVET distinto de 0, "En espera" guarda la espera, cierra la ficha sin tacto y deja leer la siguiente enseguida; la botonera muestra la caravana; con CVET 0 no hay botón (RF-1, RF-2).
- [x] La botonera muestra la caravana de cada vaca en espera (hasta 4, con "+N" para el resto) y un toque abre su ficha con el cartel "en espera" sin sumar al conteo; la lista completa muestra caravana, CVET, hora y tiempo esperando; no hay temporizador (RF-2, RF-3, RF-4).
- [x] Guardar con Entrar un tacto o un rechazo desde una espera la deja **resuelta** en la misma operación; si el registro se rechaza, sigue pendiente (RF-3).
- [x] **Saltar** en una ficha que viene de una espera la deja **salteada** sin guardar registros; en una ficha normal solo cierra (RF-3).
- [x] Ninguna otra acción saca una vaca de la espera: ni el tiempo, ni el cambio de día, ni "Terminar el tacto" (que solo avisa cuántas hay), ni desmarcar (que se rechaza con el motivo) (RF-4, RF-9, RF-10).
- [x] Leer o buscar una vaca en espera abre su ficha directo, sin "Animal repetido" (RF-5).
- [x] "Rechazo" aparece siempre en la lista de eventos; el motivo se escribe a mano en la observación; con Entrar guarda el rechazo sin cambiar el estado ni la fecha de último tacto; se muestra en la ficha, el historial y los movimientos; se deshace y se edita (RF-6, RF-7, RF-8, RF-11).
- [x] Se puede cambiar un tacto por un rechazo y al revés con Editar, en una sola transacción (RF-8).
- [x] **Pruebas de la base:** la espera no se borra ni se modifica salvo pendiente→resuelta o salteada (una vez); no puede haber dos pendientes de la misma vaca en un rodeo; el rechazo no se borra ni se modifica salvo "Anulado" de "no" a "sí"; el historial nuevo no se modifica ni se borra (RF-12, RF-13, RF-6).
- [x] Las esperas pendientes sobreviven a cerrar y reabrir la app (RF-14).
- [x] Con 20.000 vacas, poner en espera, listar y resolver cumple los tiempos de la 003 RF-37 (menos de 300 ms).
- [x] **Verificado en la tablet (2026-11-23)** (no con tests): el botón "En espera" y la botonera se alcanzan con una mano y se leen de un vistazo (constitución §9).
- [x] `dotnet build` y `dotnet test` en verde.

## Decisiones
**Del usuario (2026-11-21):**
1. **Rechazo = un evento más de la lista**; el motivo se escribe a mano en la observación (no hay atajos ni motivos fijos).
2. **Una vaca en espera que se relee abre su ficha directo**, para poner el evento ahí mismo.
3. **La lista de espera se guarda en la base** (sobrevive a cerrar la app).
4. **Qué es En Espera:** poder seguir marcando a los otros animales mientras se tacta uno, para ver si el siguiente va a tacto o si ya se libera la manga; cuando hay veredicto se vuelve a la vaca, se abre su ficha y se pone el evento.
5. **Sin temporizador ni vencimiento:** la vaca está en espera hasta que se le agrega un evento o se la saltea a mano.
6. **La única forma de sacarla de la espera** es entrar a su ficha y poner un evento con Entrar, o saltearla porque no hace falta evento. No hay botón aparte, y desmarcar una vaca en espera se rechaza.

**Cerradas con criterio técnico (revisables):**
7. **La espera no es un registro de campo:** no entra en el "último vigente" ni en el orden compartido, para no bloquear el deshacer de la marca ni de otros registros; es una tabla aparte con su historial.
8. **Una sola espera pendiente por vaca y rodeo,** garantizada también en la base (índice único parcial).
9. **Botonera de acceso directo** con la caravana de cada vaca en espera (2 o 3 a la vez) y lista completa solo como respaldo (RF-2).
10. **El rechazo es otro tipo de registro de campo** (no un estado del catálogo ni un "tacto" con estado especial) para no cambiar el estado de la vaca ni generar un evento de estado hacia Dairy Comp.
11. **Editar** un movimiento puede cambiar entre tacto y rechazo, porque se anula y se guarda uno nuevo (RF-8).
12. **Una ficha de una vaca en espera no es "solo mirar":** se sale con un evento o con Saltar (RF-3), para que no queden esperas colgadas por un toque distraído.

## Supuestos y riesgos
- **Exportación a Dairy Comp:** no se sabe cómo debe expresarse el rechazo (¿evento, campo, observación?); lo define la spec de exportación tras la prueba 0.2. Esta spec solo lo guarda.
- **Cambio de base:** dos tablas nuevas (espera y rechazo) con triggers de inmutabilidad y un índice único parcial; el orden de guardado compartido incluye al rechazo. Constitución §3 y §7. Se define en el plan.
- **Esperas olvidadas:** como no vencen, una vaca puesta en espera y olvidada queda en la botonera (con su tiempo a la vista) hasta que se la resuelva o saltee; el mismo riesgo vale para las de un día anterior (RF-4). Es lo que el usuario pidió.
- **Atrás y cierres:** cerrar la app con una ficha de espera abierta no cambia nada: la espera sigue pendiente (RF-14).
- **Tamaño (constitución §10, máximo 5 tareas):** entra. Orden previsto: (1) entidades, migración y reglas (espera, rechazo, orden compartido); (2) servicios (poner en espera, resolver, saltear; guardar y editar rechazo; movimientos; rechazar desmarcar); (3) ViewModels y lector (abrir directo una vaca en espera); (4) pantalla de trabajo (botón, botonera y lista, evento Rechazo); (5) movimientos, historial y cierre documental.
- **Pantalla sin verificar en navegador:** la interfaz se prueba en la tablet (hoy no hay); la lógica y los ViewModels, con tests.

## Preguntas abiertas
Ninguna pendiente del usuario. Para el plan: cómo distinguir en `ConstructorFicha` y en la botonera la espera de otros estados de la ficha, y el tamaño máximo de la observación de un rechazo (propuesto: el mismo de la observación de la 003, 300 caracteres).

## Diferido a una spec posterior de tratamientos
Estaba en el borrador original (ver el historial de Git, commit `f09f9d0`): catálogo de tratamientos con **Prostal** y **DIB** de ejemplo; panel opcional "Detalle" con tratamientos, nota y días de preñez (0 a 300); **solo tratamiento** (registro sin tacto ni cambio de estado); los **tres renglones de evento + observación** por vaca del Daily; desactivar estados y tratamientos; deshacer un estado o tratamiento recién creado. Se retoma cuando el usuario lo pida; hoy "+ Nuevo evento" (spec 003 RF-39) ya cubre crear un estado desde la lista.
