# 004 — En Espera y Rechazo (nuevos registros de campo)

Estado: borrador (reescrito el 2026-11-21, **sin aprobar**)
Fecha: 2026-11-16 (reescrita: 2026-11-21)

> **Spec hermana de `specs/003-flujo-de-tacto/spec.md`, que ya está implementada.** Esta amplía la ficha, el "último registro vigente" y la lista de últimos movimientos de la 003 (ver sus RF-39 a RF-52). Al aprobarse como 004, la exportación pasa a 005 y el bastón a 006 (`docs/plan.md`). **Nombre de carpeta provisorio:** hoy es `004-registros-y-catalogos`; conviene renombrarla a `004-en-espera-y-rechazo` al aprobar.

## Contexto
**En Espera.** Al escanear una vaca el veterinario empieza a tactarla y, a veces, demora (hay que revisar, esperar un resultado, consultar). Mientras tanto la manga no puede frenarse: se sigue escaneando a las demás. Cuando tiene el veredicto, el veterinario necesita **volver a la vaca que estaba tactando, abrir su ficha y poner el evento**. Hoy, una lectura nueva reemplaza la ficha abierta y la vaca anterior solo se recupera buscándola a mano. Con En Espera el veterinario puede tactar **mientras se sigue adelantando el escaneo**. En el Daily, el botón "En Espera" (rosa) hace esto.

**Rechazo.** El veterinario marca una vaca como **rechazo** (que no sirve para el servicio) con una observación libre; las más usadas son "pata" o "ubre", pero es personalizada. En la ficha actual (Evento + Observación + Entrar) encaja como **un evento más de la lista**, con la observación como motivo.

Todo debe seguir siendo muy rápido y pensado para una mano, en vertical.

## Alcance
- Incluye:
  - **En Espera:** poner una vaca en espera desde su ficha, ver la lista de espera, volver a una vaca y resolverla (con un evento) o sacarla de la espera.
  - **Rechazo:** el evento fijo "Rechazo" en la lista de eventos, con observación y atajos "Patas" y "Ubre"; deshacerlo y editarlo; mostrarlo en ficha, historial y movimientos.
  - Ampliar de la 003 el "último registro vigente", el orden de guardado compartido, el historial y la lista de últimos movimientos al rechazo.
- No incluye (**diferido a una spec posterior de tratamientos**, ver el final):
  - Catálogo de tratamientos (Prostal, DIB), panel "Detalle" con nota y días de preñez, "solo tratamiento" y los **tres renglones de evento + observación** del Daily.
  - Desactivar estados y deshacer un estado recién creado (hoy "+ Nuevo evento" de la 003 crea el estado y alcanza).
  - Un catálogo editable de motivos de rechazo (solo observación libre con dos atajos).
  - Exportar a Excel: cómo se expresan para Dairy Comp el rechazo (y lo demás) lo define la spec de exportación (depende de la prueba 0.2). Esta spec solo lo **guarda**.
  - Sincronización entre tablets, voz y alertas habladas, y el lector real.

## Definiciones
(Las de la spec 003 valen acá; estas las amplían.)
- **Espera:** el registro de que una vaca quedó **pendiente de veredicto** en un rodeo: de qué vaca, cuándo, quién y cómo terminó. Está **pendiente** hasta que se **resuelve** (se guardó un tacto o un rechazo de esa vaca) o se **saca** (el veterinario la quitó sin cargar nada) o **vence** (RF-9).
- **Marca de rechazo:** el evento fijo "Rechazo" de la vaca, con una observación libre opcional (hasta 60 caracteres; atajos "Patas" y "Ubre"). **No cambia el estado de la vaca ni su fecha de último tacto** y no genera un evento de estado hacia Dairy Comp.
- **Registro de campo:** en esta spec, una marca de pasada, un tacto o una marca de rechazo. **La espera no es un registro de campo**: no cuenta como "último registro vigente" ni bloquea deshacer otros registros.
- **Último registro vigente de una vaca:** el registro de campo vigente con fecha y hora más reciente entre los **tres** tipos juntos; si empatan, gana el que se guardó después (orden de guardado compartido, spec 003 RF-21).
- **Vaca que entra en tacto:** la de CVET distinto de 0 (spec 003 RF-40). Solo estas pueden ponerse en espera o tener rechazo.

