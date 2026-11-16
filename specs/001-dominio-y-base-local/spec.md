# 001 — Dominio y base local

Estado: aprobada
Fecha: 2026-11-16

## Contexto
Todo lo que hace Aviss (importar, tactar, exportar) necesita un lugar donde guardar tambos, rodeos, vacas, tactos y su historial, y que ese lugar no pierda nada aunque se apague la tablet en medio de un tacto. Esta spec define **qué datos existen y qué garantías da la base local**; no tiene pantallas. La usan, indirectamente, todos los veterinarios que trabajan con la tablet, y es la base de las specs de importación, campo y exportación.

## Alcance
- Incluye:
  - Los conceptos del negocio: Establecimiento (tambo), Rodeo (una importación), Animal (vaca), Estado reproductivo (catálogo), Tacto, Historial (auditoría) y Usuario (veterinario).
  - Catálogo de estados reproductivos configurable, con su código de Dairy Comp, y valores iniciales.
  - Reglas que la base garantiza por sí misma: unicidad de caravana por rodeo, relaciones obligatorias, inmutabilidad de tactos e historial, no borrar nada.
  - Registro atómico de un tacto (animal + tacto + historial, o nada), alta de animal en campo y anulaciones.
  - Creación y actualización de la base al iniciar la app, con copia de seguridad previa; backups rotativos.
  - Identificación de fechas en UTC y de cada tablet (dispositivo).
- No incluye: importar Excel, pantallas de trabajo, lector RFID, exportación, log de lecturas RFID, registro de exportaciones, selección de veterinario en pantalla, administración de estados en pantalla, restaurar una copia a pedido del usuario, sincronización en la nube, historial de cambios de establecimientos, rodeos, usuarios y catálogo (estos solo guardan fecha de creación y de última modificación).

## Definiciones
- **Tacto vigente:** tacto normal (no de anulación) que no fue anulado.
- **Último tacto vigente de un animal:** el tacto vigente con fecha y hora más reciente; si dos tienen la misma, gana el que se guardó después. Los tactos de anulación nunca cuentan como "último".
- **Tacto de anulación:** el tacto nuevo que se guarda al anular otro (RF-13).
- **Baja:** marcar un registro como inactivo; el registro y sus datos siguen existiendo.

## Requisitos funcionales

### Establecimientos, rodeos, usuarios
- **RF-1** — El sistema deberá permitir guardar varios establecimientos en la misma tablet, cada uno con nombre (obligatorio) y notas (opcionales).
- **RF-2** — El sistema deberá permitir que un establecimiento tenga varios rodeos. Cada rodeo guarda, todos obligatorios: establecimiento, nombre, fecha de importación, nombre del archivo de origen, huella SHA-256 del archivo, el archivo original completo, el perfil de Excel usado y su estado (Abierto o Exportado). La cantidad de animales del rodeo no se guarda: se calcula contando sus animales no anulados (incluye las altas en campo). El estado es solo informativo: un rodeo Exportado admite nuevos tactos y altas igual que uno Abierto; solo la función de exportación lo pasa a Exportado y no vuelve a Abierto solo. El sistema permite guardar dos rodeos con la misma huella (la advertencia por archivo repetido es de la spec de importación).
- **RF-7** — El sistema deberá permitir guardar usuarios (veterinarios) con nombre (obligatorio) y matrícula (opcional, hasta 30 caracteres, sin contraseña), y dejar constancia del usuario que hizo cada tacto, cada anulación y cada alta o edición de un animal.
- **RF-7b** — Cuando se crea la base por primera vez, el sistema deberá crear un usuario "Sin asignar", que se usa donde todavía no hay un veterinario elegido (por ejemplo, hasta que exista la selección de veterinario). Ese usuario no se puede dar de baja mientras no haya otro usuario activo.
- **RF-24** — El sistema no deberá permitir borrar establecimientos, rodeos, usuarios ni estados reproductivos: solo darlos de baja (campo Activo). Una baja no altera tactos, historial ni animales ya guardados. Un establecimiento o rodeo dado de baja no admite nuevos rodeos, altas ni tactos; un usuario o estado dado de baja no puede elegirse para registros nuevos, pero sigue figurando en los anteriores.

