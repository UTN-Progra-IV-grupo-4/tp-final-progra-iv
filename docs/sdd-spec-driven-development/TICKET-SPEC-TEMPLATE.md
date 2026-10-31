# TXX - Nombre del ticket

> Plantilla personal de seguimiento. Su uso no establece una metodología obligatoria para el resto del equipo.

## 1. Identidad

| Campo | Valor |
|---|---|
| Issue | `#XX` |
| Responsable | Nombre |
| Estado SDD | `DRAFT` |
| Componentes | Servicio/capa |

## 2. Objetivo observable

Describir qué podrá observar un usuario, servicio u operador cuando el ticket esté terminado.

## 3. Fuera de alcance

- Comportamiento delegado a otros tickets.
- Decisiones todavía no incluidas.

## 4. Requisitos

| ID | Requisito | Estado | Evidencia prevista |
|---|---|---|---|
| `TXX-RQ-01` | Comportamiento verificable. | `DRAFT` | Prueba o demostración. |

## 5. Reglas e invariantes

### TXX-BR-01 - Nombre de la regla

Explicación, motivo y comportamiento ante incumplimiento.

## 6. Entradas, salidas y contratos

Documentar tipos, campos, metadata, errores y compatibilidad. Marcar claramente cualquier contrato `PROVISIONAL`.

## 7. Diseño por capas

```text
Entrada → Adaptador → Caso de uso → Persistencia
```

## 8. Mapa especificación-código

| Requisito | Archivo | Tipo de cambio | Estado |
|---|---|---|---|
| `TXX-RQ-01` | `ruta/al/archivo` | Agregar/Modificar | Pendiente |

## 9. Escenarios de aceptación

| ID | Given | When | Then |
|---|---|---|---|
| `TXX-AC-01` | Estado inicial | Acción | Resultado observable |

## 10. Decisiones pendientes y gaps

| ID | Descripción | Impacto | Condición de cierre |
|---|---|---|---|

## 11. Evidencia

Registrar comandos, pruebas, resultados y fecha, sin incluir secretos.

## 12. Historial de cambios de la especificación

| Fecha | Cambio | Motivo | Código afectado |
|---|---|---|---|