## Requisitos funcionales

### En Espera
- **RF-1** — Cuando hay una ficha abierta de una vaca que entra en tacto, el sistema deberá ofrecer en la barra de abajo, junto a "Saltar" y "Entrar", el botón **"En espera"**. Al tocarlo deberá guardar la espera de esa vaca en el rodeo en trabajo (con el veterinario, la fecha y hora del reloj y el dispositivo, en una única operación indivisible, con su registro de historial), **cerrar la ficha sin guardar ningún tacto** y dejar la pantalla lista para la siguiente lectura casi de inmediato. Si la vaca ya estaba en espera, no deberá crear otra. El evento y la observación que se habían elegido **no se guardan**: al volver se empieza de cero. Una vaca con CVET 0 no puede ponerse en espera (no tiene botón).
- **RF-2** — El sistema deberá mostrar siempre, en la barra de arriba de la pantalla de trabajo, un acceso **"En espera: N"** con la cantidad de vacas pendientes del rodeo en trabajo (oculto o apagado en 0), que abre la **lista de espera**. La lista muestra, de la más antigua a la más reciente, la caravana, el CVET, la hora en que se puso en espera y hace cuánto espera, con un botón para abrir cada una. Debe seguir siendo usable con 20 o más vacas pendientes.
- **RF-3** — Cuando el veterinario abre una vaca desde la lista de espera, el sistema deberá mostrar su ficha normal (con Evento, Observación, Datos, Historial, Saltar y Entrar) **sin volver a marcarla ni sumarla al conteo** y marcada como "en espera" (un cartel visible). Al guardar con **Entrar** un tacto (spec 003 RF-39) o un rechazo (RF-6), la espera deberá quedar **resuelta** en la misma operación indivisible que guarda el registro (si el registro se rechaza, la espera sigue pendiente). **Saltar** cierra la ficha y la espera sigue pendiente.
- **RF-4** — Cuando el veterinario quiera descartar una espera sin cargar nada (por ejemplo, la vaca no necesitaba tacto), el sistema deberá ofrecer en la lista de espera el botón **"Sacar de la espera"** con una confirmación, que marca la espera como **sacada** (quién y cuándo) sin guardar ningún registro de campo.
- **RF-5** — Cuando se lee (o se busca a mano) una vaca que está **en espera**, el sistema deberá **abrir directamente su ficha** como en RF-3 (con el cartel "en espera"), sin el aviso de "Animal repetido" ni "Ya pasó" y sin marcarla otra vez. Una vaca que ya fue resuelta o sacada vuelve a comportarse como cualquier vaca ya procesada (spec 003 RF-22).
- **RF-9** — Cuando se empieza a trabajar un rodeo, el sistema deberá dejar como **vencidas** las esperas pendientes de un día local anterior (red de seguridad, como el cierre automático de una manga abierta, spec 003 RF-10): no se muestran más en la lista ni cuentan en "En espera: N". No hay corte automático a medianoche con la app abierta. *(Criterio técnico revisable: la alternativa es que sigan pendientes hasta resolverlas.)*
- **RF-10** — Cuando el veterinario toca "Terminar el tacto" y hay esperas pendientes, el sistema deberá avisarlo en la pregunta final ("Hay N vacas en espera") junto a la advertencia de manga abierta de la 003 RF-46, sin impedir terminar. Al terminar, las esperas pendientes quedan **vencidas**.

