# Historial de prompts — Exequiel Rodriguez

**Rol:** gestión general y apoyo del proyecto, más que tickets específicos de un componente: repositorio, tablero y tickets, decisiones con la cátedra, revisión de PRs, releases, coordinación del equipo y el trabajo transversal (#8 diagramas, #24 logs de auditoría, #25 demo).
**Herramienta de IA usada:** Claude Code (agente de IA en la terminal), con memoria persistente y subagentes.
**Período:** 24/10/2026 al 05/11/2026.
**Alcance:** prompts que orientaron decisiones, revisiones, pruebas y la organización del trabajo. Este es un registro individual del uso de IA, no una metodología aprobada por el equipo.

## Cómo leer el registro

- Las citas son mensajes reales míos, en el orden en que los envié, agrupadas por tema. Solo registro los prompts, no las respuestas del agente.
- Para que sea legible corregí errores de tipeo y ortografía (por ejemplo «keycloack» por «Keycloak») y acomodé la puntuación; no cambié el sentido ni agregué pedidos que no hice.
- Un `…` marca un recorte. Omití confirmaciones breves («dale», «listo», «mandalo nomás»), mensajes repetidos y comandos de rutina.
- Cuando pegué un mensaje de otra persona o de la cátedra, lo indico entre corchetes y no lo reproduzco. Se omiten además cadenas de conexión, claves y datos personales.
- **Contexto** es una síntesis posterior, no parte del prompt.

## 1. Arranque del repositorio (24/10)

- **Prompt:** «Hola, tenemos un proyecto que armar sobre esto» y, después, «Podés leer PDF?» (el enunciado del trabajo).
- **Prompt:** «Necesito que crees el repo con dos branchs, main o master y develop, y qué más necesitamos? Por ahora lo dejamos vacío, si querés con un README que tenga la base del proyecto.»
- **Prompt:** «Por ahora solo el README explicando el proyecto, las ramas capaz las hacemos con número de ticket con Jira u otro gestor.»
- **Prompt:** «Revisate si el ruleset está bien creado y cumple para proteger develop y main.»
- **Prompt:** «Para crear una organización y meter el repo ahí tenemos que tener el Pro de GitHub, no?»
- **Contexto:** se creó el repositorio, se protegieron `develop` y `main` con un ruleset y se movió el repo a la organización del grupo.

## 2. Tablero y organización de tickets (25 al 26/10)

- **Prompt:** «Para los tickets nos conviene usar kanban en Jira o los issues de GitHub?»
- **Prompt:** «Agregué una nueva columna develop. ¿Podés acomodar vos el workflow?»
- **Prompt:** «Si la idea es C#/.NET porque fue el lenguaje de la materia… Pero más allá de eso, ya podemos crear issues?»
- **Prompt:** «Sí, crea los 20 issues, pero si no tenés data de algo dejá un placeholder, muchas decisiones de momento son arbitrarias, lo tengo que charlar con mis compañeros. Agregate de base los issues de configuraciones iniciales del proyecto, con el label de setup/chore o lo que consideres necesario; lo mismo el ticket de elección del broker, a ver si quieren usar otra cosa.»
- **Prompt:** «Antes que nada, tenemos que organizar los tickets, porque algunos son bloqueantes de otros. Vamos a crear una columna nueva de bloqueados y, de paso, traduzcamos los tickets.»
- **Prompt:** «Un pequeño detalle que te corrijo del flujo de los tickets: blocked puede ser un estado de cualquier ticket, aunque esté en review o en progress, no importa.»
- **Prompt:** «Por cierto, los comentarios en los issues en español.»
- **Contexto:** armé el tablero con las columnas Backlog, Blocked, Todo, In Progress, Review, Develop y Prod; los tickets se crearon en español y con bloqueos explícitos.

## 3. Entender el proyecto y las decisiones técnicas (25 al 26/10)

- **Prompt:** «Explicame un poco del proyecto, qué tenemos que hacer? En qué módulos va a estar separado?»
- **Prompt:** «Básicamente es un sistema de ticketería típico de cine o conciertos.»
- **Prompt:** «Keycloak, por qué usar ese? Por la compatibilidad con .NET? … Pero qué es lo que hace Keycloak?»
- **Prompt:** «De los tickets 1 a 6, con cuál arrancamos?» y «Dame recomendaciones de cada uno de los tickets 4, 5 y 6.»
- **Prompt:** «Lo ideal es que también sea fácil de correr todo.»
- **Prompt:** «Usamos el Azure que nos ofrece el docente pero fallbackeamos si no viene.»
- **Prompt:** «Armame un mensaje para preguntar lo que necesitamos para el profe. Ejemplo si se puede usar PostgreSQL, usar Keycloak y el tema del topic de Azure.»
- **Prompt:** «Para entender cómo funciona el tópico y la suscripción, compáralo con SQS y SNS si querés.»
- **Contexto:** se cerraron las decisiones de motor de base de datos (PostgreSQL con una base por servicio), servidor de autorización (Keycloak en contenedor) y broker (Azure Service Bus con el ejemplo de la cátedra), y se redactó la consulta al docente.

## 4. Estructura base del código (26/10)

- **Prompt:** «Arranquemos con el 1, podemos armar la estructura ya, no?» y «Usá el número de issue y la branch correspondiente.»
- **Prompt:** «Vamos con el ticket 2, así creamos la estructura base. Importante: tendríamos que tener soporte para Visual Studio y Visual Studio Code, o dejarlo agnóstico, porque capaz alguno usa VS Code.»
- **Prompt:** «No sé cómo es la creación de un MVC en .NET, nunca hice esto, investigá. … Y probate que funcione.»
- **Prompt:** «Antes de seguir, explicame la estructura de carpetas, qué contiene cada una y por qué es importante.»
- **Prompt:** «Ahora entiendo, entonces son 3 backends: Availability, Booking y Payment.»

## 5. Decisiones con la cátedra (26/10)

- **Prompt:** «[Pegué la respuesta del profe] Algunos detalles importantes que confirmó el profe: no tenemos que implementar el sistema de notificaciones, lo que evita necesitar la otra subscription, pero sí hay que agregarlo al diagrama.»
- **Prompt:** «Dejalo en Backlog, por si nos permiten implementarlo no lo sacaría del todo, me parecería interesante implementarlo.»
- **Prompt:** «Justificame por qué es mejor tener 3 bases dentro del mismo contenedor.» / «Después lo justificamos en la defensa, cualquier cosa.»

## 6. Revisión de PRs (27/10 al 05/11)

Fue la parte más constante de mi trabajo: revisar los PRs del equipo, dejar comentarios claros y mergear en orden.

- **Prompt:** «Revisemos la PR de Lila.» / «Revisate nuestra PR, nos dejaron 2 comments.»
- **Prompt:** «No uses "hola" y esas cosas en las reviews. ¿Vos podés marcar una parte de la PR y agregar un comentario, o solo se hace a mano?»
- **Prompt:** «Bien, dejale un comentario explicando qué hicimos y explicale lo de los commits sin formato. Mandame el mensaje antes de enviárselo.» / «No le digas lo del equipo, nada más.»
- **Prompt:** «Lo de la co-authored no es un problema, solo avisaría que está por si no se dio cuenta el desarrollador. Armá los comments.»
- **Prompt:** «Modifiquemos el comment para que sea un aviso y no un pedido de cambio.»
- **Prompt:** «Una duda: conviene meter nosotros los commits que faltan para acelerar?» → «No, entonces mandá los comments directamente.»
- **Prompt:** «En el caso de 39, metés lo que falte, hacés el approve y metés el squash.» / «¿Cómo funciona el squash? ¿Lo hace el dueño de la rama o nosotros?»
- **Prompt:** «Revisate el PR de Tincho.» / «Los pendientes los metemos nosotros.» / «Aprobado, mergeá.»
- **Prompt:** «Perfecto, analizá los 4 y, a menos que haya un problema muy grave, metés los cambios y aprobás con su respectivo comment.»
- **Prompt:** «Bien, mergeá todo menos el de Tincho, que le avisé para que lo chequee.»
- **Prompt:** «Cuando hables en los comments hablá en primera persona del singular, no uses "nosotros", por favor.»

## 7. Reglas de negocio y consultas del equipo (30/10 al 04/11)

- **Prompt:** «Era precio fijo, no? … ¿Sería un precio fijo donde el usuario elige cantidad de asientos por monto fijo igual precio final? … Igual, el cambio a varios asientos afecta a todo el sistema o no?»
- **Prompt:** «En teoría la vista de asientos es libre pero para reservar tenés que estar logueado (login con Google, por ejemplo). Si se hace una prereserva ya se marca el asiento como reservado hasta que venzan los 10 minutos, o sea que el asiento se marca en gris hasta que se libere de verdad.»
- **Prompt:** «Lo ideal es que si está vencido o ya pasó el tiempo y no se confirmó el pago, se libere el asiento.»
- **Prompt:** «Max Delivery Count igual es irrelevante porque realmente no tenemos control sobre eso, solo hagas programación defensiva y listo.»
- **Prompt:** «Este sí es obligatorio, tenemos que usar un servicio de terceros» (sobre si Google es obligatorio en #10).
- **Prompt:** «Estaba declarado en los tickets la mayoría de estas cosas, no? … Agregá lo que falte en los tickets para que estén actualizados.»
- **Contexto:** respondí consultas del equipo y de la cátedra, y llevé las decisiones a los tickets para que quedaran escritas.

## 8. Asignaciones y estado del equipo (29/10, 01/11 y 04/11)

- **Prompt:** «Vamos a definir a la gente en los diferentes backends y partes del proyecto para hacerlo más fácil para el docente evaluarlos después.»
- **Prompt:** «Somos 9, no nos faltan Rocío y Alejandro?»
- **Prompt:** «[Pegué el reparto del equipo por componente] Perfecto, guardá en Engram.»
- **Prompt:** «Revisemos la lista de asignaciones a ver si los tickets están asignados correctamente. … Reasignalo a Ro y sacá a Lisandro.»
- **Prompt:** «Dame un mensajito así mando sobre esto.» / «Pasame los números nada más.»
- **Prompt:** «Pasame una lista de todos los tickets y un pequeño resumen de cada uno para pasarle a mis compañeros, solamente la lista, no necesito saludos ni nada.»

## 9. Releases, CI y estado del tablero (01/11 al 04/11)

- **Prompt:** «Ya puedo mandar a prod, no? O qué ticket nos convendría esperar?»
- **Prompt:** «Armate la PR de develop a main con el `Closes #N`.»
- **Prompt:** «Revisate los tickets a ver cuáles todavía están bloqueados, cuáles no y cuáles tienen que estar en prod.»
- **Prompt:** «El resto del repo tiene CI? Creo que no. … Lo armamos como un ticket aparte para todo el repo o no? … Dale, armá el issue y asignámelo a mí en progress.»
- **Prompt:** «Pasemos a la branch del ticket 41, vamos a armar el CI.»
- **Prompt:** «Listo la PR del CI, mergeala nomás y fijate si podemos sacar a prod.»
- **Prompt:** «Acomodemos los tickets en sus respectivas columnas.» / «Acomodemos los tickets que sigan diciendo bloqueados aunque no lo estén.»

## 10. Google, Azure y pruebas de integración (04 y 05/11)

- **Prompt:** «Lo de Google lo hacemos nosotros, qué necesitás?»
- **Prompts siguientes (resumidos):** preguntas de configuración sobre el modo testing, qué email usar, la URL de redirección y dónde cargar las credenciales.
- **Prompt:** «Ya lo cargué en el .env, quedan vacíos el resto de valores porque no sé qué va, testealo.»
- **Prompt:** «Tengo el connection string, te lo paso, total después lo van a cerrar. ¿Cómo se usa?» (omito la cadena).
- **Prompts siguientes (resumidos):** preguntas sobre cómo cargarlo de forma segura y confirmaciones de cada paso para probar el envío real.
- **Prompt:** «Que la tengas vos no hay drama, esta clave una vez termine el proyecto se elimina, es de la cátedra.»
- **Prompt:** «Tenés acceso a crear esas cosas? Nos la mandó la cátedra a ese tópico, no debería haber problema en usar la misma, creo, nadie está con el proyecto ahora.»
- **Prompt:** «Docker corriendo, no esperes decisiones del equipo porque estamos cortos de tiempo, vamos de una con lo que te parezca más razonable, preguntame antes.»
- **Prompt:** «Vamos a tener que meter nosotros los tickets que faltan, me parece. El del cliente HTTP es el de Ro, no? … Hagamos nosotros la conexión de los 3 tickets, vos decidís la rama.»
- **Prompt:** «Perfecto, de paso lanzá otro agente a acomodar los tickets que están medio medio.»
- **Prompt:** «Los tenemos que cerrar antes del release sí o sí a esos que no se pueden cerrar todavía, pero primero esperemos a 59 y 60.»
- **Prompt:** «Las decisiones del equipo son nuestras principalmente, así que cerrate los que ya están.»
- **Prompt:** «Cuando salgan estos dos tickets podemos probar todo el flujo y sacar a prod, no?»

## 11. Diagramas y documentación (05/11)

- **Prompt:** «Tema diagramas, ¿los podés hacer vos? El tema prompts tenemos que armar nuestro archivo.»
- **Prompt:** «En mi caso recordá que yo tuve un tema más de gestión general y apoyo, más que tickets específicos. Solo importan los prompts, no la respuesta.»

## Estado al compartir

Los tickets #1 a #7, #10, #11, #13 a #16 y #19 están cerrados; el repositorio tiene CI en cada PR, el tablero refleja el estado real y se hicieron las pruebas de punta a punta con Azure Service Bus, Keycloak, Booking y Payment. Quedan en curso los diagramas (#8), la demo (#25) y los archivos de prompts de los demás integrantes (#9). Los PRs y el detalle técnico de cada componente están en el historial del repositorio y en `docs/`.
