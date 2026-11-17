# 004 — Registros y catálogos de campo (tratamientos, rechazo, estados nuevos)

Estado: borrador
Fecha: 2026-11-16

> **Nombre y número provisorios.** Spec hermana de `specs/003-flujo-de-tacto/spec.md`, **sin aprobar**. **Depende de la 003**: no se puede planificar ni implementar hasta que la 003 esté aprobada y hecha, porque amplía su ficha, su "último registro vigente", su lista de últimos movimientos y su orden de guardado. `docs/plan.md` reserva hoy 004 para la exportación y 005 para el bastón; al aprobarse esta spec como 004, **la exportación pasaría a 005 y el bastón a 006**. Queda **pendiente editar `docs/plan.md`** (el planificador no lo toca).

## Contexto
El veterinario, además de tactar, necesita dejar constancia de lo que hace en la manga más allá del estado: a la vaca "se le inyecta prostal para sincronizar celos o se usan unos DIB para controlar los ciclos", hay "un posible evento de sincronización de celos y tratamientos", puede marcar una vaca como **rechazo** ("se suele marcar como un evento fijo 'rechazo' y te permite agregar una observación que ponés como vos querés y los más usados son pata o ubre, pero es totalmente personalizado la observación"), y necesita **agregar estados nuevos al catálogo** desde el campo (por ejemplo "aborto" en una vaca preñada) sin esperar un cambio del programa. También pidió que un animal nuevo "se pueda guardar sin tener un evento como preñez o vacía" (eso ya lo cubre la spec 003). Todo debe ser muy rápido: sin abrir nada extra, el flujo sigue siendo un toque por vaca (spec 003).

Esta spec agrega, encima del núcleo de la 003: el catálogo de tratamientos, el panel de detalle del tacto (tratamientos, nota y días de preñez), el registro de "solo tratamiento", el rechazo, crear estados y tratamientos en campo, desactivarlos y deshacer uno recién creado.

## Alcance
- Incluye:
  - Catálogo de tratamientos con Prostal y DIB de ejemplo; agregarlos y desactivarlos desde el campo.
  - Panel opcional "Detalle" en la ficha: tratamientos, nota libre y días de preñez (0 a 300) del tacto.
  - **Solo tratamiento**: registro de tratamientos sin tacto y sin cambiar el estado (vacas sin estado, altas en campo).
  - **Marca de rechazo**: evento fijo "rechazo" con observación libre opcional de hasta 60 caracteres y atajos "Patas" y "Ubre".
  - Crear estados nuevos desde el campo (con su código de Dairy Comp), aplicarlos en el mismo paso, desactivarlos, y deshacer uno recién creado.
  - Ampliar de la 003 el "último registro vigente", la ficha, el historial, el aviso de animal repetido, la lista de últimos movimientos y el orden de guardado a los dos tipos nuevos de registro.
- No incluye:
  - Un catálogo editable de motivos de rechazo (solo observación libre con los atajos fijos).
  - Renombrar estados o tratamientos desde la pantalla, y reactivar uno desactivado salvo lo previsto en RF-4 y RF-8.
  - Exportar a Excel: cómo se expresan para Dairy Comp los tratamientos, observaciones, rechazos, estados nuevos y solo tratamientos lo define la spec de exportación (depende de la prueba 0.2). Esta spec solo los **guarda**. No se supone cómo los recibe Dairy Comp.
  - Voz, reglas de alerta y el historial completo de inseminaciones (ideas para specs posteriores, ver spec 003).
  - Todo lo que no está en la 003 ni acá (lector real, sincronización, etc.).

## Definiciones
(Las de la spec 003 valen acá; estas las amplían o agregan.)
- **Tratamiento:** una práctica o medicación elegida de un catálogo (por ejemplo Prostal, DIB).
- **Solo tratamiento:** registro de uno o más tratamientos (con nota opcional) sin tacto: no cambia el estado de la vaca ni su fecha de último tacto, y no genera un evento de estado hacia Dairy Comp. Sirve también para vacas sin estado y altas en campo.
- **Marca de rechazo:** el evento fijo "rechazo" de la vaca, con una observación libre opcional (hasta 60 caracteres; atajos "Patas" y "Ubre"). No cambia el estado de la vaca.
- **Registro de campo:** una marca de pasada, un tacto, un solo tratamiento o una marca de rechazo (esta spec amplía la definición de la 003, que solo tenía los dos primeros).
- **Último registro vigente de una vaca:** el registro de campo vigente con fecha y hora más reciente, entre los **cuatro** tipos juntos; si dos tienen la misma, gana el que se guardó después (orden de guardado compartido, spec 003 RF-21).
- **Estado o tratamiento creado en la app:** el que se agregó desde la pantalla de campo; los estados iniciales del catálogo (los de Dairy Comp, spec 001 RF-6) no lo son.
- **Anulado (catálogo):** un estado o tratamiento creado en la app que se deshizo antes de usarse (RF-11): no ocupa su nombre ni su código y no puede elegirse.