### Animales
- **RF-3** — El sistema deberá guardar de cada animal: rodeo, número de caravana, identificador electrónico (opcional), estado reproductivo actual (puede estar vacío: animal sin estado), fecha del último tacto (vacía hasta el primer tacto registrado en Aviss; no se completa con la fecha de importación), observaciones (opcionales), origen (Importado o Alta en campo), marca de anulado (RF-25) y las columnas originales del Excel sin modificar. Las columnas originales y el origen no cambian nunca.
- **RF-4** — El sistema deberá garantizar que no existan dos animales con el mismo número de caravana dentro de un mismo rodeo, aunque se intente guardar por fuera de las reglas de la aplicación. El mismo número puede repetirse en rodeos distintos. Reglas del número de caravana: texto obligatorio, de hasta 20 caracteres, sin espacios al principio ni al final; se guarda tal como viene (los ceros a la izquierda se conservan: "0123" y "123" son caravanas distintas); la comparación para detectar repetidos no distingue mayúsculas de minúsculas (incluida la Ñ). Un animal anulado (RF-25) no ocupa su número.
- **RF-4b** — Si se informa identificador electrónico, entonces el sistema deberá guardarlo sin espacios (hasta 32 caracteres) y rechazar que dos animales no anulados del mismo rodeo tengan el mismo.
- **RF-15** — El sistema no deberá permitir borrar animales: sus datos permanecen mientras exista su rodeo, y los rodeos no se borran (RF-24).
- **RF-25** — Cuando se da de alta un animal en campo (vaca que no figura en el rodeo), el sistema deberá guardarlo con origen "Alta en campo", con solo estos datos mínimos: rodeo, número de caravana y usuario; identificador electrónico y observaciones son opcionales; el estado queda vacío y la fecha de último tacto vacía. Rechaza una caravana repetida en el rodeo (RF-4) y registra el alta en el historial. El alta en campo se anula marcando el animal como anulado, solo si no tiene tactos vigentes; el animal anulado se conserva, libera su número de caravana, ya no cuenta en el rodeo y no admite nuevos tactos ni ediciones. La anulación queda en el historial. Un animal importado no puede anularse.
- **RF-26** — El sistema deberá permitir editar solo las observaciones, el identificador electrónico y, en las altas en campo, el número de caravana de un animal no anulado; en un animal importado el número de caravana no se puede cambiar. Cada edición registra el usuario y actualiza la fecha de modificación del animal.

### Catálogo de estados
- **RF-5** — El sistema deberá mantener un catálogo de estados reproductivos, cada uno con: código interno (estable, en mayúsculas, sin acentos ni espacios, único), nombre para mostrar (único sin distinguir mayúsculas), código de Dairy Comp (texto exacto que se usa en el Excel; único entre los estados activos) y marca Activo. Deberá poder ampliarse o modificarse mediante una operación del sistema (sin pantalla en esta spec) y sin cambiar el programa. Renombrar un estado no rompe el historial ni los tactos, porque estos lo referencian por identificador y no por nombre.
- **RF-6** — Cuando se crea la base por primera vez, el sistema deberá cargar en el catálogo los estados vistos en Dairy Comp: PREÑADA, INSEMIN, SECA, VACIA, FRESCA y NOINSEM, con código de Dairy Comp igual al nombre (supuesto a validar en la prueba 0.2). Cuando una versión futura agregue estados iniciales, el sistema deberá agregar solo los que falten, sin modificar los existentes ni reactivar los que el usuario desactivó.

