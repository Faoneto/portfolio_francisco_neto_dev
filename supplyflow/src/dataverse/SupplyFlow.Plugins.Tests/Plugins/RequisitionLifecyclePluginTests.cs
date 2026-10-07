using System;
using FakeXrmEasy.Abstractions.Plugins.Enums;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Plugins.Requisition;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Plugins;

public class RequisitionLifecyclePluginTests : TestBase
{
    private readonly Entity _supplier = Supplier();
    private readonly Entity _costCenter = CostCenter(annual: 50_000m, committed: 45_000m);
    private readonly Entity _material = Material();

    [Fact]
    public void Create_applies_defaults()
    {
        var target = new Entity(Schema.Requisition.EntityName) { [Schema.Requisition.Justification] = "x" };

        Context.ExecutePluginWith<RequisitionLifecyclePlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target));

        Assert.Equal((int)RequisitionStage.Draft, target.GetAttributeValue<OptionSetValue>(Schema.Requisition.Stage).Value);
        Assert.Equal((int)IntegrationStatus.NotSent, target.GetAttributeValue<OptionSetValue>(Schema.Requisition.IntegrationStatus).Value);
        Assert.Equal(RequesterId, target.GetAttributeValue<EntityReference>(Schema.Requisition.RequesterId).Id);
        Assert.False(target.GetAttributeValue<bool>(Schema.Requisition.BudgetCommitted));
    }

    [Fact]
    public void Create_in_a_later_stage_is_rejected()
    {
        var target = new Entity(Schema.Requisition.EntityName)
        {
            [Schema.Requisition.Stage] = new OptionSetValue((int)RequisitionStage.Approved),
        };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<RequisitionLifecyclePlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target)));
    }

    [Fact]
    public void Invalid_transition_is_rejected()
    {
        var requisition = Requisition(_supplier, _costCenter);
        Context.Initialize(requisition);

        var ex = Throws<InvalidPluginExecutionException>(() => Transition(requisition, RequisitionStage.Approved, ApproverId));

        Assert.Contains("Transição inválida", ex.Message);
    }

    [Fact]
    public void Submission_resolves_level_and_flags_over_budget()
    {
        var requisition = Requisition(_supplier, _costCenter);
        Context.Initialize(new[]
        {
            _supplier, _costCenter, _material, requisition,
            Line(requisition, _material, 100m, 60m), // 6.000 > 5.000 available
            Policy(ApprovalLevel.Director, 5_000m),
        });

        var target = Transition(requisition, RequisitionStage.PendingApproval);

        Assert.Equal((int)ApprovalLevel.Cfo, target.GetAttributeValue<OptionSetValue>(Schema.Requisition.ApprovalLevel).Value);
        Assert.True(target.GetAttributeValue<bool>(Schema.Requisition.IsOverBudget));
        Assert.Equal(6_000m, target.GetAttributeValue<Money>(Schema.Requisition.TotalAmount).Value);
        Assert.Contains("excede o orçamento", target.GetAttributeValue<string>(Schema.Requisition.ApprovalNotes));
        Assert.NotNull(target[Schema.Requisition.SubmittedOn]);
    }

    [Fact]
    public void Submission_lists_every_problem_at_once()
    {
        var blocked = Supplier(status: SupplierStatus.Blocked);
        var requisition = Requisition(blocked, _costCenter);
        requisition[Schema.Requisition.Justification] = null;
        requisition[Schema.Requisition.NeedByDate] = DateTime.UtcNow.Date.AddDays(-1);
        Context.Initialize(new[] { blocked, _costCenter, requisition });

        var ex = Throws<InvalidPluginExecutionException>(() => Transition(requisition, RequisitionStage.PendingApproval));

        Assert.Contains("não está homologado", ex.Message);
        Assert.Contains("data de necessidade", ex.Message);
        Assert.Contains("justificativa", ex.Message);
        Assert.Contains("ao menos um item", ex.Message);
    }

    [Fact]
    public void Requester_cannot_approve_own_requisition()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.PendingApproval);
        Context.Initialize(requisition);

        var ex = Throws<InvalidPluginExecutionException>(() => Transition(requisition, RequisitionStage.Approved, RequesterId));

        Assert.Contains("Segregação de funções", ex.Message);
    }

    [Theory]
    [InlineData("yes", IntegrationStatus.Queued)]
    [InlineData("no", IntegrationStatus.NotSent)]
    public void Approval_queues_sap_integration_when_enabled(string enabled, IntegrationStatus expected)
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.PendingApproval);
        Context.Initialize(new[] { requisition, EnvironmentVariable(Schema.EnvironmentVariables.SapIntegrationEnabled, enabled) });

        var target = Transition(requisition, RequisitionStage.Approved, ApproverId);

        Assert.Equal((int)expected, target.GetAttributeValue<OptionSetValue>(Schema.Requisition.IntegrationStatus).Value);
        Assert.NotNull(target[Schema.Requisition.ApprovedOn]);
    }

    [Fact]
    public void Cancellation_requires_a_reason()
    {
        var requisition = Requisition(_supplier, _costCenter);
        Context.Initialize(requisition);

        var ex = Throws<InvalidPluginExecutionException>(() => Transition(requisition, RequisitionStage.Cancelled));
        Assert.Contains("motivo do cancelamento", ex.Message);

        var target = new Entity(Schema.Requisition.EntityName, requisition.Id)
        {
            [Schema.Requisition.Stage] = new OptionSetValue((int)RequisitionStage.Cancelled),
            [Schema.Requisition.CancellationReason] = "Compra substituída por contrato corporativo.",
        };
        Context.ExecutePluginWith<RequisitionLifecyclePlugin>(
            PluginContext("Update", ProcessingStepStage.Preoperation, target, preImage: requisition));
    }

    [Fact]
    public void Purchase_order_number_is_required_to_complete()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.SentToSap);
        Context.Initialize(requisition);

        Assert.Throws<InvalidPluginExecutionException>(() =>
            Transition(requisition, RequisitionStage.PurchaseOrderCreated, ApproverId));
    }

    private Entity Transition(Entity requisition, RequisitionStage to, Guid? user = null)
    {
        var target = new Entity(Schema.Requisition.EntityName, requisition.Id)
        {
            [Schema.Requisition.Stage] = new OptionSetValue((int)to),
        };

        Context.ExecutePluginWith<RequisitionLifecyclePlugin>(
            PluginContext("Update", ProcessingStepStage.Preoperation, target, user, preImage: requisition));
        return target;
    }
}