### Rechazo
- **RF-6** — El sistema deberá incluir en la lista de eventos de la ficha (spec 003 RF-39) el evento fijo **"Rechazo"**, que no pertenece al catálogo de estados, aparece **siempre** y no puede desactivarse. Al elegirlo, deberá ofrecer en la observación los atajos **"Patas"** y **"Ubre"** (un toque los escribe) además del texto libre de hasta 60 caracteres (opcional). Con **Entrar** deberá guardar la marca de rechazo de esa vaca con la observación (si hay), el veterinario, la fecha y hora del reloj, el dispositivo y su registro de historial, en una única operación indivisible, **sin cambiar el estado de la vaca ni su fecha de último tacto**. Una vaca puede tener varias marcas de rechazo (la ficha muestra la vigente más reciente). Es un registro que solo se agrega (no se modifica ni se borra, también en la base), con la única modificación permitida de pasar su marca "Anulado" de "no" a "sí" (con quién y cuándo) al deshacerse, junto con un registro de historial. Su observación no se modifica una vez guardada.
- **RF-7** — El sistema deberá tratar la marca de rechazo como un registro de campo en todo lo que la spec 003 define sobre el "último registro vigente": el **orden de guardado compartido** (RF-21), la **validación de reloj** (RF-21), **deshacer** el último registro de la vaca (RF-26; para el rechazo marca el registro como anulado con su registro de historial), el aviso de animal repetido o ya pasó (RF-22, que puede mostrar "Rechazo · Patas" como último registro y ofrece "Corregir" cuando lo último no es la marca de pasada) y **"Últimos movimientos"** (RF-48: hora, caravana con CVET y debajo "Rechazo · Patas"; con **Deshacer** y **Editar** cuando es el último registro vigente de la vaca).
- **RF-8** — El sistema deberá permitir **editar** una marca de rechazo desde "Últimos movimientos" con las mismas reglas de la 003 RF-49: se anula el registro y se guarda uno nuevo con el evento y la observación elegidos, en una sola transacción. Se puede **cambiar un tacto por un rechazo y viceversa** (el evento elegido decide qué tipo de registro se guarda); si el nuevo se rechaza, no cambia nada y el original sigue vigente.
- **RF-11** — El sistema deberá mostrar en la ficha, además de lo de la 003, si la vaca está marcada como **rechazo** (la marca vigente más reciente con su observación, en una franja visible) y sumar al **Historial** de la ficha los rechazos con su observación, con los anulados marcados como tales.

### Historial y transversales
- **RF-12** — El sistema deberá registrar en el historial (solo agregar, spec 001 RF-10), con el veterinario y la fecha y hora, estas acciones nuevas: **espera creada**, **espera resuelta**, **espera sacada**, **espera vencida**, **rechazo creado** y **rechazo anulado**. La base rechaza modificar o borrar cualquier registro de historial nuevo.
- **RF-13** — La espera deberá ser un registro que solo se agrega: no se modifica ni se borra (también en la base), salvo pasar de **pendiente** a **resuelta**, **sacada** o **vencida** (una sola vez, con quién y cuándo, y qué registro la resolvió). Una vaca puede tener **una sola espera pendiente por rodeo**, y esto deberá garantizarse también en la base. Una vaca puede ponerse en espera más de una vez (por ejemplo, después de resolverla).
- **RF-14** — Cada registro nuevo de esta spec deberá guardarse en una única operación indivisible, confirmarse al instante en el almacenamiento permanente sin botón "Guardar" aparte de "Entrar", y si la tablet se apaga o la app se cierra, existe completo o no existe. **Las esperas pendientes deberán sobrevivir** a cerrar la app (spec 003 RF-35).
- **RF-15** — Las lecturas que llegan mientras hay abierta la **lista de espera** se tratan como las de un cuadro que pide datos: se guarda solo la última y se procesa al cerrarla (spec 003 RF-19), salvo que sea de una vaca en espera, que abre su ficha (RF-5).
- **RF-16** — El sistema no deberá usar la red ni incluir caravanas, nombres de tambo o de veterinarios en registros de error ni en logs (spec 003 RF-32 y RF-33). La interfaz cumple la constitución §9 y las pantallas se piensan **en vertical** (spec 003 RF-51).

