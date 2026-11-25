# Prueba del bastón en casa

Instructivo del **spike de diagnóstico** (paso 5.1 de [plan.md](plan.md), previo a la spec 008). La pantalla "Diagnóstico del bastón" no es el lector final: sirve para averiguar **cómo se conecta** el Tru-Test XRS2 a la tablet (Bluetooth clásico, BLE o teclado) y **qué bytes manda**. Con eso se escribe la spec 008.

Solo existe en la compilación de desarrollo (`dotnet build src/Aviss.App -f net10.0-android -t:Run`). No hace falta Internet ni se tocan los datos de los rodeos.

## Antes de empezar

1. **Pedirle a tu papá el archivo con el ID y el EID de los animales** (Excel o CSV: una columna con el ID de Dairy Comp y otra con el EID, que es el número que lee el bastón). Con ese archivo se resuelve la relación EID a ID; el spike no lo importa, solo lo vas a usar de referencia para comparar.
2. Tener a mano unas **5 caravanas o botones** (con su número impreso) y, si se puede, que sean animales que figuren en ese archivo.
3. Bastón cargado y encendido, tablet con batería.

## 1. Emparejar el bastón

1. En la app, abrí **Diagnóstico del bastón** (botón al final de la pantalla de inicio).
2. Tocá **Ajustes de Bluetooth** (abre los de Android) y emparejá el bastón como cualquier dispositivo Bluetooth (si pide PIN, probá `0000` o `1234`, o mirá el manual del XRS2).
3. Volvé a la app y tocá **Refrescar**. La primera vez Android pide el permiso de Bluetooth ("Dispositivos cercanos"): aceptalo.
4. Elegí el bastón en la lista. Anotá lo que muestra: **nombre, dirección, tipo** (Clásico, BLE o Dual) y los **UUID** que anuncia (si aparece `00001101-...` es puerto serie SPP).

## 2. Conectar y leer

1. Según el **tipo**: si es Clásico o Dual, tocá **Conectar (clásico SPP)**; si es BLE, **Conectar (BLE)**.
2. Mirá el **Estado**: Conectando, Conectado o Error con el motivo. Con SPP la app prueba sola tres métodos de socket con reintentos y deja cada intento en el archivo.
3. Con el estado en **Conectado**, leé una caravana con el bastón. Tiene que aparecer una fila en "Lecturas en vivo" con hora, hexadecimal, texto, separadores y el candidato a caravana (si hay un número de 15 dígitos).
4. **Anotá el número impreso:** escribilo en "Caravana impresa de la última lectura" y tocá **Anotar**. Queda en el archivo pegado a esa lectura.
5. Repetí con unas **5 caravanas**, **todas las veces que quieras la misma** (para ver si repite igual) y alguna leída dos veces seguidas.
6. Si el bastón no manda salto de línea, lo recibido queda "pendiente": tocá **Cerrar línea pendiente**. **Limpiar** pone los contadores en cero (el archivo no se borra).

## 3. Comparar con el archivo del EID

Con las lecturas hechas, buscá en el archivo de tu papá la caravana impresa que anotaste y mirá su **columna EID**: tiene que coincidir con el número que leyó el bastón (el candidato de 15 dígitos del archivo de registro). Anotá aparte si coincide **entero**, si el bastón agrega o saca ceros o el prefijo (por ejemplo `982`), o si manda otro formato. Eso define cómo se vincula el EID con el ID en la spec 008.

## 4. Sacar el archivo

Abajo de todo, en "Sacar el archivo":

- **Compartir:** abre el menú de Android (mail, WhatsApp, Drive...).
- **Guardar en Descargas:** deja una copia en la carpeta Descargas de la tablet.

El archivo se llama `diagnostico-baston-AAAAMMDD-HHMMSS.txt` (uno por sesión) y está en el almacenamiento privado de la app. Trae: hora, fuente, bytes en hexadecimal, texto, separadores, candidatos, las anotaciones y los intentos de conexión. **No lleva datos de animales ni de tambos**: solo lo que manda el bastón, el nombre y la dirección del dispositivo y la caravana que anotes. Pasalo junto con el archivo de EID (o una foto de la comparación).

