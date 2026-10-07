using System;
using FakeXrmEasy.Abstractions;
using FakeXrmEasy.Abstractions.Enums;
using FakeXrmEasy.Middleware;
using FakeXrmEasy.Middleware.Crud;
using FakeXrmEasy.Middleware.Messages;
using FakeXrmEasy.Middleware.Pipeline;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.CustomApis;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Integration;

/// <summary>
/// End-to-end procure-to-pay scenario running every plugin through the simulated event pipeline,
/// wired from plugin-registration.json.
/// </summary>
public class ProcureToPayScenarioTests
{
    private readonly IXrmFakedContext _context;
    private readonly Guid _requesterId = Guid.NewGuid();
    private readonly Guid _approverId = Guid.NewGuid();
    private readonly Entity _supplier;
    private readonly Entity _costCenter;
    private readonly Entity _bearing;
    private readonly Entity _belt;

    public ProcureToPayScenarioTests()
    {
        EnvironmentVariableService.ClearCache();
        _context = MiddlewareBuilder.New()
            .AddCrud()
            .AddFakeMessageExecutors()
            .AddPipelineSimulation(new PipelineOptions { UsePipelineSimulation = true })
            .UsePipelineSimulation()
            .UseCrud()
            .UseMessages()
            .SetLicense(FakeXrmEasyLicense.RPL_1_5)
            .Build();
        _context.InitializeMetadata(SchemaMetadata.All);

        _supplier = new Entity(Schema.Account.EntityName, Guid.NewGuid())
        {
            [Schema.Account.Name] = "Rolamentos Amazônia Ltda",
            [Schema.Account.IsSupplier] = true,
            [Schema.Account.Cnpj] = "11222333000181",
            [Schema.Account.SupplierStatus] = new OptionSetValue((int)SupplierStatus.Approved),
            [Schema.Account.StateCode] = new OptionSetValue(0),
        };
        _costCenter = new Entity(Schema.CostCenter.EntityName, Guid.NewGuid())
        {
            [Schema.CostCenter.Name] = "CC1001 – Manutenção",
            [Schema.CostCenter.AnnualBudget] = new Money(100_000m),
            [Schema.CostCenter.CommittedAmount] = new Money(10_000m),
        };
        _bearing = Material("Rolamento 6205", 48.90m);
        _belt = Material("Correia transportadora 800mm", 315m);

        var sapEnabled = new Entity("environmentvariabledefinition", Guid.NewGuid())
        {
            ["schemaname"] = Schema.EnvironmentVariables.SapIntegrationEnabled,
            ["defaultvalue"] = "yes",
        };

        _context.Initialize(new[]
        {
            _supplier, _costCenter, _bearing, _belt, sapEnabled,
            Link(_supplier, _bearing), Link(_supplier, _belt),
            Policy(ApprovalLevel.Manager, 0m), Policy(ApprovalLevel.Director, 25_000m), Policy(ApprovalLevel.Cfo, 150_000m),
        });

        var registered = RegistrationDrivenPipeline.RegisterAll(_context, SchemaMetadata.Registration);
        Assert.True(registered >= 9);
    }

