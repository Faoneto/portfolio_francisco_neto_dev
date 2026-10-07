using System;
using FakeXrmEasy.Abstractions.Plugins.Enums;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Plugins.Requisition;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Plugins;

public class RequisitionLinePricingPluginTests : TestBase
{
    private readonly Entity _supplier = Supplier();
    private readonly Entity _costCenter = CostCenter();
    private readonly Entity _material = Material(price: 33.333m);

    [Fact]
    public void Defaults_unit_price_from_material_and_rounds_total()
    {
        var requisition = Requisition(_supplier, _costCenter);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition, SupplierMaterialLink(_supplier, _material) });

        var target = new Entity(Schema.RequisitionLine.EntityName)
        {
            [Schema.RequisitionLine.RequisitionId] = requisition.ToEntityReference(),
            [Schema.RequisitionLine.MaterialId] = _material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = 3m,
        };

        Context.ExecutePluginWith<RequisitionLinePricingPlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target));

        Assert.Equal(33.333m, target.GetAttributeValue<Money>(Schema.RequisitionLine.UnitPrice).Value);
        Assert.Equal(100.00m, target.GetAttributeValue<Money>(Schema.RequisitionLine.LineTotal).Value);
        Assert.Equal("Rolamento 6205", target[Schema.RequisitionLine.Name]);
        Assert.Equal((int)MaterialGroup.Mro, target.GetAttributeValue<OptionSetValue>(Schema.RequisitionLine.MaterialGroup).Value);
    }

    [Fact]
    public void Quantity_change_uses_price_from_pre_image()
    {
        var requisition = Requisition(_supplier, _costCenter);
        var line = Line(requisition, _material, 1m, 10m);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition, line, SupplierMaterialLink(_supplier, _material) });

        var target = new Entity(Schema.RequisitionLine.EntityName, line.Id) { [Schema.RequisitionLine.Quantity] = 4m };

        Context.ExecutePluginWith<RequisitionLinePricingPlugin>(
            PluginContext("Update", ProcessingStepStage.Preoperation, target, preImage: line));

        Assert.Equal(40m, target.GetAttributeValue<Money>(Schema.RequisitionLine.LineTotal).Value);
    }

    [Fact]
    public void Lines_are_locked_after_submission()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.PendingApproval);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition, SupplierMaterialLink(_supplier, _material) });

        var target = new Entity(Schema.RequisitionLine.EntityName)
        {
            [Schema.RequisitionLine.RequisitionId] = requisition.ToEntityReference(),
            [Schema.RequisitionLine.MaterialId] = _material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = 1m,
        };

        var ex = Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<RequisitionLinePricingPlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target)));

        Assert.Contains("Em aprovação", ex.Message);
    }

    [Fact]
    public void Material_must_be_approved_for_the_supplier()
    {
        var requisition = Requisition(_supplier, _costCenter);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition }); // no N:N link

        var target = new Entity(Schema.RequisitionLine.EntityName)
        {
            [Schema.RequisitionLine.RequisitionId] = requisition.ToEntityReference(),
            [Schema.RequisitionLine.MaterialId] = _material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = 1m,
        };

        var ex = Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<RequisitionLinePricingPlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target)));

        Assert.Contains("não está homologado", ex.Message);
    }

    [Fact]
    public void Homologation_check_can_be_disabled_by_environment_variable()
    {
        var requisition = Requisition(_supplier, _costCenter);
        var variable = EnvironmentVariable(Schema.EnvironmentVariables.RequireSupplierMaterialApproval, "yes");
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition, variable, EnvironmentVariableValue(variable, "no") });

        var target = new Entity(Schema.RequisitionLine.EntityName)
        {
            [Schema.RequisitionLine.RequisitionId] = requisition.ToEntityReference(),
            [Schema.RequisitionLine.MaterialId] = _material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = 2m,
        };

        Context.ExecutePluginWith<RequisitionLinePricingPlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target));

        Assert.Equal(66.67m, target.GetAttributeValue<Money>(Schema.RequisitionLine.LineTotal).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(decimal quantity)
    {
        var requisition = Requisition(_supplier, _costCenter);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition });

        var target = new Entity(Schema.RequisitionLine.EntityName)
        {
            [Schema.RequisitionLine.RequisitionId] = requisition.ToEntityReference(),
            [Schema.RequisitionLine.MaterialId] = _material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = quantity,
        };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<RequisitionLinePricingPlugin>(PluginContext("Create", ProcessingStepStage.Preoperation, target)));
    }
}

