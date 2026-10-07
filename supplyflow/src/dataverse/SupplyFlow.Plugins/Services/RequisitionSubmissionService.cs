using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;

namespace SupplyFlow.Plugins.Services;

/// <summary>
/// Validates a requisition being submitted for approval and resolves the approval level.
/// </summary>
public sealed class RequisitionSubmissionService
{
    private readonly IOrganizationService _service;
    private readonly Func<DateTime> _utcNow;

    public RequisitionSubmissionService(IOrganizationService service, Func<DateTime>? utcNow = null)
    {
        _service = service;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Validates <paramref name="requisition"/> (the merged future state) and writes the approval
    /// outcome into <paramref name="target"/>. Throws a single message listing every problem found,
    /// so the user can fix everything in one round trip.
    /// </summary>
    public ApprovalDecision PrepareSubmission(Entity requisition, Entity target)
    {
        var errors = new List<string>();
        var repository = new RequisitionRepository(_service);

        ValidateSupplier(requisition, errors);

        var needBy = requisition.GetAttributeValue<DateTime?>(Schema.Requisition.NeedByDate);
        if (needBy is null)
        {
            errors.Add("Informe a data de necessidade.");
        }
        else if (needBy.Value.Date < _utcNow().Date)
        {
            errors.Add("A data de necessidade não pode estar no passado.");
        }

        if (string.IsNullOrWhiteSpace(requisition.GetAttributeValue<string>(Schema.Requisition.Justification)))
        {
            errors.Add("Informe a justificativa da compra.");
        }

        var lines = repository.GetLineSummary(requisition.Id);
        if (lines.Count == 0 || lines.Total <= 0)
        {
            errors.Add("Adicione ao menos um item com valor maior que zero.");
        }

        var costCenter = requisition.GetAttributeValue<EntityReference>(Schema.Requisition.CostCenterId);
        if (costCenter is null)
        {
            errors.Add("Informe o centro de custo.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidPluginExecutionException(
                "A requisição não pode ser enviada para aprovação:\n- " + string.Join("\n- ", errors));
        }

        var budget = new BudgetService(_service).GetSnapshot(costCenter!.Id);
        var decision = ApprovalPolicyResolver.Resolve(lines.Total, lines.MaterialGroups, repository.GetActivePolicies(), budget.Available);

        target[Schema.Requisition.TotalAmount] = new Money(lines.Total);
        target.SetChoice(Schema.Requisition.ApprovalLevel, decision.Level);
        target[Schema.Requisition.IsOverBudget] = decision.IsOverBudget;
        target[Schema.Requisition.ApprovalNotes] = string.Join(Environment.NewLine, decision.Reasons);
        target[Schema.Requisition.SubmittedOn] = _utcNow();

        return decision;
    }

    private void ValidateSupplier(Entity requisition, List<string> errors)
    {
        var supplierRef = requisition.GetAttributeValue<EntityReference>(Schema.Requisition.SupplierId);
        if (supplierRef is null)
        {
            errors.Add("Informe o fornecedor.");
            return;
        }

        var supplier = _service.Retrieve(
            Schema.Account.EntityName,
            supplierRef.Id,
            new ColumnSet(Schema.Account.Name, Schema.Account.SupplierStatus, Schema.Account.StateCode));

        var status = supplier.GetChoice<SupplierStatus>(Schema.Account.SupplierStatus);
        var isActive = (supplier.GetAttributeValue<OptionSetValue>(Schema.Account.StateCode)?.Value ?? 0) == 0;
        if (status != SupplierStatus.Approved || !isActive)
        {
            errors.Add($"O fornecedor \"{supplier.GetAttributeValue<string>(Schema.Account.Name)}\" não está homologado.");
        }
    }
}
