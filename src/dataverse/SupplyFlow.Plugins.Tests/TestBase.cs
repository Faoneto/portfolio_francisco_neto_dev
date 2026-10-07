using System;
using System.Linq;
using FakeXrmEasy.Abstractions;
using FakeXrmEasy.Abstractions.Enums;
using FakeXrmEasy.Abstractions.Plugins.Enums;
using FakeXrmEasy.Middleware;
using FakeXrmEasy.Middleware.Crud;
using FakeXrmEasy.Middleware.Messages;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;

namespace SupplyFlow.Plugins.Tests;

/// <summary>
/// Shared FakeXrmEasy setup and test-data builders.
/// FakeXrmEasy v3 is used under the RPL-1.5 license (open-source project).
/// </summary>
public abstract class TestBase
{
    protected TestBase()
    {
        EnvironmentVariableService.ClearCache();
        Context = MiddlewareBuilder.New()
            .AddCrud()
            .AddFakeMessageExecutors()
            .UseCrud()
            .UseMessages()
            .SetLicense(FakeXrmEasyLicense.RPL_1_5)
            .Build();
        Context.InitializeMetadata(SchemaMetadata.All);
        Service = Context.GetOrganizationService();
    }

    protected IXrmFakedContext Context { get; }

    protected IOrganizationService Service { get; }

    protected Guid RequesterId { get; } = Guid.NewGuid();

    protected Guid ApproverId { get; } = Guid.NewGuid();

    protected XrmFakedPluginExecutionContext PluginContext(
        string message, ProcessingStepStage stage, Entity target, Guid? initiatingUser = null, Entity? preImage = null, Entity? postImage = null)
    {
        var ctx = Context.GetDefaultPluginContext();
        ctx.MessageName = message;
        ctx.Stage = (int)stage;
        ctx.PrimaryEntityName = target.LogicalName;
        ctx.PrimaryEntityId = target.Id;
        ctx.InputParameters = new ParameterCollection { { "Target", target } };
        ctx.InitiatingUserId = initiatingUser ?? RequesterId;
        ctx.UserId = initiatingUser ?? RequesterId;
        ctx.OrganizationId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        if (preImage is not null)
        {
            ctx.PreEntityImages = new EntityImageCollection { { "PreImage", preImage } };
        }

        if (postImage is not null)
        {
            ctx.PostEntityImages = new EntityImageCollection { { "PostImage", postImage } };
        }

        return ctx;
    }

    protected static Entity Supplier(string name = "Fornecedor Norte Ltda", SupplierStatus status = SupplierStatus.Approved, string cnpj = "11222333000181")
    {
        return new Entity(Schema.Account.EntityName, Guid.NewGuid())
        {
            [Schema.Account.Name] = name,
            [Schema.Account.IsSupplier] = true,
            [Schema.Account.Cnpj] = cnpj,
            [Schema.Account.SupplierStatus] = new OptionSetValue((int)status),
            [Schema.Account.StateCode] = new OptionSetValue(0),
        };
    }

    protected static Entity Material(string name = "Rolamento 6205", decimal price = 50m, MaterialGroup group = MaterialGroup.Mro)
    {
        return new Entity(Schema.Material.EntityName, Guid.NewGuid())
        {
            [Schema.Material.Name] = name,
            [Schema.Material.StandardPrice] = new Money(price),
            [Schema.Material.MaterialGroup] = new OptionSetValue((int)group),
        };
    }

    protected static Entity CostCenter(decimal annual = 100_000m, decimal committed = 0m)
    {
        return new Entity(Schema.CostCenter.EntityName, Guid.NewGuid())
        {
            [Schema.CostCenter.Name] = "Manutenção Industrial",
            [Schema.CostCenter.AnnualBudget] = new Money(annual),
            [Schema.CostCenter.CommittedAmount] = new Money(committed),
        };
    }

    protected Entity Requisition(Entity supplier, Entity costCenter, RequisitionStage stage = RequisitionStage.Draft)
    {
        return new Entity(Schema.Requisition.EntityName, Guid.NewGuid())
        {
            [Schema.Requisition.Name] = "RC-202610-00001",
            [Schema.Requisition.SupplierId] = supplier.ToEntityReference(),
            [Schema.Requisition.CostCenterId] = costCenter.ToEntityReference(),
            [Schema.Requisition.RequesterId] = new EntityReference("systemuser", RequesterId),
            [Schema.Requisition.NeedByDate] = DateTime.UtcNow.Date.AddDays(15),
            [Schema.Requisition.Justification] = "Reposição de estoque de manutenção preventiva.",
            [Schema.Requisition.Stage] = new OptionSetValue((int)stage),
            [Schema.Requisition.TotalAmount] = new Money(0m),
            [Schema.Requisition.BudgetCommitted] = false,
        };
    }

    protected static Entity Line(Entity requisition, Entity material, decimal quantity, decimal unitPrice, MaterialGroup group = MaterialGroup.Mro)
    {
        return new Entity(Schema.RequisitionLine.EntityName, Guid.NewGuid())
        {
            [Schema.RequisitionLine.RequisitionId] = requisition.ToEntityReference(),
            [Schema.RequisitionLine.MaterialId] = material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = quantity,
            [Schema.RequisitionLine.UnitPrice] = new Money(unitPrice),
            [Schema.RequisitionLine.LineTotal] = new Money(quantity * unitPrice),
            [Schema.RequisitionLine.MaterialGroup] = new OptionSetValue((int)group),
            ["statecode"] = new OptionSetValue(0),
        };
    }

    protected static Entity SupplierMaterialLink(Entity supplier, Entity material)
    {
        return new Entity(Schema.SupplierMaterial.IntersectEntityName, Guid.NewGuid())
        {
            [Schema.SupplierMaterial.AccountId] = supplier.Id,
            [Schema.SupplierMaterial.MaterialId] = material.Id,
        };
    }

    protected static Entity Policy(ApprovalLevel level, decimal minAmount, MaterialGroup? group = null)
    {
        var policy = new Entity(Schema.ApprovalPolicy.EntityName, Guid.NewGuid())
        {
            [Schema.ApprovalPolicy.Name] = $"{level} >= {minAmount}",
            [Schema.ApprovalPolicy.Level] = new OptionSetValue((int)level),
            [Schema.ApprovalPolicy.MinAmount] = new Money(minAmount),
            [Schema.ApprovalPolicy.StateCode] = new OptionSetValue(0),
        };
        if (group is not null)
        {
            policy[Schema.ApprovalPolicy.MaterialGroup] = new OptionSetValue((int)group.Value);
        }

        return policy;
    }

    protected static Entity EnvironmentVariable(string schemaName, string? defaultValue)
    {
        return new Entity("environmentvariabledefinition", Guid.NewGuid())
        {
            ["schemaname"] = schemaName,
            ["defaultvalue"] = defaultValue,
        };
    }

    protected static Entity EnvironmentVariableValue(Entity definition, string value)
    {
        return new Entity("environmentvariablevalue", Guid.NewGuid())
        {
            ["environmentvariabledefinitionid"] = definition.ToEntityReference(),
            ["value"] = value,
        };
    }

    protected Entity Reload(Entity entity)
    {
        return Service.Retrieve(entity.LogicalName, entity.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
    }

    protected static T Throws<T>(Action action)
        where T : Exception
    {
        return Xunit.Assert.Throws<T>(action);
    }

    protected static string[] Lines(string text) => text.Split('\n').Select(l => l.Trim()).ToArray();
}
