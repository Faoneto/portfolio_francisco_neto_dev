import { applyStageRules, onLoad, onSave, validateNeedByDate } from "../src/forms/requisition.form";
import { IntegrationStatus, Requisition, RequisitionStage } from "../src/shared/schema";
import { FakeForm } from "./fakeForm";

function form(stage: RequisitionStage, extra: Record<string, unknown> = {}): FakeForm {
  return new FakeForm({
    [Requisition.stage]: stage,
    [Requisition.supplier]: null,
    [Requisition.costCenter]: null,
    [Requisition.needByDate]: null,
    [Requisition.justification]: "Reposição",
    [Requisition.cancellationReason]: null,
    [Requisition.approvalLevel]: null,
    [Requisition.isOverBudget]: false,
    [Requisition.integrationStatus]: IntegrationStatus.NotSent,
    [Requisition.integrationMessage]: null,
    ...extra,
  });
}

describe("requisition form", () => {
  it("filters suppliers to approved and active accounts", () => {
    const f = form(RequisitionStage.Draft);
    onLoad(f.context());

    const supplier = f.getControl(Requisition.supplier)!;
    supplier.preSearch.forEach((h) => h());

    expect(supplier.customFilters).toHaveLength(1);
    expect(supplier.customFilters[0].entity).toBe("account");
    expect(supplier.customFilters[0].filter).toContain('attribute="fno_supplierstatus" operator="eq" value="100000001"');
  });

  it("locks header fields and explains the wait while pending approval", () => {
    const f = form(RequisitionStage.PendingApproval, { [Requisition.approvalLevel]: 100000002 });
    applyStageRules(f.asXrm());

    expect(f.getControl(Requisition.supplier)!.disabled).toBe(true);
    expect(f.getControl(Requisition.justification)!.disabled).toBe(true);
    expect(f.formNotifications.get("fno_stage_info")?.message).toContain("Diretor");
  });

  it("keeps header fields editable in draft", () => {
    const f = form(RequisitionStage.Draft);
    applyStageRules(f.asXrm());

    expect(f.getControl(Requisition.supplier)!.disabled).toBe(false);
    expect(f.getControl(Requisition.cancellationReason)!.visible).toBe(false);
  });

  it("shows over-budget and integration failures", () => {
    const f = form(RequisitionStage.SentToSap, {
      [Requisition.isOverBudget]: true,
      [Requisition.integrationStatus]: IntegrationStatus.Failed,
      [Requisition.integrationMessage]: "Fornecedor bloqueado no SAP (M8 082)",
    });
    applyStageRules(f.asXrm());

    expect(f.formNotifications.get("fno_over_budget")?.level).toBe("WARNING");
    expect(f.formNotifications.get("fno_integration")?.message).toContain("M8 082");
  });

  it("prevents saving a cancellation without reason", () => {
    const f = form(RequisitionStage.Cancelled);
    const { ctx, prevented } = f.saveContext();

    onSave(ctx);

    expect(prevented()).toBe(true);
    expect(f.getAttribute(Requisition.cancellationReason)!.requiredLevel).toBe("required");
    expect(f.getControl(Requisition.cancellationReason)!.focused).toBe(true);
    expect(f.getControl(Requisition.cancellationReason)!.notifications.size).toBe(1);
  });

  it("allows saving a cancellation with reason", () => {
    const f = form(RequisitionStage.Cancelled, { [Requisition.cancellationReason]: "Compra duplicada" });
    const { ctx, prevented } = f.saveContext();

    onSave(ctx);

    expect(prevented()).toBe(false);
  });

  it("prevents saving a draft with need-by date in the past", () => {
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const f = form(RequisitionStage.Draft, { [Requisition.needByDate]: yesterday });
    const { ctx, prevented } = f.saveContext();

    onSave(ctx);

    expect(prevented()).toBe(true);
    expect(f.getControl(Requisition.needByDate)!.notifications.get("fno_needby")).toContain("passado");
  });

  it("accepts today as need-by date", () => {
    const now = new Date(2026, 9, 7, 15, 30);
    const f = form(RequisitionStage.Draft, { [Requisition.needByDate]: new Date(2026, 9, 7) });

    expect(validateNeedByDate(f.asXrm(), now)).toBe(true);
  });
});
