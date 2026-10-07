using FakeXrmEasy.Abstractions.Plugins.Enums;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Plugins.Requisition;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Plugins;

public class RequisitionBudgetCommitmentPluginTests : TestBase
{
    private readonly Entity _supplier = Supplier();
    private readonly Entity _costCenter = CostCenter(annual: 10_000m, committed: 1_000m);

    [Fact]
    public void Approval_commits_budget_once()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.Approved);
        requisition[Schema.Requisition.TotalAmount] = new Money(2_500m);
        Context.Initialize(new[] { _costCenter, requisition });

        Run(requisition, alreadyCommitted: false);

        var costCenter = Reload(_costCenter);
        Assert.Equal(3_500m, costCenter.GetAttributeValue<Money>(Schema.CostCenter.CommittedAmount).Value);
        Assert.Equal(6_500m, costCenter.GetAttributeValue<Money>(Schema.CostCenter.AvailableBudget).Value);
        Assert.True(Reload(requisition).GetAttributeValue<bool>(Schema.Requisition.BudgetCommitted));

        // SentToSap -> Approved (reprocess): already committed, nothing changes.
        Run(requisition, alreadyCommitted: true);
        Assert.Equal(3_500m, Reload(_costCenter).GetAttributeValue<Money>(Schema.CostCenter.CommittedAmount).Value);
    }

    [Fact]
    public void Cancellation_releases_committed_budget()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.Cancelled);
        requisition[Schema.Requisition.TotalAmount] = new Money(600m);
        Context.Initialize(new[] { _costCenter, requisition });

        Run(requisition, alreadyCommitted: true);

        Assert.Equal(400m, Reload(_costCenter).GetAttributeValue<Money>(Schema.CostCenter.CommittedAmount).Value);
        Assert.False(Reload(requisition).GetAttributeValue<bool>(Schema.Requisition.BudgetCommitted));
    }

    [Fact]
    public void Cancelling_a_never_approved_requisition_does_nothing()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.Cancelled);
        requisition[Schema.Requisition.TotalAmount] = new Money(600m);
        Context.Initialize(new[] { _costCenter, requisition });

        Run(requisition, alreadyCommitted: false);

        Assert.Equal(1_000m, Reload(_costCenter).GetAttributeValue<Money>(Schema.CostCenter.CommittedAmount).Value);
    }

    private void Run(Entity requisition, bool alreadyCommitted)
    {
        var target = new Entity(Schema.Requisition.EntityName, requisition.Id) { [Schema.Requisition.Stage] = requisition[Schema.Requisition.Stage] };
        var pre = new Entity(Schema.Requisition.EntityName, requisition.Id) { [Schema.Requisition.BudgetCommitted] = alreadyCommitted };

        Context.ExecutePluginWith<RequisitionBudgetCommitmentPlugin>(
            PluginContext("Update", ProcessingStepStage.Postoperation, target, ApproverId, preImage: pre, postImage: requisition));
    }
}
