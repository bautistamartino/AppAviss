# 008 — Lector real del bastón Tru-Test XRS2

Estado: aprobada
Fecha: 2026-11-24

## Contexto
Hasta hoy la app lee caravanas con un lector simulado (`FakeRfidReader`, solo en desarrollo); en la versión que se distribuye no entra nada y todo es búsqueda a mano. Esta spec conecta el **bastón real**, para que el veterinario apunte a la caravana y la ficha se abra sola (meta de un toque por vaca, constitución §9).

**Lo que mostró la prueba del 2026-11-24 con el spike** (tablet SM-X110, Android 16, bastón "XRS2i 6769", dirección `33:DC:72:9C:40:81`, tipo Dual, UUID SPP `00001101-0000-1000-8000-00805f9b34fb`; además ya había dos XRS2 emparejados, "XRS2 1971" y "XRS2 2949"):
- **Transporte:** Bluetooth clásico SPP. La lectura de un botón llegó como texto ASCII `982 000458467499` seguido de CRLF (un espacio después de `982`, 15 dígitos, una línea por lectura). Es el EID que la spec 010 ya guarda al importar.
- **Conexión poco confiable con el socket "seguro":** falló dos veces por timeout de unos 11 s en la sesión 1, conectó en 2 s en la sesión 2 y falló tras 60 s en la sesión 3. El socket **inseguro** conectó al primer intento en 1,4 s.
- **Cortes:** se vio "socket closed" a los 18 s y a los 3 min. El responsable aclaró que **el bastón no corta solo por estar en reposo** (aguanta conectado mientras le dure la batería): esos cortes fueron casi seguro porque lo apagó o lo alejó para probar. Cuando el bastón se apaga, la conexión hay que volver a establecerla: la tablet no la recupera sola.
- Solo se leyó un botón (el del responsable, que no era del rodeo importado). **No se probó** leer varias seguidas, la misma varias veces, un EID de una vaca importada, ni qué pasa con lo leído mientras el bastón estaba apagado.

Entonces los motivos realistas de perder la conexión son: bastón apagado, batería agotada, salir del alcance, o que la app pierda el socket al cambiar de pantalla o apagarse la pantalla. La reconexión automática hace falta **para que, al prender el bastón, la app lo encuentre sola** sin que el veterinario entre a ningún menú, no para sostener una conexión que se cae en reposo.

## Alcance
- Incluye:
  - Un lector real del XRS2 por **Bluetooth clásico SPP**, que entrega al flujo de trabajo el EID leído.
  - Elegir y recordar cuál de los bastones emparejados se usa (hay varios XRS2 en la tablet), por tablet.
  - Permisos de Bluetooth en ejecución (Android 12 o más) y qué se ve cuando faltan o el Bluetooth está apagado.
  - Estrategia de conexión (socket inseguro primero, tiempos cortos, reintentos con espera creciente), reconexión en segundo plano y un botón grande para reconectar a mano.
  - Estado de la conexión siempre visible en la pantalla de trabajo.
  - Qué pasa con el bastón apagado o sin batería a mitad del tacto, y con la pantalla apagada o la app en segundo plano.
  - Interpretar lo que manda el bastón: una línea por lectura, EID de 15 dígitos con o sin espacios; ignorar basura; ignorar lecturas repetidas del mismo botón en pocos segundos.
  - Que la lectura entre al mismo camino que la búsqueda a mano (cola, ficha abierta de la spec 009, CVET 0, ya procesada, no encontrada).
- No incluye (no probado o fuera de lo pedido):
  - **BLE y bastón que "tipea" como teclado:** el spike mostró que el XRS2 anda por SPP. Si algún bastón futuro no lo hiciera, es otra spec (el lector queda detrás de `IRfidReader`).
  - **Cambiar el modo de salida del bastón** (formato, terminadores o la configuración propia del XRS2): se usa como viene.
  - Leer con la app en segundo plano o con la pantalla apagada (servicio en primer plano): ver pregunta 2.
  - Cargar un archivo aparte de pares ID-EID, o completar el EID de un rodeo ya importado (spec 010).
  - Números cortos en vez de EID (por ejemplo, el bastón con un número renombrado): no se vio nunca; ver Supuestos y riesgos.
  - La pantalla de diagnóstico del spike: sigue solo en la compilación de desarrollo y no se toca su comportamiento.
  - Cambios en la base de datos, el historial o la exportación (no hay migración).

