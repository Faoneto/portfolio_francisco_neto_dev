using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SupplyFlow.Integration.Dataverse;

public sealed record RequisitionLineSnapshot(
    int LineNumber,
    string MaterialCode,
    string Description,
    string UnitOfMeasure,
    decimal Quantity,
    decimal UnitPrice,
    DateTime? DeliveryDate);

public sealed record RequisitionSnapshot(
    Guid Id,
    string Number,
    int Stage,
    string? SapPurchaseOrder,
    string? SupplierSapCode,
    string SupplierName,
    string? CostCenterCode,
    DateTime? NeedByDate,
    decimal TotalAmount,
    IReadOnlyList<RequisitionLineSnapshot> Lines);

public enum IntegrationLogStatus
{
    Success = 100_000_000,
    Failed = 100_000_001,
    Skipped = 100_000_002,
}

public sealed record IntegrationLogEntry(
    Guid RequisitionId,
    string Operation,
    IntegrationLogStatus Status,
    string CorrelationId,
    string MessageId,
    int Attempt,
    long DurationMs,
    string? Payload,
    string? Error);

/// <summary>Dataverse operations needed by the SAP integration (abstracted for unit tests).</summary>
public interface IRequisitionGateway
{
    Task<RequisitionSnapshot?> GetAsync(Guid requisitionId, CancellationToken cancellationToken);

    Task MarkSentToSapAsync(Guid requisitionId, CancellationToken cancellationToken);

    Task MarkPurchaseOrderCreatedAsync(Guid requisitionId, string purchaseOrder, CancellationToken cancellationToken);

    Task MarkIntegrationFailedAsync(Guid requisitionId, string message, CancellationToken cancellationToken);

    Task WriteLogAsync(IntegrationLogEntry entry, CancellationToken cancellationToken);
}