### Tactos, historial y anulaciones
- **RF-8** — Cuando se registra un tacto, el sistema deberá, en una única operación indivisible, actualizar el estado y la fecha del último tacto del animal, guardar el tacto y guardar su registro de historial (exactamente uno por tacto); si alguna parte falla, no deberá quedar ninguna. El tacto guarda: animal, fecha y hora, estado anterior (vacío si el animal no tenía estado), estado nuevo, días de preñez opcionales, observaciones opcionales, usuario, dispositivo y tipo (Normal o Anulación).
- **RF-8b** — El sistema deberá validar el tacto antes de guardarlo y rechazarlo sin cambiar nada si: el animal está anulado o su rodeo, establecimiento o el estado nuevo están dados de baja; el estado anterior que se informe no coincide con el estado actual del animal (si no se informa, se toma el actual); la fecha y hora es posterior a la hora actual del dispositivo más 5 minutos; la fecha y hora es anterior a la del último tacto vigente del animal (indica reloj mal puesto; el mensaje debe sugerir revisar la hora); o los días de preñez, si se informan, están fuera de 0 a 300. Un tacto sin cambio de estado (mismo estado anterior y nuevo) es válido. La hora del tacto la toma el sistema de su reloj (el mismo que se usa en los tests).
- **RF-9** — El sistema deberá confirmar cada tacto en el almacenamiento permanente en el momento de registrarlo, sin ninguna acción de "guardar".
- **RF-10** — El sistema deberá guardar el historial solo por agregado: cada registro indica qué entidad cambió, qué campo, valor anterior, valor nuevo (los estados se guardan por identificador), fecha y hora, usuario y tipo de acción. Se genera historial en: alta de animal en campo (un registro), edición de observaciones, identificador electrónico o caravana (un registro por campo cambiado), cada tacto (RF-8), cada anulación de tacto (RF-13) y cada anulación de alta (RF-25). La importación no genera un registro por animal (lo define su spec).
- **RF-11** — Si se intenta modificar o borrar un registro de historial, entonces el sistema deberá rechazar la operación y dejar el registro intacto, aunque el intento no venga de la aplicación.
- **RF-12** — Si se intenta modificar o borrar un tacto ya guardado, entonces el sistema deberá rechazar la operación, aunque el intento no venga de la aplicación. La única modificación permitida es pasar la marca "Anulado" de un tacto de "no" a "sí" (junto con su fecha de modificación) como parte de una anulación (RF-13); nunca se puede volver a "no", ni cambiar ningún otro dato del tacto.
- **RF-13** — Cuando se anula un tacto, el sistema deberá, en una única operación indivisible: guardar un tacto de anulación que referencia al anulado, devolver el animal al estado anterior del tacto anulado y su fecha de último tacto a la del último tacto vigente anterior (o vacía si no hay), registrar la anulación en el historial y marcar el tacto original como anulado. El tacto de anulación lleva: estado anterior = estado actual del animal, estado nuevo = estado anterior del tacto anulado, días de preñez vacíos, fecha y hora actuales, el usuario que anula y el dispositivo. Cualquier usuario puede anular el último tacto vigente de un animal, sin importar quién lo hizo.
- **RF-14** — Solo se puede anular el último tacto vigente del animal. Si se intenta anular un tacto ya anulado, un tacto de anulación, o un tacto que no es el último vigente de ese animal, o si el animal está anulado, entonces el sistema deberá rechazar la anulación sin cambiar nada. Un tacto de anulación no puede anularse ("deshacer el deshacer"): para volver a registrar lo mismo se guarda un tacto nuevo.

### Integridad, fechas e identificadores
- **RF-16** — El sistema deberá garantizar, también a nivel de la base y en todas las conexiones, que todo animal pertenezca a un rodeo existente, todo rodeo a un establecimiento existente, y todo tacto a un animal, un usuario y estados existentes, y que los tactos de un animal solo referencien a ese animal. Borrar un registro del que otros dependen no arrastra a los dependientes: se rechaza. Si al abrir la base no se puede exigir esta verificación, la app no abre.
- **RF-17** — El sistema deberá guardar todas las fechas y horas en UTC y toda entidad deberá registrar cuándo fue creada (y cuándo fue modificada, si admite ediciones; esto incluye Tacto por la marca de anulado y Animal). Verificable: con un reloj de prueba en hora conocida, el valor leído de vuelta es el mismo instante en UTC.
- **RF-18** — El sistema deberá identificar a cada registro con un identificador único global (no un número correlativo), de modo que registros creados en tablets distintas no se pisen. Para ordenar tactos con igual fecha y hora, además se guarda internamente su orden de inserción.
- **RF-23** — El sistema deberá registrar en cada tacto qué tablet lo hizo, mediante un identificador de dispositivo propio de la instalación, generado la primera vez que la app arranca y conservado. No depende del contenido de la base: restaurar o recrear la base no lo cambia.

