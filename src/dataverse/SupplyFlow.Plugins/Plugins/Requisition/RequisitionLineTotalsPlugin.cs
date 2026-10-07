using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;

namespace SupplyFlow.Plugins.Plugins.Requisition;

/// <summary>
/// Keeps fno_purchaserequisition.fno_totalamount in sync with its lines.
/// </summary>
/// <remarks>
/// Registration: fno_requisitionline | Create, Update (filtering: fno_quantity, fno_unitprice, fno_requisitionid, statecode), Delete
/// | PostOperation | Sync. PreImage (Update, Delete): fno_requisitionid.
/// <para>
/// Synchronous PostOperation runs inside the transaction, so the header total is never out of date and
/// any failure rolls back the line change. A rollup column was considered (ADR 0002) but rollups are
/// recalculated asynchronously (up to 12h), which is unacceptable for an approval threshold.
/// </para>
/// </remarks>
public sealed class RequisitionLineTotalsPlugin : PluginBase
{
    public RequisitionLineTotalsPlugin()
        : this(null, null)
    {
    }

    /// <summary>Constructor used by Dataverse when the step has unsecure/secure configuration.</summary>
    public RequisitionLineTotalsPlugin(string? unsecureConfiguration, string? secureConfiguration)
        : base(unsecureConfiguration, secureConfiguration)
    {
    }

    protected override void ExecuteDataversePlugin(LocalPluginContext context)
    {
        context.EnsureRegistration(
            Schema.RequisitionLine.EntityName, PluginStage.PostOperation, MessageNames.Create, MessageNames.Update, MessageNames.Delete);

        // Lines removed by the cascade of a requisition delete: the header is going away, nothing to recalculate.
        if (context.IsCascadeDeleteFrom(Schema.Requisition.EntityName))
        {
            context.Trace("Cascade delete from requisition – skipping.");
            return;
        }

        var repository = new RequisitionRepository(context.SystemService);
        foreach (var requisitionId in GetAffectedRequisitions(context))
        {
            if (context.MessageName == MessageNames.Delete)
            {
                EnsureEditable(repository, requisitionId);
            }

            var total = repository.SumLineTotals(requisitionId);
            context.SystemService.Update(new Entity(Schema.Requisition.EntityName, requisitionId)
            {
                [Schema.Requisition.TotalAmount] = new Money(total),
            });

            context.Trace($"Requisition {requisitionId} total recalculated: {total:N2}");
        }
    }

    private static IEnumerable<Guid> GetAffectedRequisitions(LocalPluginContext context)
    {
        var ids = new HashSet<Guid>();

        if (context.MessageName != MessageNames.Delete)
        {
            var current = context.GetTarget().GetAttributeValue<EntityReference>(Schema.RequisitionLine.RequisitionId);
            if (current is not null)
            {
                ids.Add(current.Id);
            }
        }

        // Update that moved the line to another requisition, or Delete: the previous parent must be recalculated too.
        var previous = context.GetPreImage()?.GetAttributeValue<EntityReference>(Schema.RequisitionLine.RequisitionId);
        if (previous is not null)
        {
            ids.Add(previous.Id);
        }

        return ids;
    }

    private static void EnsureEditable(RequisitionRepository repository, Guid requisitionId)
    {
        var stage = repository.GetRequisition(requisitionId, Schema.Requisition.Stage)
            .GetChoice<RequisitionStage>(Schema.Requisition.Stage) ?? RequisitionStage.Draft;

        if (!RequisitionStateMachine.IsEditable(stage))
        {
            throw new InvalidPluginExecutionException(
                $"Itens não podem ser excluídos com a requisição em \"{RequisitionStateMachine.Describe(stage)}\".");
        }
    }
}