## Definiciones
(Las de las specs 003, 009 y 010 valen acá.)
- **Bastón elegido:** el dispositivo Bluetooth emparejado que la tablet recuerda para leer. Se recuerda su nombre y su dirección; no identifica a ningún veterinario ni registra qué tablet es (spec 006).
- **Línea:** lo recibido hasta un CR, un LF o un CRLF (un CRLF cuenta una sola vez; las líneas vacías no cuentan).
- **EID leído:** una línea que, quitados los espacios, son exactamente 15 dígitos. Se entrega a la app **sin espacios**.
- **Basura:** cualquier otra línea (letras, otra cantidad de dígitos, bytes que no son texto).
- **Estados visibles:** **Conectado**; **Buscando el bastón** (hay un intento en curso); **Sin bastón** (no hay conexión), con el motivo y qué tocar: sin bastón elegido, sin permiso, Bluetooth apagado, tablet sin Bluetooth, bastón que ya no figura emparejado, o bastón que no responde (apagado, sin batería o lejos).

## Requisitos funcionales

### Elegir y recordar el bastón
- **RF-1** — Cuando el veterinario entra a trabajar y la tablet no recuerda ningún bastón, el sistema deberá mostrar el estado **Sin bastón** con el texto "Elegí el bastón" y, al tocarlo, una pantalla con la lista de dispositivos Bluetooth emparejados (los que se llaman XRS primero), cada uno con un botón grande; además deberá ofrecer abrir los ajustes de Bluetooth de Android para emparejar uno nuevo. La búsqueda a mano debe seguir disponible.
- **RF-2** — Cuando el veterinario elige un bastón de la lista, el sistema deberá recordarlo en la tablet (nombre y dirección), conectarse a él al volver a la pantalla de trabajo y usarlo en los trabajos siguientes **sin volver a preguntar**. El sistema no deberá elegir un bastón solo, aunque haya uno solo que se llame XRS.
- **RF-3** — El sistema deberá permitir cambiar el bastón elegido desde la capa de configuración del engranaje (que muestra el nombre del bastón actual) y desde el cartel de **Sin bastón**.
- **RF-4** — Si el bastón recordado ya no figura entre los emparejados, entonces el sistema deberá mostrar **Sin bastón** con el texto "El bastón ya no está emparejado" y llevar a la lista para elegir otro.

### Permisos y Bluetooth
- **RF-5** — Cuando el veterinario entra a trabajar con un bastón elegido, o abre la lista de bastones, y falta el permiso de Bluetooth para conectarse a dispositivos cercanos (Android 12 o más), el sistema deberá pedirlo en ese momento (no al abrir la app) y no deberá pedir ubicación. Si se deniega, deberá mostrar **Sin bastón** con el texto "Falta el permiso de Bluetooth"; al tocarlo deberá volver a pedirlo y, si Android ya no muestra el diálogo, deberá abrir los ajustes de la app e indicar "Permisos > Dispositivos cercanos". La app sigue usable con búsqueda a mano.
- **RF-6** — Si el Bluetooth de la tablet está apagado, entonces el sistema deberá mostrar **Sin bastón** con el texto "Prendé el Bluetooth de la tablet" y ofrecer abrir sus ajustes; y cuando se prenda, deberá conectarse solo en el próximo reintento (RF-10) sin salir de la pantalla de trabajo. Si la tablet no tiene Bluetooth, deberá decirlo y no reintentar.