## Casos límite
- **Poner en espera con evento u observación elegidos:** se descartan; al volver se elige de nuevo (RF-1).
- **Vaca en espera que se lee otra vez:** abre su ficha directo, sin aviso de repetida (RF-5).
- **Dos vacas en espera y llegan lecturas seguidas:** cada una queda marcada normalmente; las de espera no se ven afectadas (RF-1).
- **Resolver una espera guardando un tacto rechazado (reloj, estado dado de baja):** el tacto no se guarda, la espera sigue pendiente y se muestra el motivo (spec 003 RF-18, RF-3).
- **Deshacer el tacto con el que se resolvió una espera:** el tacto se anula, **la espera sigue resuelta** (no vuelve a pendiente); si hace falta, se pone en espera de nuevo.
- **Desmarcar (deshacer la marca de pasada) una vaca en espera:** se rechaza o se resuelve según la 003 RF-26; deberá **sacar la espera** de esa vaca en la misma operación, porque ya no está procesada. *(A confirmar en el plan.)*
- **Cierre automático de la manga mientras hay esperas:** no las afecta (la espera es del rodeo, no de la manga).
- **Cambio de veterinario con esperas pendientes:** las esperas siguen; las resuelve el veterinario activo al guardar.
- **Resetear un rodeo (prueba) con esperas:** las del rodeo viejo quedan con él, ocultas.
- **Control por manga apagado:** En Espera y Rechazo funcionan igual (no dependen de las mangas).
- **Lista de espera vacía:** el acceso no aparece (RF-2).
- **Rechazo sobre una vaca de CVET 0:** no aplica (no abre ficha); se puede mirar con "Ver ficha" pero sin evento.
- **Rechazo repetido en la misma vaca:** válido; la ficha muestra el más reciente (RF-6).

## Criterios de aceptación
- [ ] Desde una ficha con CVET distinto de 0, "En espera" guarda la espera, cierra la ficha sin tacto y deja leer la siguiente enseguida; el acceso "En espera: N" sube; con CVET 0 no hay botón (RF-1, RF-2).
- [ ] La lista de espera muestra caravana, CVET, hora y tiempo esperando; abrir una vaca de la lista muestra su ficha con el cartel "en espera" sin sumar al conteo (RF-2, RF-3).
- [ ] Guardar con Entrar un tacto o un rechazo desde una espera la deja **resuelta** en la misma operación; si el registro se rechaza, sigue pendiente; "Saltar" no la cambia (RF-3).
- [ ] "Sacar de la espera" (con confirmación) la deja **sacada** sin guardar registros de campo (RF-4).
- [ ] Leer o buscar una vaca en espera abre su ficha directo, sin "Animal repetido" (RF-5).
- [ ] Una espera de un día anterior queda vencida al empezar a trabajar y al terminar el tacto; "Terminar el tacto" avisa cuántas hay pendientes (RF-9, RF-10).
- [ ] "Rechazo" aparece siempre en la lista de eventos, con atajos "Patas" y "Ubre"; con Entrar guarda el rechazo sin cambiar el estado ni la fecha de último tacto; se muestra en la ficha, el historial y los movimientos; se deshace y se edita (RF-6, RF-7, RF-8, RF-11).
- [ ] Se puede cambiar un tacto por un rechazo y al revés con Editar, en una sola transacción (RF-8).
- [ ] **Pruebas de la base:** la espera no se borra ni se modifica salvo pendiente→resuelta, sacada o vencida (una vez); no puede haber dos pendientes de la misma vaca en un rodeo; el rechazo no se borra ni se modifica salvo "Anulado" de "no" a "sí"; el historial nuevo no se modifica ni se borra (RF-12, RF-13, RF-6).
- [ ] Las esperas pendientes sobreviven a cerrar y reabrir la app (RF-14).
- [ ] Con 20.000 vacas, listar las esperas, ponerlas y resolverlas cumple los tiempos de la 003 RF-37 (menos de 300 ms).
- [ ] **A verificar en la tablet** (no con tests): el botón "En espera" y el acceso con su cantidad se alcanzan con una mano; la lista de espera se lee de un vistazo; los atajos "Patas" y "Ubre" (constitución §9).
- [ ] `dotnet build` y `dotnet test` en verde.