### Arranque y copias de seguridad
- **RF-19** — Cuando la app inicia y la estructura de la base es más vieja que la de la app, el sistema deberá hacer una copia de seguridad completa y verificada y recién después actualizar; si la copia no se puede hacer, no actualiza.
- **RF-20** — Cuando la app inicia por primera vez (no hay base), el sistema deberá crearla con la estructura completa, los estados iniciales y el usuario "Sin asignar", sin pedir copia previa, y de forma que un corte de energía durante la creación no deje una base a medias: al reabrir, si quedó una creación incompleta, se descarta y se vuelve a crear.
- **RF-21** — El sistema deberá poder generar una copia de seguridad a pedido de otras funciones (por ejemplo, antes de importar) y conservar solo las últimas 10, ordenadas por fecha y hora de creación, borrando las más antiguas, salvo que nunca se borra la copia previa a actualización más reciente. Una copia vieja solo se borra cuando la nueva ya está completa y verificada. Antes de copiar, el sistema comprueba que haya espacio libre suficiente (tamaño de la base más un margen, considerando que incluye los archivos .xls originales); si no hay, no copia y avisa. Las copias se guardan en el almacenamiento privado de la app, con nombre que indica fecha, hora UTC y motivo (previa a actualización o a pedido).
- **RF-22** — Si la actualización de estructura falla, entonces el sistema deberá volver a la base tal como estaba antes de actualizar (restaurando la copia previa si la base quedó distinta) y avisar con un mensaje claro, sin abrir la app sobre una base a medias. Si también falla la restauración, la app no abre, no borra ni sobrescribe la copia ni la base dañada, y avisa.
- **RF-27** — Si al iniciar la base es de una versión más nueva que la app, entonces el sistema no deberá abrirla ni modificarla y avisará que hay que actualizar la app. Si la base está dañada, no la abrirá ni la sobrescribirá, la conservará aparte y avisará.
- **RF-28** — El sistema deberá dejar el resultado del arranque (correcto, o el tipo de error y su mensaje en español) disponible para que la pantalla lo muestre. Los mensajes y registros de error de arranque, migración o copia no incluyen datos de animales, tambos ni veterinarios.