## Requisitos funcionales

### Tratamientos y detalle del tacto
- **RF-1** — Cuando se crea la base por primera vez, o la app inicia con una base que todavía no los tiene, el sistema deberá cargar en el catálogo de tratamientos los de ejemplo **Prostal** y **DIB**, y deberá agregar solo los que falten en el futuro, sin modificar los existentes ni reactivar los que el usuario desactivó (misma regla que los estados iniciales, spec 001 RF-6).
- **RF-2** — Donde el veterinario quiera dejar más datos, el sistema deberá ofrecer en la ficha un panel opcional "Detalle" que se completa **antes** de tocar el estado o "Solo tratamiento" y que, si no se abre, no agrega ningún toque: uno o más tratamientos activos del catálogo, una nota de texto libre y los días de preñez (0 a 300, solo para el tacto). Con un estado, los tratamientos, la nota y los días se guardan junto con el tacto en la misma operación indivisible de la spec 003 RF-16; con "Solo tratamiento", los tratamientos y la nota se guardan en el registro de RF-5 (los días de preñez no aplican y se ignoran con aviso). El Detalle no se arrastra de una vaca a otra: cada ficha empieza con el Detalle vacío, y si una lectura de otra vaca reemplaza una ficha con Detalle cargado sin guardar, el sistema deberá avisar brevemente que se descartó (spec 003 RF-19). Las lecturas que llegan con un cuadro de esta spec abierto se tratan como en la 003 RF-19.
- **RF-3** — Cuando el veterinario toca "Agregar tratamiento" en el panel Detalle, el sistema deberá pedir el nombre (recortado, obligatorio, hasta 50 caracteres), agregarlo al catálogo de tratamientos y dejarlo elegible de inmediato en cualquier ficha. El nombre no puede repetirse (sin distinguir mayúsculas) con otro tratamiento activo o desactivado (los anulados no cuentan): en ese caso deberá ofrecer reactivar el desactivado. Los tratamientos no se borran.
- **RF-4** — El sistema deberá permitir desactivar cualquier tratamiento del catálogo, con un botón y confirmación: deja de aparecer en el Detalle y los registros ya guardados conservan sus tratamientos y su nombre. Reactivar solo se ofrece al intentar crear uno con el mismo nombre (RF-3).

### Solo tratamiento
- **RF-5** — Cuando el veterinario carga uno o más tratamientos (y opcionalmente una nota) y toca "Solo tratamiento", el sistema deberá guardar un registro de solo tratamiento de esa vaca con sus tratamientos, la nota, el veterinario activo, la fecha y hora del reloj, el dispositivo y su registro de historial, en una única operación indivisible, sin cambiar el estado ni la fecha de último tacto de la vaca, y confirmarlo como un tacto (spec 003 RF-16). Se debe elegir al menos un tratamiento. Vale para cualquier vaca, incluidas las que no tienen estado y las altas en campo. Es un registro que solo se agrega (no se modifica ni se borra, también en la base), con la única modificación permitida de pasar su marca "Anulado" de "no" a "sí" (con quién y cuándo) cuando se deshace, junto con un registro de historial; sus tratamientos y nota no se modifican una vez guardados.

### Rechazo
- **RF-6** — Cuando el veterinario toca "Rechazo" en la ficha, el sistema deberá ofrecer un cuadro con los atajos "Patas" y "Ubre" y un campo de texto corto (hasta 60 caracteres, opcional) para una observación libre, y guardar una marca de rechazo de esa vaca con la observación (si hay), el veterinario activo, la fecha y hora del reloj, el dispositivo y su registro de historial, en una única operación indivisible, sin cambiar el estado de la vaca. Un atajo guarda con un toque; también se puede guardar sin observación. Una vaca puede tener varias marcas de rechazo (la ficha muestra la vigente más reciente). Es un registro que solo se agrega, con la misma garantía y la misma única modificación permitida que el solo tratamiento (RF-5); su observación no se modifica una vez guardada. La forma de exportarlo a Dairy Comp la define la spec de exportación.

