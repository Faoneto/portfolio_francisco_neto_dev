using System.Collections.Generic;
using System.Linq;
using SupplyFlow.Plugins.Model;

namespace SupplyFlow.Plugins.Domain;

/// <summary>An active row of fno_approvalpolicy (approval authority matrix / "alçada").</summary>
public sealed record ApprovalPolicyRule(ApprovalLevel Level, decimal MinAmount, MaterialGroup? MaterialGroup = null);

public sealed record ApprovalDecision(ApprovalLevel Level, bool IsOverBudget, IReadOnlyList<string> Reasons);

/// <summary>
/// Resolves the approval level required by a requisition from the authority matrix and the cost-center budget.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="number">
///   <item>Every requisition needs at least the cost-center manager (Manager).</item>
///   <item>The highest level among the policies whose threshold is reached wins.
///         A policy scoped to a material group only applies if the requisition contains that group.</item>
///   <item>Exceeding the available budget always escalates to CFO.</item>
/// </list>
/// </remarks>
public static class ApprovalPolicyResolver
{
    public static ApprovalDecision Resolve(
        decimal totalAmount,
        IEnumerable<MaterialGroup> materialGroups,
        IEnumerable<ApprovalPolicyRule> policies,
        decimal availableBudget)
    {
        var groups = new HashSet<MaterialGroup>(materialGroups);
        var reasons = new List<string>();
        var level = ApprovalLevel.Manager;

        var matched = policies
            .Where(p => totalAmount >= p.MinAmount)
            .Where(p => p.MaterialGroup is null || groups.Contains(p.MaterialGroup.Value))
            .OrderByDescending(p => p.Level)
            .FirstOrDefault();

        if (matched is not null && matched.Level > level)
        {
            level = matched.Level;
            var scope = matched.MaterialGroup is null ? string.Empty : $" para o grupo {Describe(matched.MaterialGroup.Value)}";
            reasons.Add($"Valor {totalAmount:N2} atinge a alçada de {Describe(matched.Level)}{scope} (a partir de {matched.MinAmount:N2}).");
        }

        var isOverBudget = totalAmount > availableBudget;
        if (isOverBudget)
        {
            level = ApprovalLevel.Cfo;
            reasons.Add($"Valor {totalAmount:N2} excede o orçamento disponível do centro de custo ({availableBudget:N2}).");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("Dentro da alçada do gestor do centro de custo.");
        }

        return new ApprovalDecision(level, isOverBudget, reasons);
    }

    public static string Describe(ApprovalLevel level) => level switch
    {
        ApprovalLevel.Manager => "Gestor do Centro de Custo",
        ApprovalLevel.Director => "Diretor",
        ApprovalLevel.Cfo => "CFO",
        _ => level.ToString(),
    };

    public static string Describe(MaterialGroup group) => group switch
    {
        MaterialGroup.Mro => "MRO",
        MaterialGroup.RawMaterial => "Matéria-prima",
        MaterialGroup.Packaging => "Embalagem",
        MaterialGroup.Services => "Serviços",
        MaterialGroup.InformationTechnology => "TI",
        _ => group.ToString(),
    };
}
