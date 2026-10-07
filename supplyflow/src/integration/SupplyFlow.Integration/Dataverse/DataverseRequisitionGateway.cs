using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SupplyFlow.Integration.Dataverse;

/// <summary>
/// Dataverse access through the (thread-safe, singleton) <see cref="ServiceClient"/>.
/// Runs as an Application User; in Azure the identity is the Function's managed identity (no secret).
/// </summary>
public sealed class DataverseRequisitionGateway : IRequisitionGateway
{
    private const int StageSentToSap = 100_000_004;
    private const int StagePurchaseOrderCreated = 100_000_005;
    private const int IntegrationQueued = 100_000_001;
    private const int IntegrationSucceeded = 100_000_002;
    private const int IntegrationFailed = 100_000_003;

    private readonly ServiceClient _client;

    public DataverseRequisitionGateway(ServiceClient client)
    {
        _client = client;
    }

    public async Task<RequisitionSnapshot?> GetAsync(Guid requisitionId, CancellationToken cancellationToken)
    {
        // One round trip: header + supplier + cost center via link-entities.
        var header = new QueryExpression("fno_purchaserequisition")
        {
            ColumnSet = new ColumnSet("fno_name", "fno_stage", "fno_sappurchaseorder", "fno_needbydate", "fno_totalamount"),
            Criteria = { Conditions = { new ConditionExpression("fno_purchaserequisitionid", ConditionOperator.Equal, requisitionId) } },
        };
        var supplier = header.AddLink("account", "fno_supplierid", "accountid", JoinOperator.LeftOuter);
        supplier.EntityAlias = "s";
        supplier.Columns = new ColumnSet("name", "fno_sapvendorcode");
        var costCenter = header.AddLink("fno_costcenter", "fno_costcenterid", "fno_costcenterid", JoinOperator.LeftOuter);
        costCenter.EntityAlias = "cc";
        costCenter.Columns = new ColumnSet("fno_code");

        var result = (await _client.RetrieveMultipleAsync(header, cancellationToken)).Entities.FirstOrDefault();
        if (result is null)
        {
            return null;
        }

        var lines = new QueryExpression("fno_requisitionline")
        {
            ColumnSet = new ColumnSet("fno_name", "fno_quantity", "fno_unitprice", "fno_deliverydate", "createdon"),
            Criteria = { Conditions = { new ConditionExpression("fno_requisitionid", ConditionOperator.Equal, requisitionId) } },
            Orders = { new OrderExpression("createdon", OrderType.Ascending) },
        };
        var material = lines.AddLink("fno_material", "fno_materialid", "fno_materialid");
        material.EntityAlias = "m";
        material.Columns = new ColumnSet("fno_materialcode", "fno_unitofmeasure");

        var lineEntities = (await _client.RetrieveMultipleAsync(lines, cancellationToken)).Entities;

        return new RequisitionSnapshot(
            requisitionId,
            result.GetAttributeValue<string>("fno_name"),
            result.GetAttributeValue<OptionSetValue>("fno_stage")?.Value ?? 0,
            result.GetAttributeValue<string>("fno_sappurchaseorder"),
            Aliased<string>(result, "s.fno_sapvendorcode"),
            Aliased<string>(result, "s.name") ?? string.Empty,
            Aliased<string>(result, "cc.fno_code"),
            result.GetAttributeValue<DateTime?>("fno_needbydate"),
            result.GetAttributeValue<Money>("fno_totalamount")?.Value ?? 0m,
            lineEntities.Select((l, i) => new RequisitionLineSnapshot(
                (i + 1) * 10,
                Aliased<string>(l, "m.fno_materialcode") ?? string.Empty,
                l.GetAttributeValue<string>("fno_name"),
                Aliased<string>(l, "m.fno_unitofmeasure") ?? "UN",
                l.GetAttributeValue<decimal>("fno_quantity"),
                l.GetAttributeValue<Money>("fno_unitprice")?.Value ?? 0m,
                l.GetAttributeValue<DateTime?>("fno_deliverydate"))).ToList());
    }

    public Task MarkSentToSapAsync(Guid requisitionId, CancellationToken cancellationToken)
    {
        return _client.UpdateAsync(new Entity("fno_purchaserequisition", requisitionId)
        {
            ["fno_stage"] = new OptionSetValue(StageSentToSap),
            ["fno_integrationstatus"] = new OptionSetValue(IntegrationQueued),
            ["fno_integrationmessage"] = "Enviando ao SAP…",
        }, cancellationToken);
    }

    public Task MarkPurchaseOrderCreatedAsync(Guid requisitionId, string purchaseOrder, CancellationToken cancellationToken)
    {
        return _client.UpdateAsync(new Entity("fno_purchaserequisition", requisitionId)
        {
            ["fno_sappurchaseorder"] = purchaseOrder,
            ["fno_stage"] = new OptionSetValue(StagePurchaseOrderCreated),
            ["fno_integrationstatus"] = new OptionSetValue(IntegrationSucceeded),
            ["fno_integrationmessage"] = $"Pedido {purchaseOrder} criado no SAP em {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.",
        }, cancellationToken);
    }

    public Task MarkIntegrationFailedAsync(Guid requisitionId, string message, CancellationToken cancellationToken)
    {
        return _client.UpdateAsync(new Entity("fno_purchaserequisition", requisitionId)
        {
            ["fno_integrationstatus"] = new OptionSetValue(IntegrationFailed),
            ["fno_integrationmessage"] = message.Length > 2000 ? message[..2000] : message,
        }, cancellationToken);
    }

    public Task WriteLogAsync(IntegrationLogEntry entry, CancellationToken cancellationToken)
    {
        return _client.CreateAsync(new Entity("fno_integrationlog")
        {
            ["fno_name"] = entry.Operation,
            ["fno_requisitionid"] = new EntityReference("fno_purchaserequisition", entry.RequisitionId),
            ["fno_direction"] = new OptionSetValue(100_000_000),
            ["fno_status"] = new OptionSetValue((int)entry.Status),
            ["fno_correlationid"] = entry.CorrelationId,
            ["fno_messageid"] = entry.MessageId,
            ["fno_attempt"] = entry.Attempt,
            ["fno_durationms"] = (int)Math.Min(entry.DurationMs, int.MaxValue),
            ["fno_payload"] = entry.Payload,
            ["fno_errormessage"] = entry.Error,
        }, cancellationToken);
    }

    private static T? Aliased<T>(Entity entity, string alias)
    {
        return entity.GetAttributeValue<AliasedValue>(alias)?.Value is T value ? value : default;
    }
}