### Estados nuevos del catálogo
- **RF-7** — Cuando el veterinario toca "Agregar estado" (en la ficha), el sistema deberá pedir el nombre y el código de Dairy Comp del estado, proponiendo el nombre en mayúsculas como código (editable), y agregarlo al catálogo con las reglas de la spec 001 RF-5. Reglas: el nombre y el código de Dairy Comp se recortan, son obligatorios y tienen como máximo 50 y 30 caracteres (límites de Aviss, no de Dairy Comp); el nombre no puede repetirse (sin distinguir mayúsculas) con un estado activo o desactivado (los anulados no cuentan), y en ese caso el sistema deberá ofrecer reactivar el desactivado en lugar de crear otro; el código de Dairy Comp no puede repetirse entre los estados activos; el código interno se genera solo a partir del nombre (mayúsculas, sin acentos, espacios como guion bajo, sin otros símbolos, con un número al final si ya existe) y si no queda ningún carácter válido se rechaza. Deberá advertir que el código se confirma con Dairy Comp. Si se rechaza, deberá explicar el motivo y no crear nada. El estado queda marcado como "creado en la app".
- **RF-8** — Cuando se agrega un estado nuevo, el sistema deberá mostrarlo de inmediato entre los botones de estado de cualquier ficha, y deberá ofrecer aplicarlo en el mismo paso a la vaca que está abierta. Esto son dos operaciones seguidas: primero se crea el estado y, si se creó, se registra el tacto. Si el tacto es rechazado, el estado nuevo queda creado, la ficha sigue abierta con el nuevo botón y el mensaje indica que el estado se creó pero el tacto no se guardó y por qué.
- **RF-9** — Donde un estado fue creado en la app, el sistema deberá permitir desactivarlo con un botón de desactivar (con confirmación): deja de aparecer entre los botones y no puede elegirse para tactos nuevos, y lo ya registrado conserva su historial y su nombre. Los estados iniciales del catálogo (los de Dairy Comp) no se pueden desactivar desde la pantalla porque la importación depende de ellos. Si el nombre se escribió mal, se desactiva (o se deshace, RF-11) y se crea de nuevo bien escrito. Una vaca cuyo estado actual fue desactivado lo sigue mostrando en la ficha, y se puede tactar con cualquier estado activo.

### Deshacer lo creado
- **RF-10** — Cuando se crea un estado o un tratamiento desde la app, el sistema deberá mostrar durante 5 segundos un aviso con el botón "Deshacer" (como los de la spec 003 RF-27), y deberá ofrecer el mismo deshacer en la lista de últimos movimientos.
- **RF-11** — Cuando el veterinario deshace la creación de un estado o tratamiento, el sistema deberá marcarlo como anulado (sin borrarlo, de "no" a "sí", con quién y cuándo) y dejarlo sin poder elegirse, liberando su nombre y su código de Dairy Comp, solo si **todavía no se usó** en ningún registro (ni vigente ni anulado) ni se aplicó a ninguna vaca. Si ya se usó, se rechaza con el motivo y se ofrece desactivarlo (RF-4, RF-9). Solo se pueden deshacer los creados en la app. El anulado no vuelve a "no": para tenerlo otra vez se crea uno nuevo.