## Casos límite
- **Sin conexión:** todo funciona sin Internet; nada de esta spec usa la red.
- **Corte de energía:** a mitad de un tacto o anulación, al reabrir está completo o no existe (RF-8, RF-13); a mitad de la creación inicial, se recrea (RF-20); a mitad de una actualización, se vuelve a la base previa (RF-22); a mitad de una copia, la copia incompleta no cuenta ni borra otras (RF-21).
- **Caravana repetida:** se rechaza en el mismo rodeo, incluso con otra capitalización (RF-4); en rodeos o tambos distintos es válido.
- **Vaca que no está en el rodeo:** alta en campo con datos mínimos y la misma regla de unicidad (RF-25); si fue un error, se anula el alta (RF-25).
- **Animal sin estado:** válido; su primer tacto tiene estado anterior vacío (RF-3, RF-8).
- **Lecturas repetidas / tacto duplicado:** guardar dos tactos seguidos del mismo animal es válido y ambos quedan en el historial; la corrección se hace anulando (RF-13). El filtrado de lecturas repetidas del bastón es de otra spec.
- **Anular un tacto que no es el último, ya anulado, o de anulación:** se rechaza (RF-14).
- **Reloj de la tablet mal puesto:** fecha futura o anterior al último tacto vigente se rechaza con aviso (RF-8b).
- **Rodeo ya exportado:** se puede seguir tactando; una nueva exportación incluirá esos tactos (RF-2).
- **Mismo archivo importado dos veces:** la base lo permite, son rodeos distintos (RF-2).
- **Animal sin tactos en Aviss:** su fecha de último tacto está vacía; si se anula su único tacto, vuelve a quedar vacía (RF-3, RF-13).
- **Varios veterinarios en la misma tablet:** cada tacto guarda su usuario; cambiar de veterinario no altera registros anteriores; cualquiera puede anular el último tacto vigente (RF-13).
- **Varias tablets:** el "último tacto" y la anulación valen dentro de cada tablet; no hay sincronización entre tablets en esta etapa.
- **Varios tambos y rodeos:** un tambo puede tener varios rodeos; un mismo número de caravana puede existir en cada uno.
- **Estado desactivado o renombrado:** no se puede borrar; desactivarlo impide elegirlo en tactos nuevos pero no afecta los pasados (RF-5, RF-24).
- **Estados desconocidos de Dairy Comp:** el catálogo puede ampliarse (RF-5) sin tocar el programa; qué hacer con un estado desconocido al importar lo resuelve la spec de importación.
- **Copias sin espacio o fallidas:** si no se puede hacer la copia previa, no se actualiza y se avisa (RF-19, RF-21).
- **Base más nueva que la app, o dañada:** no se abre ni se toca (RF-27).
- **Excel mal formado:** fuera de alcance; la base solo guarda datos ya validados y las columnas originales intactas (RF-3).
- **Volumen:** con 20.000 animales y 100.000 tactos en la base, buscar un animal por caravana dentro de su rodeo tarda menos de 100 ms y registrar un tacto menos de 300 ms (medido en test, no en la tablet).

## Criterios de aceptación
- [ ] Se crean dos establecimientos con rodeos y animales; el mismo número de caravana puede existir en dos rodeos pero no dos veces en el mismo, ni variando mayúsculas o espacios de los extremos; "0123" y "123" conviven (RF-1 a RF-4).
- [ ] Un rodeo sin alguno de sus datos obligatorios se rechaza; la cantidad de animales sale de contar (RF-2).
- [ ] El catálogo arranca con los seis estados y el usuario "Sin asignar"; se agrega un estado sin tocar código; una carga posterior de iniciales no pisa un estado modificado ni reactiva uno desactivado (RF-5, RF-6, RF-7b).
- [ ] Registrar un tacto actualiza animal, tacto e historial juntos (un registro de historial); si se fuerza un fallo a mitad, no queda nada (RF-8, RF-9, RF-10).
- [ ] Se rechazan tactos con: estado anterior distinto del actual, fecha futura, fecha anterior al último vigente, días de preñez fuera de rango, estado nuevo desactivado, animal anulado; un tacto sin cambio de estado se acepta (RF-8b).
- [ ] Un intento directo de modificar o borrar un registro de historial o un tacto es rechazado por la base; el único cambio aceptado es la marca de anulado de "no" a "sí", y volverla a "no" se rechaza (RF-11, RF-12).
- [ ] Anular el último tacto vigente deja un tacto de anulación con los datos de RF-13, el animal en el estado y fecha anteriores (o fecha vacía si era el único), el original marcado y la anulación en el historial, todo junto (RF-13).
- [ ] Se rechaza anular: un tacto ya anulado, uno que no es el último vigente, un tacto de anulación, y cualquier tacto de un animal anulado; un tacto de anulación no cuenta como "último" (RF-14).
- [ ] Alta en campo con datos mínimos: crea el animal, registra historial y rechaza caravana repetida; su anulación libera la caravana y se rechaza si tiene tactos vigentes; un animal importado no puede anularse (RF-25).
- [ ] Solo observaciones, identificador electrónico y (en altas en campo) caravana son editables, con historial por campo; las columnas originales no cambian (RF-3, RF-26).
- [ ] No se puede borrar animales, rodeos, establecimientos, usuarios ni estados; las bajas no alteran lo ya guardado; no se crean tactos con rodeo, usuario o estado dado de baja (RF-15, RF-24).
- [ ] No se puede crear un animal con rodeo inexistente ni un tacto con referencias inexistentes o de otro animal; la verificación de relaciones está activa en cada conexión (RF-16).
- [ ] Con un reloj de prueba, las fechas se leen de vuelta como el mismo instante UTC; cada registro tiene identificador único global y fecha de creación; los editables tienen fecha de modificación (RF-17, RF-18).
- [ ] Todo tacto guarda el identificador del dispositivo, y este no cambia entre arranques ni al recrear o restaurar la base (RF-23).
- [ ] Con una base de estructura vieja, al iniciar se genera copia previa verificada y luego se actualiza; si falla la actualización, la base queda como estaba; si falla también la restauración, la app no abre y conserva todo (RF-19, RF-22).
- [ ] Creación inicial interrumpida: al reabrir se recrea sin base a medias; base más nueva que la app o dañada: no se abre ni se modifica (RF-20, RF-27).
- [ ] Solo se conservan las últimas 10 copias; al generar la 11.ª se borra la más antigua, nunca la previa a actualización más reciente ni antes de completar la nueva; sin espacio no se copia (RF-21).
- [ ] El resultado del arranque queda disponible con mensaje en español y sin datos de animales ni tambos (RF-28).
- [ ] Un rodeo Exportado acepta nuevos tactos y altas (RF-2).
- [ ] Un animal importado sin tactos tiene la fecha del último tacto vacía; tras el primer tacto tiene la fecha de ese tacto (RF-3).
- [ ] Con el volumen de los casos límite, la búsqueda y el registro de un tacto cumplen los tiempos indicados.
- [ ] `dotnet build` y `dotnet test` en verde.

