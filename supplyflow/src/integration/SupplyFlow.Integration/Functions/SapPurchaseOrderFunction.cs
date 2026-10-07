using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SupplyFlow.Integration.Processing;

namespace SupplyFlow.Integration.Functions;

/// <summary>
/// Consumes the Service Bus queue fed by the Dataverse service endpoint step
/// "SupplyFlow: requisition approved → Service Bus" and settles the message explicitly.
/// </summary>
public sealed class SapPurchaseOrderFunction
{
    private readonly RequisitionIntegrationProcessor _processor;
    private readonly ILogger<SapPurchaseOrderFunction> _logger;

    public SapPurchaseOrderFunction(RequisitionIntegrationProcessor processor, ILogger<SapPurchaseOrderFunction> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [Function(nameof(SapPurchaseOrderFunction))]
    public async Task Run(
        [ServiceBusTrigger("%SapQueueName%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)] ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken cancellationToken)
    {
        var result = await _processor.ProcessAsync(message.Body.ToString(), message.MessageId, message.DeliveryCount, cancellationToken);
        _logger.LogInformation("Message {MessageId} -> {Disposition}: {Reason}", message.MessageId, result.Disposition, result.Reason);

        switch (result.Disposition)
        {
            case MessageDisposition.Complete:
                await actions.CompleteMessageAsync(message, cancellationToken);
                break;
            case MessageDisposition.Retry:
                await actions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
                break;
            case MessageDisposition.DeadLetter:
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "SupplyFlow", deadLetterErrorDescription: Truncate(result.Reason), cancellationToken: cancellationToken);
                break;
        }
    }

    private static string Truncate(string value) => value.Length <= 4096 ? value : value[..4096];
}