## Si no conecta

Probá en este orden, mirando el motivo en pantalla y en el archivo:

1. **Bluetooth clásico (SPP):** el más probable. Si da error en los tres métodos: apagá y prendé el bastón, desemparejalo y emparejalo de nuevo, y reintentá. Que el bastón no esté conectado a otro aparato (otro teléfono, la balanza).
2. **BLE:** si el dispositivo figura como BLE o Dual, o SPP falla. La app lista servicios y características y se suscribe a las que notifican; si "Ninguna característica admite notificaciones", no es por acá.
3. **Teclado (HID):** si en los ajustes de Bluetooth el bastón se empareja como "teclado" o la conexión SPP no tiene sentido. Tocá el cuadro de la sección 3 de la pantalla y leé una caravana: cada línea terminada en Enter queda registrada. Si no hay Enter, escribe todo seguido en el cuadro: anotá lo que ves.
4. **Nada de lo anterior:** el XRS2 puede tener un modo de salida que se configura en el propio bastón (menú de Bluetooth o de salida de datos). Anotá qué opciones tiene y mandame fotos del menú.
5. Si Android no deja ver los dispositivos: Ajustes > Aplicaciones > Aviss > Permisos y dar "Dispositivos cercanos". Sin el permiso la lista queda vacía.

## Qué dejar anotado de la prueba

- Tipo de bastón (Clásico / BLE / Dual / teclado) y cuál ruta funcionó.
- Si la lectura llega en uno o varios paquetes y cómo termina (CR, LF, CRLF, nada).
- Formato del número leído y cómo se compara con la columna EID del archivo.
- Si se repite igual al leer la misma caravana varias veces.
- Qué pasa al apagar el bastón o alejarse (¿se corta? ¿reconecta solo?).

## Lo que se vio con el spike (2026-11-24) y qué hace ahora la app (spec 008)

Con la tablet SM-X110 (Android 16) y el bastón "XRS2i 6769" se comprobó:

- **Transporte:** Bluetooth clásico SPP (`00001101-0000-1000-8000-00805f9b34fb`).
- **Trama:** un botón llega como texto ASCII `982 000458467499` seguido de CRLF (15 dígitos con un espacio después de `982`; una línea por lectura). Es el EID que se importa en la columna `EID` (spec 010).
- **Conexión:** el socket **inseguro** conectó al primer intento en 1,4 s; el **seguro** falló 3 de 4 veces (11 s o 60 s de espera). Por eso la app prueba primero el inseguro y deja el seguro de respaldo.
- **Cortes:** el bastón no corta solo por estar en reposo; los cortes vistos fueron por apagarlo o alejarlo.

La **spec 008** convirtió eso en el lector real de la pantalla de trabajo. Para probarlo con el bastón y no con el Diagnóstico:

1. Emparejá el bastón en los ajustes de Bluetooth de Android (como antes) y abrí un rodeo **con EID importado** (archivo del padre, spec 010).
2. Entrá a **Trabajar**. La primera vez el cartel dice "Elegí el bastón": tocalo, aceptá el permiso de Bluetooth y elegí el tuyo en la lista (los XRS salen primero). La app lo recuerda; la próxima vez no pregunta.
3. El chip de arriba a la derecha pasa de "Buscando el bastón" a **Conectado** (verde). Leé un botón: se abre la ficha de esa vaca igual que escribiendo su número.
4. Probá apagar el bastón (el cartel pasa a "Sin bastón · prendelo o acercalo", vibra una vez) y prenderlo (reconecta solo). Con el bastón apagado, el botón grande "Tocá para reconectar" intenta de inmediato.
5. La lista completa de cosas a mirar está en `specs/008-lector-baston/tasks.md` (T5, "Qué mirar en la tablet"). Anotá además: cuánto tarda en reconectar, si leer con el bastón apagado y prenderlo reenvía esas lecturas, qué pasa con varios botones distintos muy seguidos y si el socket inseguro anduvo siempre.

El Diagnóstico del bastón sigue en la compilación de desarrollo, por si hace falta volver a mirar bytes.
