# Plantilla de hoja de ruta por ticket

Esta plantilla mantiene el mismo lenguaje y la misma evidencia para todos los tickets del TP. Su objetivo es registrar qué se pidió, qué se decidió, cómo se implementó y cómo se verificó, además de conservar el historial individual de prompts requerido por la consigna.

## 1. Nomenclatura

Todos los identificadores son estables, ASCII y únicos dentro del ticket.

| Elemento | Formato | Ejemplo | Uso |
|---|---|---|---|
| Ticket | `T<numero>` | `T18` | Identidad abreviada del ticket. |
| Fase | `T<numero>-F<nn>` | `T18-F03` | Etapa ordenada del desarrollo. |
| Prompt | `T<numero>-F<nn>-P<nn>` | `T18-F03-P01` | Pregunta o instrucción enviada al modelo. |
| Respuesta | `T<numero>-F<nn>-R<nn>` | `T18-F03-R01` | Resultado clave correspondiente al prompt del mismo número. |
| Decisión | `T<numero>-D<nn>` | `T18-D02` | Decisión técnica aceptada y su justificación. |
| Bloqueo | `T<numero>-B<nn>` | `T18-B01` | Dependencia o definición que impide cerrar una fase. |
| Gap provisorio | `T<numero>-G<nn>` | `T18-G01` | Supuesto temporal, aislado y explícitamente reemplazable. |
| Validación | `T<numero>-V<nn>` | `T18-V03` | Prueba o evidencia reproducible. |
| Cambio | `T<numero>-C<nn>` | `T18-C02` | Grupo coherente de archivos o commit previsto. |

La pareja `P01/R01` siempre corresponde a la misma interacción. Si un prompt requiere una corrección, se registra como `P02/R02`; no se sobrescribe el historial anterior.

## 2. Estados

| Estado | Significado |
|---|---|
| `PENDIENTE` | Todavía no iniciado. |
| `EN_CURSO` | Trabajo activo, aún sin evidencia final. |
| `PARCIAL` | Parte del resultado fue validada, pero falta revalidar una condición necesaria. |
| `BLOQUEADO` | Falta una decisión o dependencia externa. |
| `PROVISORIO` | Permite avanzar con un supuesto aislado que deberá revisarse. |
| `VALIDADO` | Resultado implementado y verificado. |
| `DESCARTADO` | Alternativa evaluada y rechazada con justificación. |

## 3. Reglas del historial de prompts

1. Conservar el texto del prompt o una versión sanitizada que mantenga su intención.
2. Nunca registrar claves, tokens, contraseñas, connection strings ni datos personales innecesarios.
3. Si el prompt original contenía un secreto, reemplazarlo por `[REDACTADO]` y marcarlo como sanitizado.
4. Resumir la respuesta clave; no copiar razonamientos internos ni salidas extensas sin valor para la entrega.
5. Vincular cada interacción con decisiones, cambios y validaciones cuando corresponda.
6. Registrar también intentos fallidos relevantes y explicar qué se aprendió.
7. Todo gap provisorio debe indicar qué archivo o capa cambiará, qué condición lo elimina y cómo se verificará el reemplazo.

Formato recomendado:

```markdown
### T00-F00-P01 - Título breve

- Fecha: AAAA-MM-DD
- Estado: PENDIENTE | EN_CURSO | PARCIAL | VALIDADO | BLOQUEADO
- Prompt: "Texto literal o sanitizado"
- Respuesta clave (`T00-F00-R01`): Resultado importante.
- Decisiones relacionadas: T00-D01
- Cambios relacionados: T00-C01
- Validaciones relacionadas: T00-V01
```

## 4. Estructura fija de cada hoja de ruta

Cada archivo de ticket debe contener, en este orden:

1. Identidad: issue, rama, responsable y componente.
2. Fuentes: consigna, issue, contratos y decisiones del equipo.
3. Objetivo y fuera de alcance.
4. Estado inicial comprobado.
5. Decisiones y bloqueos.
6. Fases de desarrollo y criterio de salida.
7. Historial real de prompts y respuestas.
8. Prompts previstos para las fases pendientes.
9. Matriz de validación.
10. Guion breve para la defensa oral.
11. Definición de terminado.

## 5. Definición de terminado

Un ticket se considera terminado cuando:

- Cumple todos sus criterios de aceptación.
- Compila sin errores.
- Las pruebas relevantes pasan y su ejecución está documentada.
- No contiene secretos ni datos locales versionados.
- Respeta la arquitectura y los contratos acordados.
- Tiene logs suficientes para demostrar el flujo.
- La hoja de ruta refleja decisiones, prompts y evidencias reales.
- El responsable puede explicar el problema, la solución, los fallos contemplados y los tradeoffs.