## Decisiones
**Del usuario (2026-11-21):**
1. **Rechazo = un evento más de la lista**, con la observación como motivo; no es un botón aparte.
2. **Una vaca en espera que se vuelve a leer abre su ficha directo**, para poner el evento ahí mismo.
3. **La lista de espera se guarda en la base** (sobrevive a cerrar la app).
4. **Qué es En Espera:** al escanear una vaca el veterinario empieza a tactarla y suele demorar; mientras tanto se siguen escaneando otras, y cuando hay veredicto se busca la vaca en una lista de espera, se abre su ficha y se pone el evento. Es tactar en paralelo al escaneo.

**Cerradas con criterio técnico (revisables):**
5. **La espera no es un registro de campo:** no entra en el "último vigente" ni en el orden compartido, para no bloquear el deshacer de la marca ni de otros registros; es una tabla aparte con su historial.
6. **Una sola espera pendiente por vaca y rodeo,** garantizada también en la base (índice único parcial).
7. **Vencen al cambiar el día** (al empezar a trabajar o al terminar el tacto), como la red de seguridad de las mangas; la alternativa es dejarlas pendientes hasta resolverlas (RF-9).
8. **El rechazo es otro tipo de registro de campo** (no un estado del catálogo ni un "tacto" con estado especial) para no cambiar el estado de la vaca ni generar un evento de estado hacia Dairy Comp.
9. **Editar** un movimiento puede cambiar entre tacto y rechazo, porque se anula y se guarda uno nuevo (RF-8).

## Supuestos y riesgos
- **Exportación a Dairy Comp:** no se sabe cómo debe expresarse el rechazo (¿evento, campo, observación?); lo define la spec de exportación tras la prueba 0.2. Esta spec solo lo guarda.
- **Cambio de base:** dos tablas nuevas (espera y rechazo) con triggers de inmutabilidad y un índice único parcial; el orden de guardado compartido incluye al rechazo. Constitución §3 y §7. Se define en el plan.
- **Esperas de varios días:** si el tambo trabaja en varios días seguidos con la misma vaca esperando, el vencimiento diario puede ser molesto (RF-9, criterio revisable).
- **Una vaca en espera cuya marca se desmarca:** la regla de "sacar la espera" se confirma en el plan (casos límite).
- **Tamaño (constitución §10, máximo 5 tareas):** entra. Orden previsto: (1) entidades, migración y reglas (espera, rechazo, orden compartido); (2) servicios (poner en espera, resolver, sacar, vencer; guardar y editar rechazo; movimientos); (3) ViewModels y lector (abrir directo una vaca en espera); (4) pantalla de trabajo (botón, acceso y lista de espera, evento Rechazo con atajos); (5) movimientos, historial y cierre documental.
- **Pantalla sin verificar en navegador:** la interfaz se prueba en la tablet (hoy no hay tablet); la lógica y los ViewModels, con tests.

## Preguntas abiertas
- ¿Las esperas vencen al cambiar el día o siguen pendientes hasta resolverlas? (RF-9)
- Al **desmarcar** una vaca en espera, ¿se saca la espera sola? (propuesto: sí)
- ¿El atajo del rechazo debe tener más motivos fijos además de "Patas" y "Ubre"?
- ¿Cuántas vacas en espera suele haber a la vez? (define el diseño de la lista)

## Diferido a una spec posterior de tratamientos
Estaba en el borrador original (ver el historial de Git, commit `f09f9d0`): catálogo de tratamientos con **Prostal** y **DIB** de ejemplo; panel opcional "Detalle" con tratamientos, nota y días de preñez (0 a 300); **solo tratamiento** (registro sin tacto ni cambio de estado); los **tres renglones de evento + observación** por vaca del Daily; desactivar estados y tratamientos; deshacer un estado o tratamiento recién creado. Se retoma cuando el usuario lo pida; hoy "+ Nuevo evento" (spec 003 RF-39) ya cubre crear un estado desde la lista.
