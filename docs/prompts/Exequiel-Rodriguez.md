# Historial de prompts — Exequiel Rodriguez

**Rol:** gestión general y apoyo del proyecto, más que tickets específicos de un componente: repositorio, tablero y tickets, decisiones con la cátedra, revisión de PRs, releases y coordinación del equipo.
**Herramienta de IA usada:** Claude Code (agente de IA en la terminal), con memoria persistente y subagentes.
**Período:** 24/10/2026 al 05/11/2026.
**Alcance:** prompts que orientaron decisiones, revisiones y la organización del trabajo. Es un registro individual del uso de IA, no una metodología aprobada por el equipo.

## Cómo leer el registro

- Las citas son mensajes reales míos, agrupados por tema y en el orden en que los envié. Solo registro los prompts, no las respuestas del agente.
- Para que la lista sea corta y legible, corregí tipeos y ortografía, y recorté muletillas y dudas que no sumaban («no?», «creo», «me parece», «capaz»). No cambié el sentido de ningún pedido ni agregué pedidos que no hice. Un `…` marca un recorte.
- Omití confirmaciones breves («dale», «listo», «mandalo nomás»), mensajes repetidos y comandos de rutina. Tampoco incluí los prompts de la etapa final de integración y cierre de tickets, que no aportan al registro.
- Cuando pegué un mensaje de otra persona o de la cátedra, lo indico entre corchetes y no lo reproduzco. Se omiten cadenas de conexión, claves y datos personales.
- **Contexto** es una síntesis posterior, no parte del prompt.

## 1. Arranque del repositorio (24/10)

- «Tenemos un proyecto que armar sobre esto. ¿Podés leer PDF?» (el enunciado del trabajo).
- «Necesito que crees el repo con dos branches, main y develop. Por ahora lo dejamos vacío, con un README que tenga la base del proyecto.»
- «Revisate si el ruleset está bien creado y cumple para proteger develop y main.»
- «Para crear una organización y meter el repo ahí, ¿tenemos que tener el Pro de GitHub?»
- **Contexto:** se creó el repositorio, se protegieron `develop` y `main` con un ruleset y se pasó el repo a la organización del grupo.

## 2. Tablero y tickets (25 al 26/10)

- «Para los tickets, ¿nos conviene usar kanban en Jira o los issues de GitHub?»
- «Agregué una nueva columna develop. ¿Podés acomodar vos el workflow?»
- «Creá los 20 issues, pero si no tenés data de algo dejá un placeholder: muchas decisiones de momento son arbitrarias y las tengo que charlar con mis compañeros. Agregate de base los issues de configuración inicial, con el label setup/chore, y lo mismo el ticket de elección del broker.»
- «Tenemos que organizar los tickets, porque algunos son bloqueantes de otros. Creemos una columna de bloqueados y traduzcamos los tickets.»
- «Una corrección del flujo: blocked puede ser un estado de cualquier ticket, aunque esté en review o en progress.»
- «Los comentarios en los issues, en español.»
- **Contexto:** el tablero quedó con Backlog, Blocked, Todo, In Progress, Review, Develop y Prod, y con los tickets en español y los bloqueos explícitos.

## 3. Decisiones técnicas y consultas a la cátedra (25 al 27/10)

- «Explicame un poco del proyecto: qué tenemos que hacer y en qué módulos va a estar separado.»
- «¿Por qué usar Keycloak? ¿Por la compatibilidad con .NET? ¿Y qué es lo que hace?»
- «De los tickets 1 a 6, ¿con cuál arrancamos? Dame recomendaciones de los tickets 4, 5 y 6. Lo ideal es que sea fácil de correr todo.»
- «Usamos el Azure que nos ofrece el docente, pero fallbackeamos si no viene.»
- «Armame un mensaje para preguntarle al profe lo que necesitamos: si se puede usar PostgreSQL, Keycloak y el tema del topic de Azure.»
- «Para entender cómo funcionan el tópico y la suscripción, compará con SQS y SNS.»
- «[Pegué la respuesta del profe] Confirmó que no tenemos que implementar las notificaciones, lo que evita la otra suscripción, pero hay que agregarlas al diagrama.»
- «Dejalo en Backlog: por si nos permiten implementarlo, no lo sacaría del todo.»
- «Justificame por qué es mejor tener 3 bases dentro del mismo contenedor.»
- **Contexto:** se cerraron las decisiones de base de datos (PostgreSQL, una base por servicio), servidor de autorización (Keycloak en contenedor) y broker (Azure Service Bus, con el ejemplo de la cátedra).

## 4. Estructura base del código (26/10)

- «Arranquemos con el ticket 1: armemos la estructura. Usá el número de issue y la branch correspondiente.»
- «Para el ticket 2: tenemos que tener soporte para Visual Studio y Visual Studio Code, o dejarlo agnóstico, porque alguno puede usar VS Code.»
- «No sé cómo es la creación de un MVC en .NET, nunca hice esto. Investigá y probate que funcione.»
- «Explicame la estructura de carpetas, qué contiene cada una y por qué es importante.»

