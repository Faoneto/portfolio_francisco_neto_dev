using System;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;

namespace SupplyFlow.Plugins.Plugins.Requisition;

/// <summary>
/// Enforces the requisition state machine and the rules attached to each transition.
/// </summary>
/// <remarks>
/// Registration: fno_purchaserequisition | PreOperation | Sync
/// <list type="bullet">
///   <item>Create – no filtering.</item>
///   <item>Update – filtering: fno_stage. PreImage: fno_stage, fno_requesterid, fno_supplierid, fno_costcenterid,
///         fno_needbydate, fno_justification, fno_cancellationreason, fno_sappurchaseorder.</item>
/// </list>
/// The server is the single source of truth: the same rules run whether the change comes from the
/// model-driven form, the fno_SubmitRequisition Custom API, Power Automate, the Web API or an import.
/// </remarks>
public sealed class RequisitionLifecyclePlugin : PluginBase
{
    public RequisitionLifecyclePlugin()
        : this(null, null)
    {
    }

    /// <summary>Constructor used by Dataverse when the step has unsecure/secure configuration.</summary>
    public RequisitionLifecyclePlugin(string? unsecureConfiguration, string? secureConfiguration)
        : base(unsecureConfiguration, secureConfiguration)
    {
    }

    protected override void ExecuteDataversePlugin(LocalPluginContext context)
    {
        context.EnsureRegistration(Schema.Requisition.EntityName, PluginStage.PreOperation, MessageNames.Create, MessageNames.Update);

        if (context.MessageName == MessageNames.Create)
        {
            OnCreate(context, context.GetTarget());
            return;
        }

        var target = context.GetTarget();
        if (!target.IsChanging(Schema.Requisition.Stage))
        {
            return;
        }

        var preImage = context.GetPreImage()
            ?? throw new InvalidPluginExecutionException($"PreImage '{ImageNames.PreImage}' is not registered.");
        var from = preImage.GetChoice<RequisitionStage>(Schema.Requisition.Stage) ?? RequisitionStage.Draft;
        var to = target.GetChoice<RequisitionStage>(Schema.Requisition.Stage)
            ?? throw new InvalidPluginExecutionException("A etapa da requisição não pode ficar vazia.");

        if (from == to)
        {
            return;
        }

        if (!RequisitionStateMachine.CanTransition(from, to))
        {
            throw new InvalidPluginExecutionException(
                $"Transição inválida: \"{RequisitionStateMachine.Describe(from)}\" → \"{RequisitionStateMachine.Describe(to)}\".");
        }

        context.Trace($"Stage transition {from} -> {to}");
        var requisition = context.GetMergedTarget();

        switch (to)
        {
            case RequisitionStage.PendingApproval:
                new RequisitionSubmissionService(context.SystemService).PrepareSubmission(requisition, target);
                break;

            case RequisitionStage.Approved:
            case RequisitionStage.Rejected:
                EnsureSegregationOfDuties(context, requisition);
                if (to == RequisitionStage.Approved)
                {
                    OnApproved(context, target, isReprocess: from == RequisitionStage.SentToSap);
                }

                break;

            case RequisitionStage.Draft:
                target[Schema.Requisition.ApprovalLevel] = null;
                target[Schema.Requisition.SubmittedOn] = null;
                break;

            case RequisitionStage.Cancelled:
                if (string.IsNullOrWhiteSpace(requisition.GetAttributeValue<string>(Schema.Requisition.CancellationReason)))
                {
                    throw new InvalidPluginExecutionException("Informe o motivo do cancelamento.");
                }

                break;

            case RequisitionStage.PurchaseOrderCreated:
                if (string.IsNullOrWhiteSpace(requisition.GetAttributeValue<string>(Schema.Requisition.SapPurchaseOrder)))
                {
                    throw new InvalidPluginExecutionException("O número do pedido SAP é obrigatório para concluir a requisição.");
                }

                break;
        }
    }

    private static void OnCreate(LocalPluginContext context, Entity target)
    {
        var stage = target.GetChoice<RequisitionStage>(Schema.Requisition.Stage);
        if (stage is not null && stage != RequisitionStage.Draft)
        {
            throw new InvalidPluginExecutionException("Requisições devem ser criadas na etapa Rascunho.");
        }

        target.SetChoice(Schema.Requisition.Stage, RequisitionStage.Draft);
        target.SetChoice(Schema.Requisition.IntegrationStatus, IntegrationStatus.NotSent);
        target[Schema.Requisition.TotalAmount] = new Money(0m);
        target[Schema.Requisition.BudgetCommitted] = false;

        if (!target.Contains(Schema.Requisition.RequesterId) || target[Schema.Requisition.RequesterId] is null)
        {
            target[Schema.Requisition.RequesterId] = new EntityReference("systemuser", context.ExecutionContext.InitiatingUserId);
        }
    }

    /// <summary>The requester can never approve or reject their own requisition.</summary>
    private static void EnsureSegregationOfDuties(LocalPluginContext context, Entity requisition)
    {
        var requester = requisition.GetAttributeValue<EntityReference>(Schema.Requisition.RequesterId);
        if (requester is not null && requester.Id == context.ExecutionContext.InitiatingUserId)
        {
            throw new InvalidPluginExecutionException(
                "Segregação de funções: o requisitante não pode aprovar ou rejeitar a própria requisição.");
        }
    }

    private static void OnApproved(LocalPluginContext context, Entity target, bool isReprocess)
    {
        if (!isReprocess)
        {
            target[Schema.Requisition.ApprovedOn] = DateTime.UtcNow;
        }

        var variables = new EnvironmentVariableService(context.SystemService, context.ExecutionContext.OrganizationId);
        var integrationEnabled = variables.GetBoolean(Schema.EnvironmentVariables.SapIntegrationEnabled, defaultValue: false);

        // The Service Bus service endpoint step (async) picks up Approved requisitions; the Azure Function
        // moves them to SentToSap / PurchaseOrderCreated. See docs/integration.md.
        target.SetChoice(Schema.Requisition.IntegrationStatus, integrationEnabled ? IntegrationStatus.Queued : IntegrationStatus.NotSent);
        if (isReprocess)
        {
            target[Schema.Requisition.IntegrationMessage] = "Reenvio ao SAP solicitado.";
        }
    }
}
