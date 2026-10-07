using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;

namespace SupplyFlow.Plugins.Plugins.Requisition;

/// <summary>
/// Prices a requisition line and validates it against the header and the supplier.
/// </summary>
/// <remarks>
/// Registration: fno_requisitionline | Create, Update (filtering: fno_materialid, fno_quantity, fno_unitprice, fno_requisitionid)
/// | PreOperation | Sync. PreImage (Update): fno_requisitionid, fno_materialid, fno_quantity, fno_unitprice.
/// <para>
/// PreOperation lets us write calculated columns directly into the Target, avoiding a second Update
/// (and the extra pipeline execution it would trigger).
/// </para>
/// </remarks>
public sealed class RequisitionLinePricingPlugin : PluginBase
{
    public RequisitionLinePricingPlugin()
        : this(null, null)
    {
    }

    /// <summary>Constructor used by Dataverse when the step has unsecure/secure configuration.</summary>
    public RequisitionLinePricingPlugin(string? unsecureConfiguration, string? secureConfiguration)
        : base(unsecureConfiguration, secureConfiguration)
    {
    }

    protected override void ExecuteDataversePlugin(LocalPluginContext context)
    {
        context.EnsureRegistration(Schema.RequisitionLine.EntityName, PluginStage.PreOperation, MessageNames.Create, MessageNames.Update);

        var target = context.GetTarget();
        var line = context.MessageName == MessageNames.Update ? context.GetMergedTarget() : target;

        var requisitionRef = line.GetAttributeValue<EntityReference>(Schema.RequisitionLine.RequisitionId)
            ?? throw new InvalidPluginExecutionException("O item deve estar vinculado a uma requisição de compra.");
        var materialRef = line.GetAttributeValue<EntityReference>(Schema.RequisitionLine.MaterialId)
            ?? throw new InvalidPluginExecutionException("Informe o material do item.");

        var quantity = line.GetAttributeValue<decimal?>(Schema.RequisitionLine.Quantity) ?? 0m;
        if (quantity <= 0)
        {
            throw new InvalidPluginExecutionException("A quantidade do item deve ser maior que zero.");
        }

        var repository = new RequisitionRepository(context.SystemService);
        var requisition = repository.GetRequisition(requisitionRef.Id, Schema.Requisition.Stage, Schema.Requisition.SupplierId);
        var stage = requisition.GetChoice<RequisitionStage>(Schema.Requisition.Stage) ?? RequisitionStage.Draft;

        if (!RequisitionStateMachine.IsEditable(stage))
        {
            throw new InvalidPluginExecutionException(
                $"Itens não podem ser alterados com a requisição em \"{RequisitionStateMachine.Describe(stage)}\". " +
                "Somente requisições em Rascunho ou Rejeitadas aceitam alterações.");
        }

        var material = context.SystemService.Retrieve(
            Schema.Material.EntityName,
            materialRef.Id,
            new ColumnSet(Schema.Material.Name, Schema.Material.StandardPrice, Schema.Material.MaterialGroup));

        EnsureMaterialApprovedForSupplier(context, repository, requisition, material);

        var unitPrice = line.GetAttributeValue<Money>(Schema.RequisitionLine.UnitPrice)?.Value
            ?? material.GetMoney(Schema.Material.StandardPrice);
        if (unitPrice < 0)
        {
            throw new InvalidPluginExecutionException("O preço unitário não pode ser negativo.");
        }

        target[Schema.RequisitionLine.UnitPrice] = new Money(unitPrice);
        target[Schema.RequisitionLine.LineTotal] = new Money(Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero));
        target[Schema.RequisitionLine.Name] = material.GetAttributeValue<string>(Schema.Material.Name);
        target[Schema.RequisitionLine.MaterialGroup] = material.GetAttributeValue<OptionSetValue>(Schema.Material.MaterialGroup);
    }

    private static void EnsureMaterialApprovedForSupplier(
        LocalPluginContext context, RequisitionRepository repository, Entity requisition, Entity material)
    {
        var supplier = requisition.GetAttributeValue<EntityReference>(Schema.Requisition.SupplierId);
        if (supplier is null)
        {
            return;
        }

        var variables = new EnvironmentVariableService(context.SystemService, context.ExecutionContext.OrganizationId);
        if (!variables.GetBoolean(Schema.EnvironmentVariables.RequireSupplierMaterialApproval, defaultValue: true))
        {
            return;
        }

        if (!repository.IsMaterialApprovedForSupplier(supplier.Id, material.Id))
        {
            throw new InvalidPluginExecutionException(
                $"O material \"{material.GetAttributeValue<string>(Schema.Material.Name)}\" não está homologado " +
                $"para o fornecedor \"{supplier.Name}\". Solicite a homologação ao time de Suprimentos.");
        }
    }
}
