# Emisor controlado de F07

Herramienta auxiliar para publicar mensajes de prueba en el topic de Azure Service Bus durante la validación controlada de T18-F07.

No contiene secretos, no crea receivers y no consume la subscription. Requiere que las variables se carguen en la sesión local:

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
