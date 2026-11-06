# Historial de prompts - Bruno Gerardo García

**Rol:** desarrollo de la estructura base de Availability (#12) y de la consulta pública de eventos y asientos (#13), con apoyo de IA y revisión del equipo.

**Herramienta:** Codex, con contexto de una conversación previa de ChatGPT.

**Período:** 27/10/2026 al 05/11/2026.

**Alcance:** reuní los pedidos más importantes que hice para implementar, corregir y verificar mi parte de Availability. También incluí la coordinación con otros componentes cuando afectaba ese trabajo.

## Cómo leer el registro

- Las citas son mensajes reales o fragmentos de mis mensajes, agrupados por tema. Mantuve la redacción original.
- Seleccioné los pedidos relacionados con el desarrollo. Dejé afuera repeticiones, confirmaciones breves, navegación y comandos rutinarios de Git.
- Cuando un mensaje incluía tickets o comentarios de otras personas, los resumí en el contexto para distinguirlos de mis propios pedidos.
- Los apartados **Contexto** y **Resultado** los agregué después para explicar cada etapa; no forman parte de los prompts originales.
- Este archivo es una selección del historial, no una exportación completa del chat.

## Prompts seleccionados

### 1. Creación de los proyectos por capas

**Contexto:** Inicio del #12; Availability.Web ya existía dentro de la solución.

**Prompt (mensaje completo o fragmento):**

> una consulta, tu ahora puedes ver y modificar la carpeta donde estan los archivos del tp. puedes tu crear esas `Availability.Domain`, `Availability.Application` y `Availability.Infrastructure`  ?

**Resultado:** La IA creó los tres proyectos, los incorporó a la solución y configuró las referencias entre capas. Domain quedó sin dependencias de otros proyectos.

### 2. Implementación del alcance del #12

**Contexto:** Se habían aportado el enunciado del profesor y la decisión de usar PostgreSQL con EF Core. El compose del #3 aún estaba pendiente.

**Prompt (mensaje completo o fragmento):**

> bien comenzemos con todo lo que podamos hacer en base a lo q pide el pdf y nuestro punto 12. si quedan cosas q no sabemos todavia como por ejemplo todo de la base de datos del 3, lo dejaremos de modo claro a completar con comentarios y etc verdad?

**Resultado:** Se generaron las entidades Event, Seat y ReservationRequest, el contexto y los mapeos de EF Core, la migración inicial y documentación de configuración y pendientes. Se mantuvo separada la solicitud de Availability de la reserva administrada por Booking.

### 3. Corrección de las herramientas de EF Core

**Contexto:** El mensaje adjuntaba un error de dotnet ef al usar Availability.Web como startup-project y observaciones de otra IA. Se omiten aquí la salida completa y el texto de terceros.

**Prompt (mensaje completo o fragmento):**

> mira voy a pasarte un error q me dio el gitbash y algo q me dijo otra ia. comprueba y haz cambios si ves necesarios:

**Resultado:** Se reprodujo el error y se agregó Microsoft.EntityFrameworkCore.Design a Web. Se verificaron las variantes con Infrastructure y con Web como inicio; ambas confirmaron que no había cambios del modelo pendientes de migrar.

### 4. Conexión con PostgreSQL del compose

**Contexto:** Después del merge del #3 se retomó la configuración real de Availability. El mensaje incluía una salida de Git, omitida por no ser parte del pedido técnico.

**Prompt (mensaje completo o fragmento):**

> perfecto como continuamos ahora? habria que corregir los datos y codigo que hicimos como para rellenar, ahora utilizando lo nuevo mergeado verdad:

**Resultado:** Se alineó EF Core con PostgreSQL 16, se aplicó la migración con el usuario de Availability y se probaron los datos y las restricciones. La configuración sensible quedó local y no forma parte de este historial.

### 5. Correcciones de la revisión del PR del #12

**Contexto:** La revisión del equipo pidió alinear nombres SQL, evitar un manifiesto duplicado, mejorar la documentación y revisar la coordinación con el #35.

**Prompt (mensaje completo o fragmento):**

> si no es asi, haz los cambios en el codigo que pide y luego detallame que responderle en cada anotacion. insisto en que si puedo ayudarte con algo me lo digas.

**Resultado:** Se adoptó snake_case con mapeos explícitos, se agregó una migración de renombrado conservando datos y se incorporaron tests de PostgreSQL con Testcontainers. Se eliminó el manifiesto duplicado y se corrigió la documentación. El mapper del #35 quedó como coordinación, no como implementación propia de este pedido.

### 6. Ajustes de organización y pruebas

**Contexto:** El mensaje adjuntaba observaciones menores del equipo: ubicación del proyecto de tests, collection compartida, limpieza de tablas y separación de la nota de prompts respecto de la guía técnica.

**Prompt (mensaje completo o fragmento):**

> bien pero en cuanto a esto modificamos el codigo?:

**Resultado:** Los tests se trasladaron a tests/Availability/Availability.Tests, la fixture pasó a una colección compartida y la limpieza tomó los nombres del modelo de EF Core. La automatización de CI quedó en el ticket del equipo correspondiente.

### 7. Análisis de requisitos y dependencias del #13

**Contexto:** Se aportó la versión actualizada del #13 y luego los tickets #21 y #11. El pedido separó explícitamente análisis e implementación.

**Prompt (mensaje completo o fragmento):**

> quiero q sin modificar codigo me digas si necesitas algo de info de otros # o si entiendes lo q hay q hacer.

**Resultado:** Se revisó el contrato de asientos ocupados y se identificó a Booking como fuente de ocupación. Se distinguió el acceso anónimo del visitante de la autenticación de servicio usada por Availability para consultar Booking.

### 8. Construcción de la lista de eventos y el mapa

**Contexto:** El mensaje incluía la consigna del #13: selección individual, ocupados en gris, mock inicial y seed ampliado con generate_series. Resumí el ticket en este contexto; la cita de abajo corresponde a mi pedido.

**Prompt (mensaje completo o fragmento):**

> nuevo cambio en el #13 analiza lo que hay que hacer y comencemos:

**Resultado:** Se implementaron interfaces y consultas en Application, catálogo en memoria y EF Core en Infrastructure, y controladores y vistas MVC públicas. El seed pasó a veinte asientos por evento conservando los seis IDs originales. Se validaron selecciones inválidas y disponibilidad desconocida; seleccionar todavía no enviaba una reserva.

### 9. Cliente HTTP del endpoint de ocupación

**Contexto:** El #21 se había integrado, pero faltaba el proveedor real de token del #11. El mensaje también aportaba la decisión sobre consistencia eventual del #8.

**Prompt (mensaje completo o fragmento):**

> que opinas quieres revisar a ver si ves los nuevos mergeos y luego comenzamos a imlementar lo que necesitamos del #21.

**Resultado:** Se implementó el cliente HTTP del contrato de ocupación y sus pruebas controladas. Errores HTTP, timeouts o respuestas inválidas mantuvieron la disponibilidad sin confirmar. Sin token no se enviaba una llamada anónima. La autenticación real quedó pendiente para el trabajo compartido del #11.

### 10. Comprobación de entidades y capas compartidas

**Contexto:** Se recibió una consulta de coordinación sobre posibles objetos duplicados entre las ramas del #12 y el #15.

**Prompt (mensaje completo o fragmento):**

> recuerdas el problema que habia con el issue con la otra compañera? bueno me mando lo siguiente; que podemos hacer como y que le respondo:

**Resultado:** La revisión encontró una sola ReservationRequest en Availability.Domain, el handler en Application y la persistencia en Infrastructure. No se creó otra entidad ni se movieron los controladores fuera de Web; se indicó contrastar las ramas con develop actualizado.

### 11. Verificación de la integración realizada por el equipo

**Contexto:** Tras nuevos mergeos, se pidió comprobar si el cliente compartido de ocupación (#13), estado (#16) y token (#11) ya estaba en develop. La rama local tenía un merge en conflicto.

**Prompt (mensaje completo o fragmento):**

> lo unico que quiero es q vos revises si se actualizo ese cambio o no. como puedo hacer eso. en git actualize develop pero por modificaciones me da error mi branch. puedes ver si esta todo bien en develop?

**Resultado:** Se inspeccionó develop en una copia temporal, sin resolver ni cancelar el merge local. Se confirmó el registro HTTP común, el BearerTokenHandler y el proveedor singleton con caché. La solución compiló y pasaron 195 tests de Availability y 24 de ServiceAuthentication. Esa integración la había realizado el equipo en los PR #51 y #56; en esta etapa pedí verificarla, no implementarla de nuevo.

## Cierre del registro

Usé la IA para generar código, consultar dudas y revisar cambios durante el desarrollo de #12 y #13. Fui compartiendo los requisitos y las devoluciones del equipo para ajustar la implementación. Las correcciones e integraciones de otros integrantes también forman parte del resultado del proyecto; este historial describe mis pedidos y no todo el trabajo del grupo.

El detalle de los cambios se puede consultar en los PR #38 y #47. La integración compartida con autenticación se incorporó en los PR del equipo #51 y #56.