### Ampliación de lo de la spec 003
- **RF-12** — El sistema deberá incluir el solo tratamiento y el rechazo como tipos de registro de campo en todo lo que la spec 003 define sobre el "último registro vigente": el orden de guardado compartido (spec 003 RF-21), el deshacer del último registro de la vaca (spec 003 RF-26, que para estos dos tipos marca el registro como anulado con su registro de historial), el aviso de animal repetido o ya pasó, que puede mostrar un solo tratamiento o un rechazo como último registro (spec 003 RF-22; "Corregir" se ofrece cuando lo último no es la marca de pasada), la lista de últimos movimientos (spec 003 RF-28) y la validación de reloj (spec 003 RF-21). Una vaca con solo un registro de solo tratamiento o de rechazo sigue contando como procesada hoy gracias a su marca de pasada (spec 003 RF-12).
- **RF-13** — El sistema deberá mostrar en la ficha, además de lo de la spec 003 RF-13, si la vaca está marcada como rechazo (la marca de rechazo vigente más reciente con su observación), y deberá sumar al historial de la ficha (spec 003 RF-14) los tactos con sus días de preñez y tratamientos, los registros de solo tratamiento y las marcas de rechazo, con los anulados marcados como tales (las marcas de pasada siguen sin aparecer).
- **RF-14** — Cuando el veterinario abre una ficha, el sistema deberá ofrecer además de los botones de estado de la spec 003 RF-15: el botón "Solo tratamiento", el botón "Rechazo", el acceso al panel "Detalle" y un botón para agregar un estado nuevo. Sin estados activos, no se puede tactar, pero sí registrar solo tratamiento y rechazo; el sistema lo indica y ofrece agregar un estado.
- **RF-15** — Si un registro se anula, entonces el sistema deberá conservar sus tratamientos, su nota y su observación tal como se guardaron y mostrarlos marcados como anulados junto al registro.
- **RF-16** — El sistema deberá registrar en el historial (solo agregar) las acciones nuevas: **solo tratamiento creado** y **anulado**; **rechazo creado** y **anulado**. Los estados y tratamientos del catálogo no generan historial, como en la spec 001 (guardan fecha de creación y de modificación). La base rechaza modificar o borrar cualquier registro de historial nuevo.

### Transversales
- **RF-17** — Cada registro nuevo de esta spec deberá guardarse en una única operación indivisible, confirmarse al instante en el almacenamiento permanente sin botón "Guardar", y si la tablet se apaga o la app se cierra, existe completo o no existe (spec 003 RF-35).
- **RF-18** — El sistema no deberá usar la red ni incluir caravanas, nombres de tambo o de veterinarios en registros de error ni en logs (spec 003 RF-32 y RF-33).
- **RF-19** — La interfaz deberá cumplir la constitución §9: botones grandes de contraste alto, un toque por vaca sin abrir el Detalle, escritura mínima (atajos en el rechazo, tratamientos elegibles del catálogo) y toda acción de esta spec deshacible (a verificar en el emulador).
- **RF-20** — Con 20.000 vacas y 100.000 registros, guardar un registro de solo tratamiento o de rechazo, o un tacto con tratamientos, deberá tardar menos de 300 ms (medido en test, no en la tablet).

## Casos límite
- **Sin conexión:** todo funciona sin Internet (RF-18).
- **Corte de energía:** a mitad de un solo tratamiento, un rechazo, un tacto con tratamientos, la creación o la desactivación de un estado o tratamiento, o un deshacer, al reabrir existe completo o no existe (RF-17).
- **Estado o tratamiento repetido al agregarlo:** se rechaza explicando cuál es el repetido u ofrece reactivar el desactivado (RF-3, RF-7).
- **Estado o tratamiento creado y deshecho enseguida:** se anula; el nombre y el código quedan libres y se puede crear de nuevo (RF-11).
- **Estado o tratamiento ya usado:** no se puede deshacer su creación; solo desactivarlo (RF-11).
- **Estado nuevo creado pero tacto rechazado:** el estado queda; se avisa (RF-8).
- **Estado desactivado con la ficha abierta:** al tocarlo se rechaza con motivo y no se cambia nada (spec 003 RF-18).
- **Vaca sin estado con solo un tratamiento:** se guarda como solo tratamiento, sin inventar un estado (RF-5).
- **Alta en campo con tratamiento o rechazo:** válido; queda con su marca de pasada y sus registros nuevos; no se puede anular el alta mientras tenga registros vigentes además de su marca (spec 003 RF-29, ampliado por RF-12).
- **Sin tratamientos activos:** "Solo tratamiento" no se puede completar; se ofrece agregar uno (RF-3, RF-5).
- **Rechazo repetido o equivocado:** se pueden registrar varios; la ficha muestra el vigente más reciente; si ya hay registros posteriores en la vaca, el rechazo anterior no se puede deshacer (regla del último vigente) y se corrige registrando otro (RF-6, RF-12).
- **Reloj mal puesto:** los registros nuevos se rechazan igual que el tacto, con aviso para revisar la hora (RF-12, spec 003 RF-21).
- **Lista de estados o tratamientos larga:** debe seguir siendo usable con un toque; el criterio de orden se define en el plan.
- **Varios veterinarios, tambos o tablets:** igual que en la spec 003; el catálogo es de la tablet y lo comparten todos los tambos.
- **Excel y exportación:** esta spec solo guarda; no hay Excel acá.