public class RequisitionLineTotalsPluginTests : TestBase
{
    private readonly Entity _supplier = Supplier();
    private readonly Entity _costCenter = CostCenter();
    private readonly Entity _material = Material();

    [Fact]
    public void Header_total_is_the_sum_of_lines()
    {
        var requisition = Requisition(_supplier, _costCenter);
        var line1 = Line(requisition, _material, 2m, 100m);
        var line2 = Line(requisition, _material, 1m, 49.90m);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition, line1, line2 });

        Context.ExecutePluginWith<RequisitionLineTotalsPlugin>(PluginContext("Create", ProcessingStepStage.Postoperation, line2));

        Assert.Equal(249.90m, Reload(requisition).GetAttributeValue<Money>(Schema.Requisition.TotalAmount).Value);
    }

    [Fact]
    public void Moving_a_line_recalculates_both_requisitions()
    {
        var from = Requisition(_supplier, _costCenter);
        var to = Requisition(_supplier, _costCenter);
        var moved = Line(to, _material, 1m, 300m);
        var remaining = Line(from, _material, 1m, 20m);
        Context.Initialize(new[] { _supplier, _costCenter, _material, from, to, moved, remaining });

        var target = new Entity(Schema.RequisitionLine.EntityName, moved.Id)
        {
            [Schema.RequisitionLine.RequisitionId] = to.ToEntityReference(),
        };
        var preImage = new Entity(Schema.RequisitionLine.EntityName, moved.Id)
        {
            [Schema.RequisitionLine.RequisitionId] = from.ToEntityReference(),
        };

        Context.ExecutePluginWith<RequisitionLineTotalsPlugin>(
            PluginContext("Update", ProcessingStepStage.Postoperation, target, preImage: preImage));

        Assert.Equal(20m, Reload(from).GetAttributeValue<Money>(Schema.Requisition.TotalAmount).Value);
        Assert.Equal(300m, Reload(to).GetAttributeValue<Money>(Schema.Requisition.TotalAmount).Value);
    }

    [Fact]
    public void Delete_is_blocked_after_submission()
    {
        var requisition = Requisition(_supplier, _costCenter, RequisitionStage.PendingApproval);
        var line = Line(requisition, _material, 1m, 10m);
        Context.Initialize(new[] { _supplier, _costCenter, _material, requisition });

        var ctx = PluginContext("Delete", ProcessingStepStage.Postoperation, line, preImage: line);
        ctx.InputParameters["Target"] = line.ToEntityReference();

        Assert.Throws<InvalidPluginExecutionException>(() => Context.ExecutePluginWith<RequisitionLineTotalsPlugin>(ctx));
    }

    [Fact]
    public void Cascade_delete_from_requisition_is_ignored()
    {
        var line = Line(Requisition(_supplier, _costCenter), _material, 1m, 10m);
        Context.Initialize(new[] { _supplier, _costCenter, _material }); // requisition already gone

        var ctx = PluginContext("Delete", ProcessingStepStage.Postoperation, line, preImage: line);
        ctx.InputParameters["Target"] = line.ToEntityReference();
        ctx.ParentContext = new FakeXrmEasy.Plugins.XrmFakedPluginExecutionContext
        {
            MessageName = MessageNames.Delete,
            PrimaryEntityName = Schema.Requisition.EntityName,
            PrimaryEntityId = Guid.NewGuid(),
        };

        Context.ExecutePluginWith<RequisitionLineTotalsPlugin>(ctx);
    }
}
