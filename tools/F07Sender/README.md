# Emisor controlado de F07

Herramienta auxiliar para publicar mensajes de prueba en el topic de Azure Service Bus durante la validación controlada de T18-F07.

No contiene secretos. Los escenarios `new` e `invalid` solo publican mensajes; `peek-active` y `peek-dlq` crean receivers de lectura, y `cleanup-dlq` liquida únicamente los mensajes de prueba indicados. Requiere que las variables se carguen en la sesión local:

```powershell
$env:F07_ASB_CONNECTION_STRING = Read-Host "Connection string" -MaskInput
$env:F07_ASB_TOPIC = "grupo4-booking-topic"
```

Ejemplos de escenarios:

```powershell
dotnet run --project .\tools\F07Sender -- new
dotnet run --project .\tools\F07Sender -- invalid
```

Para repetir un mensaje o provocar un `RequestId` conflictivo se conservan los identificadores en variables `F07_REQUEST_ID`, `F07_EVENT_ID`, `F07_SEAT_ID` y `F07_CORRELATION_ID`. El emisor asigna `MessageId = RequestId` y no imprime la connection string.

La ejecución contra el topic compartido requiere una ventana coordinada y la activación temporal del consumer en una instancia aislada de Booking.Api.

Para inspeccionar sin consumir mensajes antes y después de una prueba, definir también `F07_ASB_SUBSCRIPTION` y ejecutar `peek-active` o `peek-dlq`. El emisor muestra solo `MessageId` y `SequenceNumber`, nunca el body.

`cleanup-dlq` elimina únicamente los `MessageId` enumerados en `F07_CLEANUP_MESSAGE_IDS` (separados por comas). Los mensajes de otros identificadores se abandonan sin completarse. Usarlo solo después de inspeccionar la cola y confirmar que esos IDs pertenecen a la prueba actual.
