using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SupplyFlow.Integration.Dataverse;
using SupplyFlow.Integration.Sap;

namespace SupplyFlow.Integration.Processing;

public enum MessageDisposition
{
    /// <summary>Done (processed or intentionally ignored) – remove from the queue.</summary>
    Complete,

    /// <summary>Transient failure – abandon so Service Bus redelivers with back-off.</summary>
    Retry,

    /// <summary>Poison/business failure – move to the dead-letter queue for investigation.</summary>
    DeadLetter,
}

public sealed record ProcessingResult(MessageDisposition Disposition, string Reason);

/// <summary>
/// Turns an "approved requisition" Dataverse event into a SAP purchase order.
/// Free of Azure Functions types so the whole decision table is unit-testable.
/// </summary>
/// <remarks>
/// Idempotency: the function's own updates (SentToSap / PurchaseOrderCreated) re-trigger the service
/// endpoint step; they are ignored because the post-image stage is not Approved. Redeliveries after a
/// crash are safe because a requisition that already has fno_sappurchaseorder is skipped and the SAP call
/// carries an Idempotency-Key.
/// </remarks>
public sealed class RequisitionIntegrationProcessor
{
    public const string RequisitionEntity = "fno_purchaserequisition";
    public const int StageApproved = 100_000_002;
    public const int StageSentToSap = 100_000_004;

    private static readonly JsonSerializerOptions PayloadJson = new() { WriteIndented = false };

    private readonly IRequisitionGateway _gateway;
    private readonly ISapClient _sap;
    private readonly SapOptions _options;
    private readonly ILogger<RequisitionIntegrationProcessor> _logger;

    public RequisitionIntegrationProcessor(
        IRequisitionGateway gateway, ISapClient sap, IOptions<SapOptions> options, ILogger<RequisitionIntegrationProcessor> logger)
    {
        _gateway = gateway;
        _sap = sap;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProcessingResult> ProcessAsync(string body, string messageId, int deliveryCount, CancellationToken cancellationToken)
    {
        DataverseEvent evt;
        try
        {
            evt = RemoteContextParser.Parse(body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or System.Collections.Generic.KeyNotFoundException)
        {
            _logger.LogError(ex, "Invalid RemoteExecutionContext payload in message {MessageId}", messageId);
            return new ProcessingResult(MessageDisposition.DeadLetter, "Payload inválido.");
        }

        if (!string.Equals(evt.PrimaryEntityName, RequisitionEntity, StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessingResult(MessageDisposition.DeadLetter, $"Entidade inesperada: {evt.PrimaryEntityName}.");
        }

        var correlationId = evt.CorrelationId.ToString();
        using var scope = _logger.BeginScope(new System.Collections.Generic.Dictionary<string, object>
        {
            ["RequisitionId"] = evt.PrimaryEntityId,
            ["CorrelationId"] = correlationId,
            ["MessageId"] = messageId,
            ["DeliveryCount"] = deliveryCount,
        });

        var eventStage = evt.Get<int>("fno_stage");
        if (eventStage != StageApproved)
        {
            return new ProcessingResult(MessageDisposition.Complete, $"Ignorado: etapa {eventStage} não é Aprovada.");
        }

        var requisition = await _gateway.GetAsync(evt.PrimaryEntityId, cancellationToken);
        if (requisition is null)
        {
            return new ProcessingResult(MessageDisposition.Complete, "Requisição não encontrada (excluída).");
        }

        if (!string.IsNullOrEmpty(requisition.SapPurchaseOrder))
        {
            await LogAsync(requisition, IntegrationLogStatus.Skipped, correlationId, messageId, deliveryCount, 0, null,
                $"Pedido {requisition.SapPurchaseOrder} já existe.", cancellationToken);
            return new ProcessingResult(MessageDisposition.Complete, "Idempotência: pedido já criado.");
        }

        if (requisition.Stage is not (StageApproved or StageSentToSap))
        {
            // E.g. cancelled between approval and processing: the event is stale.
            return new ProcessingResult(MessageDisposition.Complete, $"Ignorado: etapa atual {requisition.Stage}.");
        }

        var (order, errors) = SapPurchaseOrderMapper.Map(requisition, _options);
        if (order is null)
        {
            var message = "Dados incompletos para o SAP: " + string.Join(" ", errors);
            await FailAsync(requisition, message, correlationId, messageId, deliveryCount, 0, null, cancellationToken);
            return new ProcessingResult(MessageDisposition.DeadLetter, message);
        }

        var payload = JsonSerializer.Serialize(order, PayloadJson);
        await _gateway.MarkSentToSapAsync(requisition.Id, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _sap.CreatePurchaseOrderAsync(order, correlationId, cancellationToken);
            await _gateway.MarkPurchaseOrderCreatedAsync(requisition.Id, response.PurchaseOrder, cancellationToken);
            await LogAsync(requisition, IntegrationLogStatus.Success, correlationId, messageId, deliveryCount, stopwatch.ElapsedMilliseconds,
                payload, null, cancellationToken);

            _logger.LogInformation("Requisition {Number} -> SAP PO {PurchaseOrder} in {Elapsed} ms",
                requisition.Number, response.PurchaseOrder, stopwatch.ElapsedMilliseconds);
            return new ProcessingResult(MessageDisposition.Complete, $"Pedido {response.PurchaseOrder} criado.");
        }
        catch (SapBusinessException ex)
        {
            await FailAsync(requisition, ex.Message, correlationId, messageId, deliveryCount, stopwatch.ElapsedMilliseconds, payload, cancellationToken);
            return new ProcessingResult(MessageDisposition.DeadLetter, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var lastAttempt = deliveryCount >= _options.MaxDeliveryCount;
            var message = $"Falha temporária ao chamar o SAP (tentativa {deliveryCount}/{_options.MaxDeliveryCount}): {ex.Message}";
            _logger.LogWarning(ex, "Transient SAP failure for {Number}", requisition.Number);

            if (lastAttempt)
            {
                await FailAsync(requisition, message, correlationId, messageId, deliveryCount, stopwatch.ElapsedMilliseconds, payload, cancellationToken);
                return new ProcessingResult(MessageDisposition.DeadLetter, message);
            }

            await LogAsync(requisition, IntegrationLogStatus.Failed, correlationId, messageId, deliveryCount, stopwatch.ElapsedMilliseconds,
                payload, message, cancellationToken);
            return new ProcessingResult(MessageDisposition.Retry, message);
        }
    }

    private async Task FailAsync(
        RequisitionSnapshot requisition, string message, string correlationId, string messageId, int attempt, long durationMs, string? payload,
        CancellationToken cancellationToken)
    {
        _logger.LogError("SAP integration failed for {Number}: {Message}", requisition.Number, message);
        await _gateway.MarkIntegrationFailedAsync(requisition.Id, message, cancellationToken);
        await LogAsync(requisition, IntegrationLogStatus.Failed, correlationId, messageId, attempt, durationMs, payload, message, cancellationToken);
    }

    private Task LogAsync(
        RequisitionSnapshot requisition, IntegrationLogStatus status, string correlationId, string messageId, int attempt, long durationMs,
        string? payload, string? error, CancellationToken cancellationToken)
    {
        return _gateway.WriteLogAsync(
            new IntegrationLogEntry(requisition.Id, $"SAP PO – {requisition.Number}", status, correlationId, messageId, attempt, durationMs, payload, error),
            cancellationToken);
    }
}
