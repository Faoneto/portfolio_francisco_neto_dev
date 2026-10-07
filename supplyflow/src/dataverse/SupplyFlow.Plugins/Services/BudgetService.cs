using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Model;

namespace SupplyFlow.Plugins.Services;

public sealed record BudgetSnapshot(decimal AnnualBudget, decimal CommittedAmount)
{
    public decimal Available => AnnualBudget - CommittedAmount;
}

/// <summary>
/// Reads and adjusts the committed amount of a cost center using optimistic concurrency.
/// </summary>
/// <remarks>
/// Two requisitions approved at the same time for the same cost center would otherwise read the same
/// committed value and the last write would win (lost update). Updating with
/// <see cref="ConcurrencyBehavior.IfRowVersionMatches"/> makes the second writer fail with
/// ConcurrencyVersionMismatch; we then re-read and re-apply the delta.
/// </remarks>
public sealed class BudgetService
{
    public const int MaxAttempts = 3;

    private static readonly ColumnSet BudgetColumns = new(
        Schema.CostCenter.AnnualBudget,
        Schema.CostCenter.CommittedAmount);

    private readonly IOrganizationService _service;
    private readonly Action<string>? _trace;

    public BudgetService(IOrganizationService service, Action<string>? trace = null)
    {
        _service = service;
        _trace = trace;
    }

    public BudgetSnapshot GetSnapshot(Guid costCenterId)
    {
        var costCenter = _service.Retrieve(Schema.CostCenter.EntityName, costCenterId, BudgetColumns);
        return ToSnapshot(costCenter);
    }

    /// <summary>Adds <paramref name="delta"/> (negative to release) to the committed amount.</summary>
    public BudgetSnapshot AdjustCommitted(Guid costCenterId, decimal delta)
    {
        for (var attempt = 1; ; attempt++)
        {
            var current = _service.Retrieve(Schema.CostCenter.EntityName, costCenterId, BudgetColumns);
            var snapshot = ToSnapshot(current);
            var committed = Math.Max(0m, snapshot.CommittedAmount + delta);

            var update = new Entity(Schema.CostCenter.EntityName, costCenterId)
            {
                RowVersion = current.RowVersion,
                [Schema.CostCenter.CommittedAmount] = new Money(committed),
                [Schema.CostCenter.AvailableBudget] = new Money(snapshot.AnnualBudget - committed),
            };

            try
            {
                _service.Execute(new UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = string.IsNullOrEmpty(current.RowVersion)
                        ? ConcurrencyBehavior.Default
                        : ConcurrencyBehavior.IfRowVersionMatches,
                });

                _trace?.Invoke($"Cost center {costCenterId}: committed {snapshot.CommittedAmount:N2} -> {committed:N2} (attempt {attempt}).");
                return new BudgetSnapshot(snapshot.AnnualBudget, committed);
            }
            catch (FaultException<OrganizationServiceFault> fault)
                when (fault.Detail?.ErrorCode == DataverseErrorCodes.ConcurrencyVersionMismatch && attempt < MaxAttempts)
            {
                _trace?.Invoke($"Cost center {costCenterId}: row version changed, retrying ({attempt}/{MaxAttempts}).");
            }
        }
    }

    private static BudgetSnapshot ToSnapshot(Entity costCenter)
    {
        return new BudgetSnapshot(
            costCenter.GetMoney(Schema.CostCenter.AnnualBudget),
            costCenter.GetMoney(Schema.CostCenter.CommittedAmount));
    }
}
