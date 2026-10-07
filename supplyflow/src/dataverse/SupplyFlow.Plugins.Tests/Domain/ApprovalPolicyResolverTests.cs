using System;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Domain;

public class ApprovalPolicyResolverTests
{
    private static readonly ApprovalPolicyRule[] Matrix =
    {
        new(ApprovalLevel.Manager, 0m),
        new(ApprovalLevel.Director, 10_000m),
        new(ApprovalLevel.Cfo, 100_000m),
        new(ApprovalLevel.Director, 1_000m, MaterialGroup.InformationTechnology),
    };

    [Fact]
    public void Small_amount_needs_manager_only()
    {
        var decision = ApprovalPolicyResolver.Resolve(500m, new[] { MaterialGroup.Mro }, Matrix, availableBudget: 50_000m);

        Assert.Equal(ApprovalLevel.Manager, decision.Level);
        Assert.False(decision.IsOverBudget);
    }

    [Fact]
    public void Threshold_is_inclusive()
    {
        var decision = ApprovalPolicyResolver.Resolve(10_000m, new[] { MaterialGroup.Mro }, Matrix, 50_000m);

        Assert.Equal(ApprovalLevel.Director, decision.Level);
    }

    [Fact]
    public void Group_scoped_policy_applies_only_when_group_is_present()
    {
        var withIt = ApprovalPolicyResolver.Resolve(2_000m, new[] { MaterialGroup.Mro, MaterialGroup.InformationTechnology }, Matrix, 50_000m);
        var withoutIt = ApprovalPolicyResolver.Resolve(2_000m, new[] { MaterialGroup.Mro }, Matrix, 50_000m);

        Assert.Equal(ApprovalLevel.Director, withIt.Level);
        Assert.Equal(ApprovalLevel.Manager, withoutIt.Level);
    }

    [Fact]
    public void Over_budget_always_escalates_to_cfo()
    {
        var decision = ApprovalPolicyResolver.Resolve(5_000m, new[] { MaterialGroup.Mro }, Matrix, availableBudget: 4_999.99m);

        Assert.Equal(ApprovalLevel.Cfo, decision.Level);
        Assert.True(decision.IsOverBudget);
        Assert.Contains(decision.Reasons, r => r.Contains("excede o orçamento"));
    }

    [Fact]
    public void No_policies_defaults_to_manager()
    {
        var decision = ApprovalPolicyResolver.Resolve(1_000_000m, Array.Empty<MaterialGroup>(), Array.Empty<ApprovalPolicyRule>(), 2_000_000m);

        Assert.Equal(ApprovalLevel.Manager, decision.Level);
    }
}