    [Fact]
    public void Requisition_goes_from_draft_to_approved_with_budget_committed()
    {
        // 1. Requester creates the requisition and its lines.
        var requisitionId = ServiceAs(_requesterId).Create(new Entity(Schema.Requisition.EntityName)
        {
            [Schema.Requisition.SupplierId] = _supplier.ToEntityReference(),
            [Schema.Requisition.CostCenterId] = _costCenter.ToEntityReference(),
            [Schema.Requisition.NeedByDate] = DateTime.UtcNow.Date.AddDays(10),
            [Schema.Requisition.Justification] = "Parada programada da linha 2 – troca de rolamentos e correias.",
        });

        ServiceAs(_requesterId).Create(NewLine(requisitionId, _bearing, 40m));  // 40 x 48,90 = 1.956,00 (price from material)
        var beltLine = ServiceAs(_requesterId).Create(NewLine(requisitionId, _belt, 80m)); // 80 x 315 = 25.200,00
        ServiceAs(_requesterId).Update(new Entity(Schema.RequisitionLine.EntityName, beltLine) { [Schema.RequisitionLine.Quantity] = 90m }); // 28.350,00

        var draft = Get(requisitionId);
        Assert.Equal(RequisitionStage.Draft, (RequisitionStage)draft.GetAttributeValue<OptionSetValue>(Schema.Requisition.Stage).Value);
        Assert.Equal(_requesterId, draft.GetAttributeValue<EntityReference>(Schema.Requisition.RequesterId).Id);
        Assert.Equal(30_306.00m, draft.GetAttributeValue<Money>(Schema.Requisition.TotalAmount).Value);

        // 2. Submit through the Custom API – its inner Update runs the lifecycle plugin through the pipeline.
        var apiContext = _context.GetDefaultPluginContext();
        apiContext.MessageName = Schema.CustomApis.SubmitRequisition;
        apiContext.Stage = 30;
        apiContext.UserId = apiContext.InitiatingUserId = _requesterId;
        apiContext.InputParameters = new ParameterCollection { { "Target", new EntityReference(Schema.Requisition.EntityName, requisitionId) } };
        _context.ExecutePluginWith<SubmitRequisitionApi>(apiContext);

        Assert.Equal((int)ApprovalLevel.Director, apiContext.OutputParameters[Schema.CustomApis.ApprovalLevelOutput]);
        Assert.Equal(false, apiContext.OutputParameters[Schema.CustomApis.IsOverBudgetOutput]);
        Assert.Contains("Diretor", (string)apiContext.OutputParameters[Schema.CustomApis.MessageOutput], StringComparison.OrdinalIgnoreCase);

        // 3. Lines are locked while pending approval.
        Assert.Throws<InvalidPluginExecutionException>(() => ServiceAs(_requesterId).Create(NewLine(requisitionId, _bearing, 1m)));

        // 4. Requester cannot approve their own requisition (segregation of duties).
        Assert.Throws<InvalidPluginExecutionException>(() => ServiceAs(_requesterId).Update(StageChange(requisitionId, RequisitionStage.Approved)));

        // 5. Approver approves → budget committed + SAP integration queued.
        ServiceAs(_approverId).Update(StageChange(requisitionId, RequisitionStage.Approved));

        var approved = Get(requisitionId);
        Assert.Equal((int)RequisitionStage.Approved, approved.GetAttributeValue<OptionSetValue>(Schema.Requisition.Stage).Value);
        Assert.Equal((int)IntegrationStatus.Queued, approved.GetAttributeValue<OptionSetValue>(Schema.Requisition.IntegrationStatus).Value);
        Assert.True(approved.GetAttributeValue<bool>(Schema.Requisition.BudgetCommitted));

        var costCenter = _context.GetOrganizationService().Retrieve(Schema.CostCenter.EntityName, _costCenter.Id, new ColumnSet(true));
        Assert.Equal(40_306.00m, costCenter.GetAttributeValue<Money>(Schema.CostCenter.CommittedAmount).Value);
        Assert.Equal(59_694.00m, costCenter.GetAttributeValue<Money>(Schema.CostCenter.AvailableBudget).Value);

        // 6. Cancelling releases the budget.
        var cancel = StageChange(requisitionId, RequisitionStage.Cancelled);
        cancel[Schema.Requisition.CancellationReason] = "Fornecedor atrasou entrega; compra emergencial feita via contrato.";
        ServiceAs(_approverId).Update(cancel);

        costCenter = _context.GetOrganizationService().Retrieve(Schema.CostCenter.EntityName, _costCenter.Id, new ColumnSet(true));
        Assert.Equal(10_000m, costCenter.GetAttributeValue<Money>(Schema.CostCenter.CommittedAmount).Value);
    }

    [Fact]
    public void Duplicate_supplier_cnpj_is_blocked_through_the_pipeline()
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(() => ServiceAs(_requesterId).Create(new Entity(Schema.Account.EntityName)
        {
            [Schema.Account.Name] = "Cadastro duplicado",
            [Schema.Account.IsSupplier] = true,
            [Schema.Account.Cnpj] = "11.222.333/0001-81",
        }));

        Assert.Contains("Rolamentos Amazônia Ltda", ex.Message);
    }

    /// <summary>
    /// Sets the caller for the NEXT call. FakeXrmEasy keeps a single global caller that is switched to a
    /// system user whenever a plugin calls CreateOrganizationService(null), so never cache the returned service.
    /// </summary>
    private IOrganizationService ServiceAs(Guid userId)
    {
        _context.CallerProperties.CallerId = new EntityReference("systemuser", userId);
        return _context.GetOrganizationService();
    }

    private Entity Get(Guid requisitionId)
    {
        return _context.GetOrganizationService().Retrieve(Schema.Requisition.EntityName, requisitionId, new ColumnSet(true));
    }

    private static Entity StageChange(Guid requisitionId, RequisitionStage stage)
    {
        return new Entity(Schema.Requisition.EntityName, requisitionId) { [Schema.Requisition.Stage] = new OptionSetValue((int)stage) };
    }

    private static Entity NewLine(Guid requisitionId, Entity material, decimal quantity)
    {
        return new Entity(Schema.RequisitionLine.EntityName)
        {
            [Schema.RequisitionLine.RequisitionId] = new EntityReference(Schema.Requisition.EntityName, requisitionId),
            [Schema.RequisitionLine.MaterialId] = material.ToEntityReference(),
            [Schema.RequisitionLine.Quantity] = quantity,
            ["statecode"] = new OptionSetValue(0),
        };
    }

    private static Entity Material(string name, decimal price)
    {
        return new Entity(Schema.Material.EntityName, Guid.NewGuid())
        {
            [Schema.Material.Name] = name,
            [Schema.Material.StandardPrice] = new Money(price),
            [Schema.Material.MaterialGroup] = new OptionSetValue((int)MaterialGroup.Mro),
        };
    }

    private static Entity Link(Entity supplier, Entity material)
    {
        return new Entity(Schema.SupplierMaterial.IntersectEntityName, Guid.NewGuid())
        {
            [Schema.SupplierMaterial.AccountId] = supplier.Id,
            [Schema.SupplierMaterial.MaterialId] = material.Id,
        };
    }

    private static Entity Policy(ApprovalLevel level, decimal minAmount)
    {
        return new Entity(Schema.ApprovalPolicy.EntityName, Guid.NewGuid())
        {
            [Schema.ApprovalPolicy.Name] = level.ToString(),
            [Schema.ApprovalPolicy.Level] = new OptionSetValue((int)level),
            [Schema.ApprovalPolicy.MinAmount] = new Money(minAmount),
            [Schema.ApprovalPolicy.StateCode] = new OptionSetValue(0),
        };
    }
}