### Conexión
- **RF-7** — Cuando hay un bastón elegido y permisos, el sistema deberá conectarse por Bluetooth clásico (puerto serie SPP, UUID `00001101-0000-1000-8000-00805f9b34fb`) en segundo plano, sin bloquear ni congelar la pantalla, y mostrar **Buscando el bastón** mientras intenta.
- **RF-8** — En cada ciclo de conexión el sistema deberá probar dos métodos de socket, primero el que funcionó la última vez (la primera vez, el **inseguro**) y después el otro, con un **tiempo máximo de 8 segundos por intento**: pasado ese tiempo deberá abandonar el intento (no esperar los 11 o 60 segundos de Android) y seguir con el siguiente.
- **RF-9** — Cuando se logra conectar, el sistema deberá pasar a **Conectado** y quedar listo para leer sin otro toque.
- **RF-10** — **Mientras la pantalla de trabajo esté abierta y no haya conexión** (y sea posible intentar: hay bastón elegido, permiso y Bluetooth), el sistema deberá reintentar en segundo plano, sin que el veterinario toque nada, con espera creciente entre ciclos (1, 2, 4, 8 y 15 segundos; 15 es el tope y se mantiene), para que al prender el bastón se conecte solo. La espera vuelve a 1 segundo al conectar.
- **RF-11** — El sistema deberá mostrar **Buscando el bastón** durante el primer ciclo de conexión (al entrar a trabajar o al perder la conexión); si ese ciclo no logra conectar, deberá pasar a **Sin bastón** con el texto "El bastón no responde: prendelo o acercalo" y **seguir reintentando en segundo plano** (RF-10) sin cambiar el estado visible en cada intento, para que el cartel no parpadee.
- **RF-12** — El sistema deberá ofrecer, mientras no esté Conectado, un **botón grande (al menos 72 dp de alto, ancho completo)** con el estado y la acción ("Sin bastón · Tocá para reconectar"). Al tocarlo deberá reintentar **de inmediato**, saltando la espera, y mostrar **Buscando el bastón**. Si el motivo es otro (sin elegir, permiso, Bluetooth apagado, ya no emparejado), el toque deberá hacer lo que dice su texto (RF-1, RF-4, RF-5, RF-6).
- **RF-13** — El estado de la conexión deberá estar **siempre visible** en la pantalla de trabajo, también con la cabecera contraída, con texto y color (no solo color): Conectado (verde, chico), Buscando el bastón (ámbar), Sin bastón (rojo, con el botón de RF-12).
- **RF-14** — Cuando se pierde una conexión que estaba establecida (el bastón se apagó, se alejó o se quedó sin batería), el sistema deberá pasar a **Buscando el bastón**, vibrar una vez para que el veterinario se entere sin mirar, y empezar a reintentar (RF-10). No deberá abrir ningún cuadro ni mensaje que haya que cerrar.

### Bastón apagado o sin batería a mitad del tacto
- **RF-15** — Mientras no hay bastón (apagado, batería agotada, fuera de alcance), el sistema deberá seguir funcionando con la búsqueda a mano y con todo lo demás (ficha abierta, eventos, observación, **Entrar**, En espera, Deshacer, últimos movimientos, terminar el tacto): no deberá bloquear nada, **no deberá cerrar ni modificar la ficha abierta ni perder el evento o la observación ya cargados**, y no deberá guardar nada por el corte. Cuando el bastón se prenda, deberá reconectarse solo (RF-10) y las lecturas nuevas entrarán como siempre.

### Ciclo de vida
- **RF-16** — Cuando el veterinario sale de la pantalla de trabajo (a movimientos, a la lista de bastones, al inicio) o la app pasa a segundo plano o se apaga la pantalla, el sistema deberá cortar la conexión y dejar de reintentar; no deberá leer en segundo plano. Cuando vuelve a la pantalla de trabajo o a la app, deberá intentar conectar **de inmediato** (sin esperar el tope de la espera). Las lecturas recibidas y todavía no procesadas al cortar de esta forma se descartan.
- **RF-17** — Mientras la pantalla de trabajo está abierta con la pantalla encendida, el sistema no deberá cortar la conexión por su cuenta.

### Interpretar lo que manda el bastón
- **RF-18** — Cuando llega una línea que es un **EID leído** (por ejemplo `982 000458467499` o `982000458467499`, con CRLF, CR o LF), el sistema deberá entregar a la pantalla de trabajo el código de 15 dígitos **sin espacios** como una lectura.
- **RF-19** — El sistema deberá armar la línea aunque el bastón la mande partida en varios paquetes (incluso un CR en un paquete y su LF en el siguiente) y aunque lleguen varias líneas en un mismo paquete.
- **RF-20** — Si una línea es **basura**, entonces el sistema deberá ignorarla: no deberá entregar nada, no deberá guardar nada, no deberá mostrar ningún mensaje ni hacer sonar nada, y la lectura siguiente deberá funcionar normal. Tampoco deberá crecer sin límite en memoria si llegan bytes sin terminador.
- **RF-21** — Cuando el mismo EID vuelve a llegar dentro de los **3 segundos** de la lectura anterior de ese mismo EID (el bastón repite o el ayudante lo pasó dos veces seguidas), el sistema deberá ignorar la repetida y entregar una sola. Pasados los 3 segundos, es una lectura nueva y sigue el camino de siempre (ya procesada, aviso de la spec 009). Dos EID distintos seguidos nunca se filtran.
- **RF-22** — El sistema deberá entregar cada línea completa recibida **una sola vez y en el orden en que llegó**. Al reconectar no deberá reenviar lecturas ya entregadas; una línea que quedó a medias cuando se perdió la conexión se descarta (no se puede completar). Lo que el bastón guarde en su memoria mientras está desconectado no es parte de esta spec (ver riesgos).