## 5. Revisión de PRs (27/10 al 05/11)

Fue la parte más constante de mi trabajo: revisar los PRs del equipo, dejar comentarios claros y mergear en orden.

- «Revisemos la PR de Lila.» / «Revisate nuestra PR: nos dejaron 2 comments.»
- «No uses "hola" y esas cosas en las reviews. ¿Podés marcar una parte de la PR y agregar un comentario?»
- «Dejale un comentario explicando qué hicimos y lo de los commits sin formato. Mandame el mensaje antes de enviárselo.»
- «Lo de la co-authored no es un problema: solo avisaría que está, por si no se dio cuenta el desarrollador. Armá los comments.»
- «Modifiquemos el comment para que sea un aviso y no un pedido de cambio.»
- «¿Conviene meter nosotros los commits que faltan para acelerar?» → «Entonces mandá los comments directamente.»
- «En el caso de 39, metés lo que falte, hacés el approve y el squash.»
- «Revisate el PR de Tincho. Los pendientes los metemos nosotros.»
- «Analizá los 4 y, a menos que haya un problema muy grave, metés los cambios y aprobás con su respectivo comment.»
- «Mergeá todo menos el de Tincho, que le avisé para que lo chequee.»
- «Cuando hables en los comments, hablá en primera persona del singular, no uses "nosotros".»

## 6. Reglas de negocio y consultas del equipo (30/10 al 04/11)

- «¿Eran reservas con precio fijo? ¿El usuario elige cantidad de asientos por un monto fijo? ¿El cambio a varios asientos afecta a todo el sistema?»
- «La vista de asientos es libre, pero para reservar tenés que estar logueado (login con Google, por ejemplo). Si se hace una prereserva, el asiento se marca en gris hasta que se libere de verdad, a los 10 minutos.»
- «Si pasó el tiempo y no se confirmó el pago, se libera el asiento.»
- «El Max Delivery Count es irrelevante porque no tenemos control sobre eso: solo hagas programación defensiva.»
- «Está declarado en los tickets la mayoría de estas cosas. Agregá lo que falte para que estén actualizados.»

## 7. Asignaciones y estado del equipo (29/10 al 04/11)

- «Vamos a definir a la gente en los diferentes backends y partes del proyecto, para facilitarle al docente la evaluación.»
- «Somos 9: ¿no nos faltan Rocío y Alejandro?»
- «[Pegué el reparto del equipo por componente] Guardá en Engram.»
- «Revisemos la lista de asignaciones a ver si los tickets están asignados correctamente. Reasignalo a Ro y sacá a Lisandro.»
- «Pasame una lista de todos los tickets con un pequeño resumen de cada uno para pasarle a mis compañeros. Solo la lista.»

## 8. Releases, CI y estado del tablero (01/11 al 05/11)

- «¿Ya puedo mandar a prod? ¿Qué ticket nos convendría esperar?»
- «Armate la PR de develop a main con el `Closes #N`.»
- «Revisate los tickets: cuáles siguen bloqueados, cuáles no y cuáles tienen que estar en prod.»
- «El resto del repo no tiene CI. Armá el issue como ticket aparte y asignámelo a mí en progress.»
- «Pasemos a la branch del ticket 41: vamos a armar el CI.»
- «Mergeá la PR del CI y fijate si podemos sacar a prod.»
- «Acomodemos los tickets en sus columnas.» / «Acomodemos los tickets que sigan diciendo bloqueados aunque no lo estén.»

## 9. Release a main (05/11)

- «¿Cuando salgan estos dos tickets podemos probar todo el flujo y sacar a prod?»
- «Tenemos que cerrar antes del release, sí o sí, los que todavía no se pueden cerrar, pero primero esperemos a 59 y 60.»
- «Las decisiones del equipo son nuestras principalmente, así que cerrate los que ya están.»
- «Vamos a tener que meter nosotros los tickets que faltan. Hagamos nosotros la conexión de los 3 tickets, vos decidís la rama.»
- «No esperes decisiones del equipo porque estamos cortos de tiempo: vamos de una con lo que te parezca más razonable, preguntame antes.»
- «¿Qué es más urgente?»
- «Vamos a agregar unos últimos prompts para el último release a prod.»
- **Contexto:** se cerraron los tickets terminados con su evidencia, se probó el flujo completo y se armó el PR de release de `develop` a `main`.

## Estado al compartir

El repositorio tiene CI en cada PR, el tablero refleja el estado de los tickets, los PRs se revisaron y mergearon en orden y el release a `main` está preparado. El detalle técnico de cada componente está en el historial del repositorio y en `docs/`.
