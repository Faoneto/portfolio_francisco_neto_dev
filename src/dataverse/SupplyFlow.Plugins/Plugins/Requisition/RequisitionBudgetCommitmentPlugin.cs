using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;

namespace SupplyFlow.Plugins.Plugins.Requisition;

/// <summary>
/// Commits the requisition amount to the cost-center budget on approval and releases it on cancellation.
/// </summary>
/// <remarks>
/// Registration: fno_purchaserequisition | Update (filtering: fno_stage) | PostOperation | Sync.
/// PreImage: fno_budgetcommitted. PostImage: fno_stage, fno_totalamount, fno_costcenterid, fno_budgetcommitted.
/// <para>
/// fno_budgetcommitted makes the operation idempotent: a requisition that goes back to Approved after an
/// SAP integration failure (SentToSap → Approved) is not committed twice.
/// </para>
/// </remarks>
public sealed class RequisitionBudgetCommitmentPlugin : PluginBase
{
    public RequisitionBudgetCommitmentPlugin()
        : this(null, null)
    {
    }

    /// <summary>Constructor used by Dataverse when the step has unsecure/secure configuration.</summary>
    public RequisitionBudgetCommitmentPlugin(string? unsecureConfiguration, string? secureConfiguration)
        : base(unsecureConfiguration, secureConfiguration)
    {
    }

    protected override void ExecuteDataversePlugin(LocalPluginContext context)
    {
        context.EnsureRegistration(Schema.Requisition.EntityName, PluginStage.PostOperation, MessageNames.Update);

        var postImage = context.GetPostImage()
            ?? throw new InvalidPluginExecutionException($"PostImage '{ImageNames.PostImage}' is not registered.");
        var alreadyCommitted = context.GetPreImage()?.GetAttributeValue<bool?>(Schema.Requisition.BudgetCommitted) == true;
        var stage = postImage.GetChoice<RequisitionStage>(Schema.Requisition.Stage);
        var costCenter = postImage.GetAttributeValue<EntityReference>(Schema.Requisition.CostCenterId);
        var amount = postImage.GetMoney(Schema.Requisition.TotalAmount);

        bool commit;
        if (stage == RequisitionStage.Approved && !alreadyCommitted)
        {
            commit = true;
        }
        else if (stage == RequisitionStage.Cancelled && alreadyCommitted)
        {
            commit = false;
        }
        else
        {
            return;
        }

        if (costCenter is null)
        {
            throw new InvalidPluginExecutionException("Centro de custo não informado na requisição.");
        }

        var budget = new BudgetService(context.SystemService, context.Trace);
        budget.AdjustCommitted(costCenter.Id, commit ? amount : -amount);

        context.SystemService.Update(new Entity(Schema.Requisition.EntityName, postImage.Id)
        {
            [Schema.Requisition.BudgetCommitted] = commit,
        });
    }
}