## Criterios de aceptación
- [ ] Prostal y DIB vienen cargados; se agrega un tratamiento nuevo y queda elegible de inmediato; se repite el nombre y se rechaza u ofrece reactivar; cualquier tratamiento se desactiva sin afectar lo guardado (RF-1, RF-3, RF-4).
- [ ] Se registra un tacto con nota, uno o varios tratamientos y días de preñez, todo en la misma operación; sin abrir el Detalle sigue siendo un toque por vaca; el Detalle no pasa de una vaca a otra y avisa si se descartó uno (RF-2, RF-19).
- [ ] "Solo tratamiento" guarda un registro con sus tratamientos y nota, sin cambiar estado ni fecha de último tacto, en una vaca sin estado y en un alta en campo; se puede deshacer (RF-5, RF-12).
- [ ] "Rechazo" guarda con un toque en "Patas" o "Ubre", con texto breve de hasta 60 caracteres o sin observación; se muestra en la ficha y puede deshacerse (RF-6, RF-13).
- [ ] **Pruebas de la base por tabla nueva** (una por tabla, que compruebe que la base rechaza modificar o borrar aunque el intento no venga de la aplicación): registro de solo tratamiento (y sus tratamientos), marca de rechazo, tratamientos del tacto; y que solo se acepta anulado de "no" a "sí" en los registros anulables (RF-5, RF-6, RF-16).
- [ ] Se agrega un estado nuevo ("aborto") con el código propuesto editable, aparece de inmediato como botón y se puede aplicar a la vaca abierta; si el tacto es rechazado el estado queda y se avisa; los repetidos se rechazan con motivo u ofrecen reactivar (RF-7, RF-8).
- [ ] Un estado creado en la app se desactiva y desaparece de los botones conservando lo registrado; los estados iniciales no ofrecen desactivar (RF-9).
- [ ] Deshacer la creación de un estado o tratamiento sin uso lo anula, libera su nombre y su código y se puede crear de nuevo; con uso se rechaza y ofrece desactivar; un anulado no vuelve a "no" (RF-10, RF-11).
- [ ] El solo tratamiento y el rechazo entran en el último registro vigente: el deshacer, el aviso de animal repetido (con "Corregir"), la lista de últimos movimientos y el orden de guardado compartido los tratan igual que al tacto y a la marca (RF-12).
- [ ] La ficha muestra el rechazo vigente y el historial suma tactos con tratamientos, solo tratamientos y rechazos con los anulados marcados; anular un registro conserva sus datos (RF-13, RF-15).
- [ ] El historial registra solo tratamiento y rechazo creados y anulados con su veterinario y fecha (RF-16).
- [ ] Toda acción de esta spec se puede deshacer (RF-5, RF-6, RF-11, RF-19).
- [ ] Con el volumen de RF-20, los registros nuevos cumplen el tiempo indicado (RF-20).
- [ ] No hay llamadas de red ni datos de animales en los logs (RF-18).
- [ ] **A verificar en el emulador** (no se prueban con tests): tamaño y contraste de los botones, que el Detalle no tape los estados, los atajos "Patas" y "Ubre", y que un tacto siga siendo un toque (RF-14, RF-19).
- [ ] `dotnet build` y `dotnet test` en verde.

## Decisiones
**Del responsable del proyecto (movidas de la spec 003, con sus textos):**
1. **Tratamientos y observaciones:** catálogo de tratamientos más una nota libre por tacto (la observación del tacto que ya existe). Prostal y DIB se siembran como ejemplos (RF-1); no se supone cómo los recibe Dairy Comp.
2. **Solo tratamiento sin tacto (opción A):** un tratamiento se registra sin estado y sin tacto con un registro propio (no es tacto, no cambia el estado, no genera evento de estado hacia Dairy Comp), y sirve para vacas sin estado y altas en campo (RF-5). Un animal nuevo se guarda sin estado y sin evento; no se inventa un estado falso (spec 003 RF-25).
3. **Estado nuevo desde campo:** pide nombre y código de Dairy Comp, con el nombre en mayúsculas propuesto y editable (RF-7).
4. **Desactivar:** se pueden desactivar estados y tratamientos creados en campo con un botón; desaparecen de los botones y lo ya registrado conserva su historial; un nombre mal escrito se corrige desactivando y recreando (RF-4, RF-9).
5. **Rechazo:** es un evento fijo "rechazo", con observación libre de hasta 60 caracteres y atajos "Patas" y "Ubre"; no es un catálogo editable. Se guarda y se muestra en la ficha; cómo se exporta a Dairy Comp queda como riesgo para la spec de exportación (RF-6).
6. **Días de preñez:** opcionales (0 a 300) dentro del Detalle (RF-2).
7. **Todo se puede deshacer:** también anular un estado o tratamiento recién creado (RF-10, RF-11). Con la spec 003 (que lo hace para el cierre de manga), la constitución §9 ya no tiene excepciones en el flujo de campo.