## Decisiones
1. **Anulación:** solo se puede anular el último tacto vigente de la vaca; no se puede anular una anulación (RF-13, RF-14).
2. **Backups:** se conservan 10 copias rotativas, protegiendo la previa a actualización más reciente (RF-21).
3. **Códigos de Dairy Comp:** el código es igual al nombre del Excel (PREÑADA, INSEMIN, …) hasta que la prueba 0.2 indique otra cosa (RF-6, ver Supuestos y riesgos).
4. **Rodeo Exportado:** se puede seguir tactando; el estado solo informa (RF-2).
5. **Fecha del último tacto:** queda vacía hasta el primer tacto hecho en Aviss (RF-3).
6. **Sin borrados:** todo se da de baja, nada se borra; las claves no arrastran dependientes (RF-15, RF-16, RF-24).
7. **Orden de tactos:** por fecha y hora, desempate por orden de inserción; se rechaza un tacto anterior al último vigente por reloj mal puesto (RF-8b).
8. **Usuario por defecto:** "Sin asignar" hasta que exista selección de veterinario (RF-7b).

## Supuestos y riesgos
- **Códigos de Dairy Comp = nombre del Excel:** es un supuesto sin validar. Si la prueba 0.2 muestra que Dairy Comp espera otros códigos, se corrigen en el catálogo (RF-5) sin tocar el programa, pero hay que revisar los datos ya cargados. **Nota para el implementador:** anotar este supuesto en la sección Riesgos de `MEMORY.md` (el planificador solo escribe en `specs/`).
- **Caravana tal cual (ceros a la izquierda, sin normalizar):** si el Excel real de Dairy Comp trae ceros inconsistentes, lo resuelve la spec de importación; también anotarlo en `MEMORY.md`.
- **Identificador electrónico único por rodeo:** si las muestras reales traen repetidos, se revisa en la spec de importación.
- **Rechazo por fecha anterior al último tacto:** puede frenar el trabajo con un reloj atrasado; se prefiere por consistencia y el mensaje indica revisar la hora.
- **Último tacto por tablet:** sin sincronización, dos tablets pueden tactar el mismo animal; se resolverá cuando exista sincronización.
- **Tamaño de copias:** los .xls originales dentro de la base agrandan cada copia; 10 copias pueden ocupar mucho espacio en la tablet.

## Preguntas abiertas
Ninguna pendiente.
