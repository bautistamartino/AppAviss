# Requerimientos de Aviss

Visión general de lo que debe hacer el sistema. El detalle fino de cada funcionalidad vive en las specs (`specs/NNN-nombre/spec.md`).

## Problema

Hoy el flujo es: sistema principal (Dairy Comp) → cargar rodeo → sincronizar tablet → trabajar con bastón RFID → sincronizar de nuevo. Hay que cargar los animales antes, cada tablet necesita una licencia, preparar cada visita es lento y todo depende de software propietario.

## Objetivo

Una app Android para tablet que funcione **sin Internet**, que:

1. Importe un Excel generado por Dairy Comp y lo valide.
2. Guarde todo en SQLite y trabaje solo sobre SQLite.
3. Se conecte con el bastón Tru-Test XRS2.
4. Registre cada cambio (tacto) y mantenga el historial completo.
5. Exporte un Excel que Dairy Comp pueda importar.

Se usa en **varias tablets**, con **varios veterinarios** que trabajan en **muchos tambos**.

## Flujo completo

Dairy Comp exporta Excel → app importa y valida → importa a SQLite → conecta el bastón → trabajo de campo (lectura → ficha → nuevo estado → guarda → siguiente lectura) → exporta Excel → se importa en Dairy Comp.

## Datos de entrada (Excel de Dairy Comp, `.xls`)

Archivo de muestra: una hoja, una fila por vaca, más una fila final `Total:N`. Columnas:

| Columna | Qué es |
|---|---|
| `ID` | Caravana y botón (número, único) |
| `CVET` | Código veterinario (`0`, `PRE`) |
| `FPART` | Fecha del último parto (texto, con espacios) |
| `LACT`, `DEL` | Lactancia y días en leche |
| `FCELO` | Fecha de celo |
| `VINS` | **Cantidad** de inseminaciones (un número, no una fecha) |
| `UTORO` | Último toro |
| `COM` | Comentarios (`46DIAS`, `F007T001`, `2.8`, `3`) |
| `RPRO` | Estado reproductivo: PREÑADA, INSEMIN, SECA, VACIA, FRESCA, NOINSEM |
| `FAPAR` | Fecha probable de parto |

Dairy Comp **usa el `ID`** para cruzar con la vaca y **crea eventos con historial** al importar. Si acepta `.xls`, `.xlsx` o ambos, y si mapea columnas, está **por validar** con una prueba manual.

## Requisitos funcionales (generales)

- **Importación**: leer el Excel, validar estructura y datos, mostrar vista previa con errores por fila y confirmar. Ignorar la fila `Total`. Guardar el original.
- **Rodeos y tambos**: varios tambos por tablet; cada importación es un rodeo con nombre, fecha, cantidad de animales y archivo de origen.
- **Trabajo de campo**: leer la caravana con el bastón, buscar la vaca automáticamente, mostrar la ficha, elegir el evento (y una observación opcional) y guardar con "Entrar". Búsqueda manual como respaldo. Alta en campo de un animal no encontrado.
  - **Mangas** (spec 003): el trabajo se organiza en mangas numeradas por rodeo; cada vaca leída queda **marcada automáticamente** en la manga actual (contador de vacas distintas), con aviso si ya pasó ese día. Se cierran con "Finalizar manga" (verificando el contador contra la cantidad real) o "Terminar el tacto" (doble confirmación); un cierre se puede deshacer reabriendo la manga mientras sea el último del día.
  - **Registros y catálogos de campo** (spec 004, borrador): tratamientos, nota y días de preñez del tacto, rechazo y estados nuevos creados en el campo.
- **Historial**: registrar fecha, animal, estado anterior y nuevo, veterinario y hora de cada cambio. Nunca borrar ni sobrescribir. Deshacer mediante anulación.
- **Veterinarios**: elegir quién trabaja al abrir la app.
- **Exportación**: generar un Excel compatible con Dairy Comp a partir del original, con los cambios; validarlo antes de entregarlo.
- **Lector RFID**: conectarse con el XRS2 (Bluetooth u otro método según el spike), reconectar solo, ignorar lecturas repetidas. Debe poder cambiarse el método de comunicación sin tocar el resto.
- **Autoguardado y recuperación**: no perder datos ante un apagón o cierre inesperado; backups automáticos de la base.

## Requisitos no funcionales

- Sin Internet en todo el flujo de trabajo.
- Rápida, usable con una mano, legible al sol, botones grandes, mínima escritura.
- Mantenible y escalable: otros sistemas principales, otros lectores, otros eventos (pesaje, sanidad) y una posible sincronización en la nube.

## Fuera de alcance (por ahora)

- Sincronización con servidor o nube.
- iOS u otras plataformas.
- Reemplazar a Dairy Comp: Aviss es una app intermedia de trabajo de campo.

## Riesgos y pendientes de validar

- Cómo importa Dairy Comp el Excel de vuelta (formato, columnas, eventos).
- Comportamiento real del XRS2 (transporte, trama, relación con el `ID`). Sin bastón ni tablet por ahora: se desarrolla con emulador y lector simulado.
- Instalación del SDK de .NET, la carga de trabajo de MAUI y el SDK de Android en la PC de desarrollo.
