using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Domain;

public class RequisitionStateMachineTests
{
    [Theory]
    [InlineData(RequisitionStage.Draft, RequisitionStage.PendingApproval)]
    [InlineData(RequisitionStage.PendingApproval, RequisitionStage.Approved)]
    [InlineData(RequisitionStage.PendingApproval, RequisitionStage.Rejected)]
    [InlineData(RequisitionStage.PendingApproval, RequisitionStage.Draft)]
    [InlineData(RequisitionStage.Rejected, RequisitionStage.PendingApproval)]
    [InlineData(RequisitionStage.Approved, RequisitionStage.SentToSap)]
    [InlineData(RequisitionStage.SentToSap, RequisitionStage.PurchaseOrderCreated)]
    [InlineData(RequisitionStage.SentToSap, RequisitionStage.Approved)]
    [InlineData(RequisitionStage.Approved, RequisitionStage.Cancelled)]
    public void Allowed_transitions(RequisitionStage from, RequisitionStage to)
    {
        Assert.True(RequisitionStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RequisitionStage.Draft, RequisitionStage.Approved)]
    [InlineData(RequisitionStage.Draft, RequisitionStage.SentToSap)]
    [InlineData(RequisitionStage.Rejected, RequisitionStage.Approved)]
    [InlineData(RequisitionStage.SentToSap, RequisitionStage.Cancelled)]
    [InlineData(RequisitionStage.PurchaseOrderCreated, RequisitionStage.Draft)]
    [InlineData(RequisitionStage.Cancelled, RequisitionStage.Draft)]
    public void Forbidden_transitions(RequisitionStage from, RequisitionStage to)
    {
        Assert.False(RequisitionStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RequisitionStage.Draft, true)]
    [InlineData(RequisitionStage.Rejected, true)]
    [InlineData(RequisitionStage.PendingApproval, false)]
    [InlineData(RequisitionStage.Approved, false)]
    public void Only_draft_and_rejected_are_editable(RequisitionStage stage, bool editable)
    {
        Assert.Equal(editable, RequisitionStateMachine.IsEditable(stage));
    }
}
