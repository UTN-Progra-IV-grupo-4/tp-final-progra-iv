# Registro personal de desarrollo por especificaciones

Esta carpeta es un registro de trabajo personal para los tickets desarrollados por Alejandro. Utiliza prácticas de Spec-Driven Development para mantener trazables los requisitos, las decisiones, el código y las pruebas, pero no establece una metodología obligatoria para el equipo.

No forma parte de las decisiones técnicas aprobadas del proyecto y no se publica como convención en el README principal. Ante cualquier contradicción, prevalecen el enunciado, los issues, los contratos y las decisiones acordadas por el equipo.

## Cómo se usa

Para cada ticket se mantiene una especificación viva con cuatro preguntas:

1. ¿Qué comportamiento debe entregar?
2. ¿Qué reglas e invariantes no pueden romperse?
3. ¿Qué archivos implementan cada requisito?
4. ¿Qué evidencia demuestra que funciona?

Este registro se actualiza junto con los cambios propios para evitar que describa un comportamiento diferente del código implementado.

## Referencias utilizadas

Si aparece una contradicción, se resuelve en este orden:

1. Enunciado original del trabajo práctico.
2. Criterios de aceptación del issue.
3. Contratos compartidos aprobados.
4. Decisiones técnicas cerradas por el equipo.
5. Código y pruebas implementados en el ticket.
6. Estas notas personales de desarrollo.

Estas notas no pueden crear requisitos ni decisiones para otros integrantes. Los supuestos temporales se marcan como `PROVISORIO` y deben indicar cómo serán reemplazados.

## Estructura

```text
docs/sdd-spec-driven-development/
├── README.md
├── PROJECT-SPEC.md
├── TICKET-SPEC-TEMPLATE.md
└── tickets/
    └── T18-booking-consumer/
        ├── SPEC.md
        └── IMPLEMENTATION-LOG.md
```

- `PROJECT-SPEC.md`: contexto de arquitectura utilizado para comprender los desarrollos propios.
- `TICKET-SPEC-TEMPLATE.md`: formato personal reutilizable para tickets futuros.
- `SPEC.md`: requisitos observables y diseño aceptado del ticket.
- `IMPLEMENTATION-LOG.md`: relación cronológica entre especificación, código y validaciones.

## Estados

| Estado | Significado |
|---|---|
| `DRAFT` | Especificación todavía incompleta. |
| `PROVISIONAL` | Permite avanzar con un supuesto explícito y reemplazable. |
| `READY` | Requisitos suficientes para implementar. |
| `IMPLEMENTING` | Código en desarrollo. |
| `PARTIALLY_VERIFIED` | Compila o tiene validaciones parciales, pero falta evidencia de integración. |
| `VERIFIED` | Criterios de aceptación demostrados. |
| `BLOCKED` | Falta una decisión o dependencia necesaria. |

## Reglas de trazabilidad

- Cada requisito usa un identificador estable, por ejemplo `T18-RQ-01`.
- Cada regla usa un identificador, por ejemplo `T18-BR-03`.
- Los archivos modificados indican qué requisitos implementan.
- Las pruebas indican qué requisitos verifican.
- Los resultados de compilación o pruebas se registran sin copiar secretos.
- Un cambio de contrato debe actualizar la especificación, el mapper y sus pruebas.
- Nunca se documentan connection strings, tokens o contraseñas reales.

La hoja de ruta de cada ticket registra fases, prompts y decisiones. Estas notas complementan esa hoja para uso personal: la hoja de ruta explica el proceso y la especificación registra el comportamiento verificable implementado.
