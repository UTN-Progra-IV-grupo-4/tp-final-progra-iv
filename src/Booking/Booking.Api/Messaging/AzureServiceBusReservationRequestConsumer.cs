using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

internal sealed class AzureServiceBusReservationRequestConsumer(
    IOptions<BrokerOptions> brokerOptions,
    ReservationRequestMessageHandler handler,
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
                "Azure Service Bus reservation consumer is disabled. F06 must be completed before enabling it.");
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
        }
        catch
        {
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
    }

    public ValueTask DisposeAsync() => DisposeResourcesAsync();

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var delivery = new ReservationRequestDelivery(
            args.Message.Body,
            args.Message.MessageId,
            args.Message.CorrelationId,
            args.Message.Subject);

        await handler.HandleAsync(delivery, args.CancellationToken);

        // F05 intentionally does not settle messages. Complete, DeadLetter and retry
        // decisions are implemented and verified in F06 before enabling this consumer.
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Azure Service Bus processor failed. Source: {ErrorSource}; entity: {EntityPath}.",
            args.ErrorSource,
            args.EntityPath);

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