**Cerradas por el planificador con criterio técnico (revisables):**
8. **Crear estado y tactar son dos operaciones**; si falla el tacto el estado queda y se avisa (RF-8). Descartado: una operación única, porque requiere un caso de uso nuevo para un caso poco frecuente.
9. **Validación de nombres y códigos:** límites de 50 y 30 caracteres (de Aviss), generación del código interno, nombres únicos contra activos y desactivados (no contra anulados) con ofrecimiento de reactivar (RF-3, RF-7).
10. **Desactivar:** estados solo los creados desde la app (los iniciales de Dairy Comp no, porque la importación depende de ellos); tratamientos cualquiera (RF-4, RF-9). Reactivar solo se ofrece al repetir un nombre.
11. **Anular un estado o tratamiento recién creado:** se marca como anulado (de "no" a "sí") en vez de borrarlo, solo si no se usó, y libera su nombre y su código (RF-11). Descartado: borrar el registro, porque la constitución prohíbe borrar; descartado también reusar "desactivar", porque desactivado conserva el nombre ocupado y el usuario quiere poder recrearlo.
12. **Cambios de esquema (para el plan):**
    - Tablas nuevas: tratamientos, tratamientos de un tacto, registros de solo tratamiento (con sus tratamientos) y marcas de rechazo, con triggers de inmutabilidad y una prueba de base por tabla.
    - Estado reproductivo: una **marca de origen** ("inicial" o "creado en la app") y una **marca de anulado**; y la unicidad del nombre debe excluir a los anulados (índice parcial), hoy es sobre todos. Los límites de 50 y 30 caracteres se aplican en la pantalla y el servicio.
    - Tratamiento: marca de anulado e índice de nombre único que excluye anulados.
    - El orden de guardado compartido de la spec 003 debe incluir las tablas nuevas.
13. **Sin historial para el catálogo:** como en la spec 001, crear, desactivar y anular estados y tratamientos solo guardan fechas; el historial cubre los registros de campo (RF-16).
14. **El "último registro vigente" se amplía acá a cuatro tipos:** la spec 003 lo define para dos; esta spec lo extiende sin cambiar las reglas (RF-12). Una vaca con solo tratamiento o rechazo cuenta como procesada por su marca de pasada, no por estos registros.

## Supuestos y riesgos
- **Depende de la spec 003:** no se puede planificar ni implementar antes. Si la 003 cambia (por ejemplo, el modelo de vigencia o el orden de guardado), esta spec se ajusta.
- **Tratamientos, observaciones, solo tratamiento y rechazos en la exportación:** no se sabe si Dairy Comp tiene dónde recibirlos (el usuario menciona que el rechazo se marca como un evento fijo con observación); lo define la spec de exportación con la prueba 0.2. Riesgo de guardar datos sin salida.
- **Códigos de Dairy Comp de estados nuevos:** un estado creado en campo con un código que Dairy Comp no conoce podría ser rechazado o ignorado al reimportar. Depende de la prueba 0.2 y de la spec de exportación.
- **Rechazo equivocado no deshacible:** si ya hay registros posteriores en la vaca, no se puede deshacer; se corrige registrando otro rechazo. A revisar con el uso real.
- **Cambio de base:** tablas nuevas, marcas de origen y anulado en estados y tratamientos, y un índice de nombre distinto (constitución §3 y §7). Se define en el plan.
- **Tamaño (constitución §10, máximo 5 tareas):** debería entrar holgada: (1) entidades y migración; (2) servicios; (3) ViewModels; (4) pantallas del Detalle, solo tratamiento y rechazo; (5) pantallas de estados y tratamientos nuevos, desactivar y deshacer lo creado, y cierre.
- **Pantalla sin verificar en navegador:** la interfaz se prueba en el emulador; la lógica y los ViewModels, con tests.

## Preguntas abiertas
Ninguna pendiente.