### Camino de la lectura
- **RF-23** — El sistema deberá tratar la lectura del bastón **exactamente igual que la búsqueda a mano** con ese código (spec 003 RF-19 y RF-23, spec 009, spec 010 RF-13): cola de lecturas (una por vez, en orden, solo la última con un cuadro abierto), marca automática, ficha, vaca con CVET 0 (se marca y suena la chicharra), vaca ya procesada ("Animal repetido" o "Ya pasó"), vaca en espera, aviso de tres opciones con una ficha abierta, y "Caravana no encontrada" con el código cargado y "Buscar a mano" cuando el EID no es de ningún animal del rodeo (incluida una vaca sin EID: se la busca por su número).
- **RF-24** — Si el bastón entrega algo que el flujo rechaza (reloj mal puesto, rodeo sin abrir), entonces el sistema deberá mostrar el mensaje que ya existe para esa lectura, sin cambios.

### Transversales
- **RF-25** — El sistema deberá usar el lector real tanto en la compilación de desarrollo como en la que se distribuye. El **simulador de lecturas** y la pantalla de **Diagnóstico del bastón** deberán seguir existiendo **solo** en la compilación de desarrollo; el simulador deberá entregar sus códigos por el mismo camino del lector (mismo filtro y misma cola).
- **RF-26** — El sistema no deberá usar la red, ni incluir EID, caravanas o nombres de tambos en registros de error o logs (ni en mensajes de excepción mostrados), ni registrar o mostrar qué veterinario ni qué tablet (spec 006). Lo único que se recuerda es el nombre y la dirección del bastón elegido, en las preferencias de la tablet. El manifiesto de la versión que se distribuye deberá pedir solo los permisos de Bluetooth necesarios para conectarse a un dispositivo ya emparejado (nada de ubicación, nada de búsqueda de dispositivos).
- **RF-27** — El estado, el botón de reconectar y la lista de bastones deberán estar pensados para la tablet **en vertical** (800x1340): botones de 72 dp, alto contraste, una mano, español rioplatense (constitución §9).

## Casos límite
- **Sin conexión (Internet):** todo es Bluetooth local; no hay red (RF-26).
- **Corte de energía o app cerrada:** la lectura solo marca a la vaca cuando el servicio la procesa (como siempre, transacción única); una línea recibida y todavía no procesada se pierde, y se vuelve a leer. No hay nada a medias.
- **Bastón apagado a mitad del tacto o batería agotada:** RF-14 y RF-15. La ficha abierta sigue intacta.
- **Bastón que se prende con la pantalla de trabajo abierta:** se conecta solo en pocos segundos (RF-10).
- **Bastón que se prende con la app en segundo plano o fuera de la pantalla de trabajo:** no pasa nada hasta volver (RF-16).
- **Salir del alcance y volver:** igual que apagar y prender (RF-10, RF-14).
- **Varios bastones emparejados (hay tres XRS en la tablet de prueba):** se usa solo el elegido (RF-2); los otros, aunque estén prendidos cerca, no leen en esta tablet.
- **Dos tablets cerca del mismo bastón:** un bastón SPP admite una conexión a la vez; la segunda no logra conectar y queda en **Sin bastón: no responde**. No hay forma de saber por qué; se documenta en "qué mirar".
- **Lectura repetida del mismo botón:** RF-21 (3 s) y, después, lo de la spec 003 ("ya procesada").
- **Lectura de una vaca que no está en el rodeo (otro tambo) o sin EID:** "Caravana no encontrada" (RF-23).
- **Bastón que manda algo que no es un EID (basura, mensajes de estado):** se ignora en silencio (RF-20).
- **Lecturas muy seguidas de vacas distintas:** cada una se entrega y la cola las procesa en orden (RF-22, RF-23).
- **Excel mal formado:** no aplica; un EID mal importado no se encuentra y queda la búsqueda por número (spec 010).
- **Varios veterinarios o tambos en la misma tablet:** el bastón elegido es de la tablet; la lectura se busca solo en el rodeo en trabajo; la app no registra veterinario ni tablet (spec 006).
- **Tablet sin Bluetooth o con Bluetooth apagado:** RF-6.
- **Android 12 o más:** permiso en ejecución (RF-5); Android 11 o menos: permisos "normales" sin diálogo.
- **Rotar la tablet:** la conexión sigue (la pantalla no se recrea); no hay corte (RF-17).

