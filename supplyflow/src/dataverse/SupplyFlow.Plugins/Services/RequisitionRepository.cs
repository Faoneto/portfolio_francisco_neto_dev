using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;

namespace SupplyFlow.Plugins.Services;

public sealed record LineSummary(int Count, decimal Total, IReadOnlyCollection<MaterialGroup> MaterialGroups);

/// <summary>Data access for requisitions, lines and related reference data.</summary>
public sealed class RequisitionRepository
{
    private readonly IOrganizationService _service;

    public RequisitionRepository(IOrganizationService service)
    {
        _service = service;
    }

    public Entity GetRequisition(Guid requisitionId, params string[] columns)
    {
        return _service.Retrieve(Schema.Requisition.EntityName, requisitionId, new ColumnSet(columns));
    }

    public LineSummary GetLineSummary(Guid requisitionId)
    {
        var query = new QueryExpression(Schema.RequisitionLine.EntityName)
        {
            ColumnSet = new ColumnSet(Schema.RequisitionLine.LineTotal, Schema.RequisitionLine.MaterialGroup),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(Schema.RequisitionLine.RequisitionId, ConditionOperator.Equal, requisitionId),
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                },
            },
        };

        var lines = _service.RetrieveMultiple(query).Entities;
        var groups = lines
            .Select(l => l.GetChoice<MaterialGroup>(Schema.RequisitionLine.MaterialGroup))
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();

        return new LineSummary(lines.Count, lines.Sum(l => l.GetMoney(Schema.RequisitionLine.LineTotal)), groups);
    }

    /// <summary>Server-side SUM of line totals (FetchXML aggregate – avoids paging through lines).</summary>
    public decimal SumLineTotals(Guid requisitionId)
    {
        var fetchXml = $@"
<fetch aggregate='true'>
  <entity name='{Schema.RequisitionLine.EntityName}'>
    <attribute name='{Schema.RequisitionLine.LineTotal}' alias='total' aggregate='sum' />
    <filter>
      <condition attribute='{Schema.RequisitionLine.RequisitionId}' operator='eq' value='{requisitionId}' />
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
  </entity>
</fetch>";

        var result = _service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities.FirstOrDefault();
        var value = result?.GetAttributeValue<AliasedValue>("total")?.Value;
        return value switch
        {
            Money money => money.Value,
            decimal d => d,
            _ => 0m,
        };
    }

    public IReadOnlyList<ApprovalPolicyRule> GetActivePolicies()
    {
        var query = new QueryExpression(Schema.ApprovalPolicy.EntityName)
        {
            ColumnSet = new ColumnSet(
                Schema.ApprovalPolicy.Level,
                Schema.ApprovalPolicy.MinAmount,
                Schema.ApprovalPolicy.MaterialGroup),
            Criteria = { Conditions = { new ConditionExpression(Schema.ApprovalPolicy.StateCode, ConditionOperator.Equal, 0) } },
        };

        return _service.RetrieveMultiple(query).Entities
            .Where(p => p.Contains(Schema.ApprovalPolicy.Level))
            .Select(p => new ApprovalPolicyRule(
                p.GetChoice<ApprovalLevel>(Schema.ApprovalPolicy.Level)!.Value,
                p.GetMoney(Schema.ApprovalPolicy.MinAmount),
                p.GetChoice<MaterialGroup>(Schema.ApprovalPolicy.MaterialGroup)))
            .ToList();
    }

    /// <summary>Checks the supplier x material N:N (approved materials per supplier).</summary>
    public bool IsMaterialApprovedForSupplier(Guid supplierId, Guid materialId)
    {
        var query = new QueryExpression(Schema.SupplierMaterial.IntersectEntityName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(Schema.SupplierMaterial.AccountId, ConditionOperator.Equal, supplierId),
                    new ConditionExpression(Schema.SupplierMaterial.MaterialId, ConditionOperator.Equal, materialId),
                },
            },
        };

        return _service.RetrieveMultiple(query).Entities.Count > 0;
    }
}
