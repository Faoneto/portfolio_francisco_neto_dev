using System.Collections.Generic;
using SupplyFlow.Plugins.Model;

namespace SupplyFlow.Plugins.Domain;

/// <summary>
/// Allowed lifecycle transitions of a purchase requisition (fno_stage).
/// </summary>
/// <remarks>
/// <code>
/// Draft ──submit──▶ PendingApproval ──▶ Approved ──▶ SentToSap ──▶ PurchaseOrderCreated
///   ▲                  │   │    ▲                       │
///   └──── recall ──────┘   ▼    │ resubmit              └── reprocess (admin) ──▶ Approved
///                        Rejected ──── rework ──▶ Draft
/// Draft / PendingApproval / Rejected / Approved ──▶ Cancelled
/// </code>
/// </remarks>
public static class RequisitionStateMachine
{
    private static readonly IReadOnlyDictionary<RequisitionStage, RequisitionStage[]> Transitions =
        new Dictionary<RequisitionStage, RequisitionStage[]>
        {
            [RequisitionStage.Draft] = new[] { RequisitionStage.PendingApproval, RequisitionStage.Cancelled },
            [RequisitionStage.PendingApproval] = new[]
            {
                RequisitionStage.Approved, RequisitionStage.Rejected, RequisitionStage.Draft, RequisitionStage.Cancelled,
            },
            [RequisitionStage.Rejected] = new[]
            {
                RequisitionStage.PendingApproval, RequisitionStage.Draft, RequisitionStage.Cancelled,
            },
            [RequisitionStage.Approved] = new[] { RequisitionStage.SentToSap, RequisitionStage.Cancelled },
            [RequisitionStage.SentToSap] = new[] { RequisitionStage.PurchaseOrderCreated, RequisitionStage.Approved },
            [RequisitionStage.PurchaseOrderCreated] = new RequisitionStage[0],
            [RequisitionStage.Cancelled] = new RequisitionStage[0],
        };

    public static bool CanTransition(RequisitionStage from, RequisitionStage to)
    {
        return from == to || (Transitions.TryGetValue(from, out var allowed) && System.Array.IndexOf(allowed, to) >= 0);
    }

    /// <summary>Lines and header data can be edited only while the requisition is being prepared.</summary>
    public static bool IsEditable(RequisitionStage stage)
    {
        return stage is RequisitionStage.Draft or RequisitionStage.Rejected;
    }

    public static bool IsTerminal(RequisitionStage stage)
    {
        return stage is RequisitionStage.PurchaseOrderCreated or RequisitionStage.Cancelled;
    }

    public static string Describe(RequisitionStage stage) => stage switch
    {
        RequisitionStage.Draft => "Rascunho",
        RequisitionStage.PendingApproval => "Em aprovação",
        RequisitionStage.Approved => "Aprovada",
        RequisitionStage.Rejected => "Rejeitada",
        RequisitionStage.SentToSap => "Enviada ao SAP",
        RequisitionStage.PurchaseOrderCreated => "Pedido criado",
        RequisitionStage.Cancelled => "Cancelada",
        _ => stage.ToString(),
    };
}