## Criterios de aceptación
- [ ] Sin bastón recordado, el cartel dice "Elegí el bastón", la lista muestra los emparejados con los XRS primero, elegir uno lo recuerda y al volver conecta; la próxima vez no pregunta (RF-1, RF-2).
- [ ] Desde el engranaje se ve el bastón actual y se puede cambiar (RF-3); con el bastón desemparejado el cartel lo dice (RF-4).
- [ ] El permiso de Bluetooth se pide al entrar a trabajar (no al abrir la app); denegado deja "Falta el permiso de Bluetooth" y la búsqueda a mano anda; Bluetooth apagado dice "Prendé el Bluetooth" (RF-5, RF-6).
- [x] Un test con un enlace falso comprueba el orden de los métodos (inseguro primero y después el último que anduvo), el límite de 8 s por intento, las esperas 1, 2, 4, 8, 15 y 15 s, el reinicio de la espera al conectar y que el reintento manual salta la espera (RF-7, RF-8, RF-10, RF-12).
- [x] Un test de estados comprueba Buscando, Conectado, Sin bastón con cada motivo y que el cartel no parpadea entre reintentos (RF-9, RF-11, RF-13).
- [ ] Con la conexión perdida: vibra una vez, reintenta solo, y la ficha abierta con su evento y observación queda intacta; se puede buscar a mano y guardar con **Entrar** (RF-14, RF-15).
- [ ] Salir de la pantalla o pasar a segundo plano corta la conexión y volver reconecta de inmediato; lo pendiente se descarta (RF-16, RF-17).
- [x] Con tramas inventadas (EID de prueba con prefijo `999`): EID con y sin espacios, CRLF, CR y LF; partido en paquetes; varias líneas juntas; basura ignorada sin efecto; repetida a menos de 3 s se ignora y a más de 3 s pasa; cada línea entra una sola vez y en orden, también a través de una reconexión (RF-18 a RF-22).
- [x] La lectura del bastón y la búsqueda a mano dan el mismo resultado en todos los casos de la spec 003, 009 y 010; un EID desconocido da "Caravana no encontrada" (RF-23, RF-24).
- [ ] La compilación que se distribuye usa el lector real, no tiene simulador ni Diagnóstico, y su manifiesto solo pide Bluetooth de conexión; la de desarrollo conserva el simulador por el mismo camino (RF-25, RF-26).
- [x] No hay migración; `dotnet ef migrations has-pending-model-changes` sin cambios.
- [ ] **A verificar en la tablet con el bastón** (no con tests): leer un botón de una vaca del rodeo con EID importado y ver su ficha; apagar y prender el bastón con la pantalla de trabajo abierta y ver que reconecta solo; el botón grande, el cartel y el permiso en 800x1340 (ver `tasks.md`, T5).
- [ ] `dotnet build` y `dotnet test` en verde.

## Preguntas abiertas
1. **Ventana de lecturas repetidas (RF-21):** 3 segundos. **Recomendación: 3 s.** Es un valor provisorio (vive en un solo lugar); no se sabe si el XRS2 repite solo. Se ajusta tras probar.
2. **Leer con la app en segundo plano o la pantalla apagada (RF-16):** **Recomendación: no.** Necesitaría un servicio en primer plano con notificación y otro permiso, y no está probado; con la pantalla encendida durante el trabajo (spec 003 RF-3) no hace falta. Solo se retoma si el veterinario pasa mucho tiempo con la pantalla apagada entre vacas.
3. **El bastón podría mandar otra cosa que el EID** (un número corto renombrado, guiones): hoy se ignora como basura. **Recomendación:** dejarlo así y confirmarlo en la tablet con varios botones; si pasara, se agrega el formato nuevo en el intérprete de líneas.
4. **Aviso al perder el bastón (RF-14):** **Recomendación: una vibración corta**, sin sonido (en la manga hay ruido y el sonido ya significa otras cosas: chicharra de CVET 0, repetido).
