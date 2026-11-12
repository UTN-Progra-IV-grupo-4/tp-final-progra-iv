using Azure.Messaging.ServiceBus;
using Booking.Api.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

// Adaptador de Azure Service Bus: recibe los mensajes y delega en el procesador. Consume de forma
// defensiva: AutoCompleteMessages está desactivado, así el mensaje solo se completa o se manda a
// dead-letter explícitamente tras decidir su resultado; si el procesamiento lanza una excepción,
// queda sin completar y el broker lo reentrega.
internal sealed class AzureServiceBusReservationRequestConsumer(
    IOptions<BrokerOptions> brokerOptions,
    ReservationRequestMessageProcessor messageProcessor,
    ILogger<AzureServiceBusReservationRequestConsumer> logger) :
    IReservationRequestConsumer,
    IAsyncDisposable
{
    private readonly BrokerOptions options = brokerOptions.Value;
    private ServiceBusClient? client;
    private ServiceBusProcessor? processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.AzureServiceBus.Enabled)
        {
            logger.LogInformation(
                "Azure Service Bus reservation consumer is disabled by configuration.");
            return;
        }

        if (processor is not null)
        {
            return;
        }

        try
        {
            client = new ServiceBusClient(options.AzureServiceBus.ConnectionString);
            processor = client.CreateProcessor(
                options.AzureServiceBus.TopicName,
                options.AzureServiceBus.SubscriptionName,
                new ServiceBusProcessorOptions
                {
                    // El mensaje se completa a mano, solo cuando Booking ya persistió el resultado.
                    AutoCompleteMessages = false
                });

            processor.ProcessMessageAsync += ProcessMessageAsync;
            processor.ProcessErrorAsync += ProcessErrorAsync;

            await processor.StartProcessingAsync(cancellationToken);

            logger.LogInformation(
                "Azure Service Bus reservation consumer started. Topic: {TopicName}; Subscription: {SubscriptionName}.",
                options.AzureServiceBus.TopicName,
                options.AzureServiceBus.SubscriptionName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Azure Service Bus reservation consumer failed to start. ExceptionType: {ExceptionType}.",
                exception.GetType().FullName);
            await DisposeResourcesAsync();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (processor is null)
        {
            return;
        }

        try
        {
            await processor.StopProcessingAsync(cancellationToken);
        }
        finally
        {
            await DisposeResourcesAsync();
        }

        logger.LogInformation("Azure Service Bus reservation consumer stopped.");
    }

    public ValueTask DisposeAsync() => DisposeResourcesAsync();

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        WarnIfPublishedFromAnotherHost(args.Message);

        var delivery = new ReservationRequestDelivery(
            args.Message.Body,
            args.Message.MessageId,
            args.Message.CorrelationId,
            args.Message.Subject,
            args.Message.DeliveryCount);
        var settlement = new AzureServiceBusReservationRequestMessageSettlement(args);

        await messageProcessor.ProcessAsync(
            delivery,
            settlement,
            args.CancellationToken);
    }

    // Diagnostic only. It never changes how the message is processed, and it is in the broker adapter
    // on purpose: it is about how the message arrived, not about the reservation.
    private void WarnIfPublishedFromAnotherHost(ServiceBusReceivedMessage message)
    {
        var publisherHost = ReservationMessageOrigin.ReadPublisherHost(message.ApplicationProperties);
        if (!ReservationMessageOrigin.IsPublishedFromAnotherHost(publisherHost, Environment.MachineName))
        {
            return;
        }

        logger.LogWarning(
            BookingAuditEvents.ReservationMessageFromOtherHost,
            "Reservation message was published from another host: a consumer of the same subscription running elsewhere may be competing for the messages. Service: {Service}; MessageId: {MessageId}; PublisherHost: {PublisherHost}; ConsumerHost: {ConsumerHost}; Subscription: {SubscriptionName}.",
            BookingAuditEvents.ServiceName,
            message.MessageId,
            publisherHost,
            Environment.MachineName,
            options.AzureServiceBus.SubscriptionName);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            "Azure Service Bus processor failed. Source: {ErrorSource}; Entity: {EntityPath}; ExceptionType: {ExceptionType}.",
            args.ErrorSource,
            args.EntityPath,
            args.Exception.GetType().FullName);

        return Task.CompletedTask;
    }

    private async ValueTask DisposeResourcesAsync()
    {
        var currentProcessor = processor;
        var currentClient = client;
        processor = null;
        client = null;

        if (currentProcessor is not null)
        {
            currentProcessor.ProcessMessageAsync -= ProcessMessageAsync;
            currentProcessor.ProcessErrorAsync -= ProcessErrorAsync;
            await currentProcessor.DisposeAsync();
        }

        if (currentClient is not null)
        {
            await currentClient.DisposeAsync();
        }
    }
}
