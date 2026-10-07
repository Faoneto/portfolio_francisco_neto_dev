using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;

namespace SupplyFlow.Plugins.CustomApis;

/// <summary>
/// Implementation of the bound Custom API <c>fno_SubmitRequisition</c>.
/// </summary>
/// <remarks>
/// Custom API: fno_SubmitRequisition | Binding: Entity (fno_purchaserequisition) | Action (IsFunction = false).
/// Outputs: ApprovalLevel (Integer), IsOverBudget (Boolean), Message (String).
/// <para>
/// Why a Custom API instead of letting the client set fno_stage? It gives every channel (command bar,
/// Power Automate, external systems via Web API) one documented business operation with typed outputs,
/// it can be secured with its own privilege, and the client doesn't need to know the stage values.
/// The validation itself stays in <see cref="Plugins.Requisition.RequisitionLifecyclePlugin"/> which runs
/// as part of the Update below – a single source of truth.
/// </para>
/// </remarks>
public sealed class SubmitRequisitionApi : PluginBase
{
    public SubmitRequisitionApi()
        : this(null, null)
    {
    }

    /// <summary>Constructor used by Dataverse when the step has unsecure/secure configuration.</summary>
    public SubmitRequisitionApi(string? unsecureConfiguration, string? secureConfiguration)
        : base(unsecureConfiguration, secureConfiguration)
    {
    }

    protected override void ExecuteDataversePlugin(LocalPluginContext context)
    {
        var execution = context.ExecutionContext;
        if (execution.MessageName != Schema.CustomApis.SubmitRequisition)
        {
            throw new InvalidPluginExecutionException($"Invalid registration: {execution.MessageName}.");
        }

        var reference = context.GetTargetReference();
        var repository = new RequisitionRepository(context.UserService);
        var stage = repository.GetRequisition(reference.Id, Schema.Requisition.Stage)
            .GetChoice<RequisitionStage>(Schema.Requisition.Stage) ?? RequisitionStage.Draft;

        if (stage is not (RequisitionStage.Draft or RequisitionStage.Rejected))
        {
            throw new InvalidPluginExecutionException(
                $"Somente requisições em Rascunho ou Rejeitadas podem ser enviadas (etapa atual: {RequisitionStateMachine.Describe(stage)}).");
        }

        // Runs as the calling user: their security role must allow updating the requisition.
        var update = new Entity(Schema.Requisition.EntityName, reference.Id);
        update.SetChoice(Schema.Requisition.Stage, RequisitionStage.PendingApproval);
        context.UserService.Update(update);

        var result = repository.GetRequisition(
            reference.Id, Schema.Requisition.ApprovalLevel, Schema.Requisition.IsOverBudget, Schema.Requisition.ApprovalNotes);

        execution.OutputParameters[Schema.CustomApis.ApprovalLevelOutput] = result.GetChoice(Schema.Requisition.ApprovalLevel) ?? 0;
        execution.OutputParameters[Schema.CustomApis.IsOverBudgetOutput] = result.GetAttributeValue<bool?>(Schema.Requisition.IsOverBudget) ?? false;
        execution.OutputParameters[Schema.CustomApis.MessageOutput] = result.GetAttributeValue<string>(Schema.Requisition.ApprovalNotes) ?? string.Empty;

        context.Trace($"Requisition {reference.Id} submitted.");
    }
}
