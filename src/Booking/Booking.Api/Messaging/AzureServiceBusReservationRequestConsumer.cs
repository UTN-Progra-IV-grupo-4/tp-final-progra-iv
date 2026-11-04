using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

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
