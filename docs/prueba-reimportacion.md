# Prueba de reimportación en Dairy Comp (paso 0.2)

Objetivo: saber cómo toma Dairy Comp un Excel modificado, para definir la exportación (spec de exportación, paso 4.1). Se hace en la PC con Dairy Comp. No hace falta la tablet.

## Antes de empezar
- [ ] Tengo un `.xls` exportado de Dairy Comp del rodeo de prueba.
- [ ] Hice una **copia** del original y trabajo solo sobre la copia.
- [ ] Uso un rodeo o una base de prueba, no datos reales en producción.
- [ ] No se commitea ningún Excel real al repo (las muestras van anonimizadas en `tests/muestras/`).

Datos de la prueba:

| Campo | Valor |
|---|---|
| Fecha | |
| Versión de Dairy Comp | |
| Archivo original | |
| Cantidad de animales | |
| Formato exportado (`.xls` / `.xlsx`) | |

## Prueba A: copia sin cambios

Reimportar la copia exacta, sin tocar nada.

| Pregunta | Resultado |
|---|---|
| ¿Dairy Comp la aceptó? | |
| ¿Pidió mapear columnas o usa formato fijo? | |
| ¿Acepta `.xls` y `.xlsx`? (probar los dos si se puede) | |
| ¿Qué hizo con la fila `Total`? | |
| ¿Cómo trató las fechas (texto o fecha)? | |
| ¿Creó eventos o cambió algún dato? | |
| Mensajes de error o aviso (copiar el texto exacto) | |

## Prueba B: una vaca a PREÑADA

Elegir una vaca. Anotar sus valores **antes**, cambiar solo lo necesario y reimportar.

| Campo | Valor |
|---|---|
| ID de la vaca | |
| Estado reproductivo antes | |

| Columna cambiada | Valor antes | Valor después |
|---|---|---|
| | | |
| | | |
| | | |

| Pregunta | Resultado |
|---|---|
| ¿Dairy Comp la pasó a PREÑADA? | |
| ¿Generó un evento de tacto/preñez? ¿Con qué fecha? | |
| ¿Hizo falta cambiar más columnas de las que pensaba? ¿Cuáles? | |
| ¿Cambió algo que no esperaba (otras columnas, otras vacas)? | |
| Mensajes de error o aviso | |

## Prueba C: una vaca a VACIA

Igual que la B, con otra vaca.

| Campo | Valor |
|---|---|
| ID de la vaca | |
| Estado reproductivo antes | |

| Columna cambiada | Valor antes | Valor después |
|---|---|---|
| | | |
| | | |
| | | |

| Pregunta | Resultado |
|---|---|
| ¿Dairy Comp la pasó a VACIA? | |
| ¿Generó el evento esperado? ¿Con qué fecha? | |
| ¿Hizo falta cambiar más columnas? ¿Cuáles? | |
| ¿Cambió algo que no esperaba? | |
| Mensajes de error o aviso | |

## Pruebas extra (si hay tiempo)
- [ ] Una observación o evento de texto libre (por ejemplo, un tratamiento): ¿dónde se escribe y lo toma?
- [ ] Una vaca con CVET distinto de 0: ¿cambia algo al reimportar?
- [ ] Dos vacas cambiadas a la vez: ¿se procesan bien las dos?
- [ ] Reimportar el mismo archivo dos veces: ¿duplica eventos?

## Conclusiones (completar al final)
- Columnas que la exportación **debe** cambiar para cada tacto:
- Columnas que **no** hay que tocar:
- Formato que acepta (`.xls` o `.xlsx`):
- Qué hacer con la fila `Total` y con las fechas:
- Riesgos o dudas que quedaron:

Cuando esté completa: pasar las conclusiones a `MEMORY.md`, marcar el paso 0.2 del [plan](plan.md) y actualizar la sección 14 de [arquitectura](arquitectura.md).
