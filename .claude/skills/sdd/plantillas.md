# Plantillas SDD de Aviss

## Formato EARS para requisitos

EARS (*Easy Approach to Requirements Syntax*) escribe cada requisito con una estructura fija, así no hay ambigüedad:

| Tipo | Estructura | Ejemplo |
|---|---|---|
| Siempre | El sistema **deberá** … | El sistema deberá guardar cada tacto en SQLite en el momento de confirmarlo. |
| Evento | **Cuando** <evento>, el sistema **deberá** … | Cuando el bastón lee una caravana del rodeo abierto, el sistema deberá mostrar la ficha de esa vaca. |
| Estado | **Mientras** <estado>, el sistema **deberá** … | Mientras no haya un veterinario elegido, el sistema deberá impedir registrar tactos. |
| Opcional | **Donde** <condición/característica>, el sistema **deberá** … | Donde la vaca no esté en el rodeo, el sistema deberá ofrecer darla de alta en campo. |
| No deseado | **Si** <situación no deseada>, **entonces** el sistema **deberá** … | Si el archivo Excel no tiene la columna `ID`, entonces el sistema deberá rechazarlo con un mensaje claro y no importar nada. |

---

## spec.md

```markdown
# NNN — <Nombre de la funcionalidad>

Estado: borrador | aprobada
Fecha: AAAA-MM-DD

## Contexto
Qué problema del veterinario o del tambo resuelve y por qué hace falta. Quién lo usa.

## Alcance
- Incluye: …
- No incluye: …

## Requisitos funcionales
- **RF-1** — <requisito en EARS>
- **RF-2** — …

## Casos límite
- (sin conexión, corte de energía, Excel mal formado, lecturas repetidas, vaca no encontrada, varios veterinarios o tambos…)

## Criterios de aceptación
- [ ] …

## Preguntas abiertas
- (vacío cuando la spec se aprueba)
```

---

## plan.md

```markdown
# Plan — NNN <Nombre>

Spec: [spec.md](spec.md)

## Resumen
2–3 líneas del enfoque.

## Base de datos
Entidades nuevas o modificadas, migración de EF Core, SQL a mano (triggers, índices únicos, CHECK).

## Capas y servicios
| Capa | Qué se agrega (interfaces, servicios, repositorios, DTOs) | RF |
|---|---|---|

## Pantallas
Pantallas y ViewModels, y cómo se ven en tablet (botones grandes, alto contraste).

## Archivos
- Crear: …
- Modificar: …

## Decisiones
- **<Decisión>** — elegida porque … Alternativa descartada: … porque …

## Estrategia de tests
| RF | Test (proyecto › clase › caso) |
|---|---|

## Riesgos
- …
```

---

## tasks.md

```markdown
# Tareas — NNN <Nombre>

- [ ] **T1** — <tarea>
  - RF: RF-1, RF-2
  - Hecho cuando: <condición verificable, por ejemplo "los tests X pasan">
  - Qué mirar en el emulador: <solo en tareas de pantalla>
- [ ] **T2** — …
```

Máximo 5 tareas, en orden y verificables. Agrupar lo que va junto (entidades y migración, un caso de uso completo, una pantalla, cierre dentro de la última).
