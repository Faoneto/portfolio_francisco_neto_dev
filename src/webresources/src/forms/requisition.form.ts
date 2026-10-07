import {
  ApprovalLevelLabels,
  editableStages,
  FormType,
  IntegrationStatus,
  Requisition,
  RequisitionStage,
  SupplierStatus,
} from "../shared/schema";
import { controlNotification, getChoice, getText, setDisabled, setRequired, setVisible, startOfToday } from "../shared/form";

/**
 * Purchase requisition main form.
 * Register in the form properties with "Pass execution context as first parameter":
 *   OnLoad  -> SupplyFlow.RequisitionForm.onLoad
 *   OnSave  -> SupplyFlow.RequisitionForm.onSave
 *
 * The server (RequisitionLifecyclePlugin) enforces every rule; the form only gives faster feedback.
 */

const NOTIFICATION = {
  stage: "fno_stage_info",
  overBudget: "fno_over_budget",
  integration: "fno_integration",
  needBy: "fno_needby",
  cancel: "fno_cancel_reason",
} as const;

const HEADER_FIELDS = [
  Requisition.supplier,
  Requisition.costCenter,
  Requisition.needByDate,
  Requisition.justification,
];

const supplierFilter =
  `<filter type="and">` +
  `<condition attribute="fno_issupplier" operator="eq" value="1" />` +
  `<condition attribute="fno_supplierstatus" operator="eq" value="${SupplierStatus.Approved}" />` +
  `<condition attribute="statecode" operator="eq" value="0" />` +
  `</filter>`;

export function onLoad(executionContext: Xrm.Events.EventContext): void {
  const form = executionContext.getFormContext();

  // Only approved, active suppliers can be selected.
  form.getControl<Xrm.Controls.LookupControl>(Requisition.supplier)?.addPreSearch(() => {
    form.getControl<Xrm.Controls.LookupControl>(Requisition.supplier)?.addCustomFilter(supplierFilter, "account");
  });

  form.getAttribute(Requisition.needByDate)?.addOnChange(onNeedByDateChange);
  form.getAttribute(Requisition.stage)?.addOnChange(onStageChange);

  applyStageRules(form);
}

export function onNeedByDateChange(executionContext: Xrm.Events.EventContext): void {
  validateNeedByDate(executionContext.getFormContext());
}

export function onStageChange(executionContext: Xrm.Events.EventContext): void {
  applyStageRules(executionContext.getFormContext());
}

/**
 * Blocks the save when the requisition is being cancelled without a reason, or when the need-by date is in the past.
 * Uses getEventArgs().preventDefault() – the supported replacement for "return false" in legacy scripts.
 */
export function onSave(executionContext: Xrm.Events.SaveEventContext): void {
  const form = executionContext.getFormContext();
  const args = executionContext.getEventArgs();
  const stage = getChoice(form, Requisition.stage);

  if (stage === RequisitionStage.Cancelled && getText(form, Requisition.cancellationReason).length === 0) {
    args.preventDefault();
    setVisible(form, Requisition.cancellationReason, true);
    setRequired(form, Requisition.cancellationReason, true);
    controlNotification(form, Requisition.cancellationReason, "Informe o motivo do cancelamento para salvar.", NOTIFICATION.cancel);
    form.getControl<Xrm.Controls.StandardControl>(Requisition.cancellationReason)?.setFocus();
    return;
  }

  if (stage !== null && editableStages.has(stage) && !validateNeedByDate(form)) {
    args.preventDefault();
  }
}

export function applyStageRules(form: Xrm.FormContext): void {
  const stage = getChoice(form, Requisition.stage) ?? RequisitionStage.Draft;
  const editable = editableStages.has(stage);

  HEADER_FIELDS.forEach((field) => setDisabled(form, field, !editable));

  const isCancelling = stage === RequisitionStage.Cancelled;
  setVisible(form, Requisition.cancellationReason, isCancelling || getText(form, Requisition.cancellationReason).length > 0);
  setRequired(form, Requisition.cancellationReason, isCancelling);
  setDisabled(form, Requisition.cancellationReason, !isCancelling || form.ui.getFormType() !== FormType.Update);

  form.ui.clearFormNotification(NOTIFICATION.stage);
  form.ui.clearFormNotification(NOTIFICATION.overBudget);
  form.ui.clearFormNotification(NOTIFICATION.integration);

  if (stage === RequisitionStage.PendingApproval) {
    const level = ApprovalLevelLabels[getChoice(form, Requisition.approvalLevel) ?? 0] ?? "—";
    form.ui.setFormNotification(`Aguardando aprovação: ${level}. Itens bloqueados para edição.`, "INFO", NOTIFICATION.stage);
  }

  if (stage === RequisitionStage.Rejected) {
    form.ui.setFormNotification("Requisição rejeitada. Ajuste os itens e envie novamente.", "WARNING", NOTIFICATION.stage);
  }

  if (form.getAttribute<Xrm.Attributes.BooleanAttribute>(Requisition.isOverBudget)?.getValue() === true) {
    form.ui.setFormNotification("Valor acima do saldo do centro de custo – exige aprovação do CFO.", "WARNING", NOTIFICATION.overBudget);
  }

  if (getChoice(form, Requisition.integrationStatus) === IntegrationStatus.Failed) {
    const message = getText(form, Requisition.integrationMessage) || "Erro não detalhado.";
    form.ui.setFormNotification(`Falha na integração com o SAP: ${message}`, "ERROR", NOTIFICATION.integration);
  }
}

export function validateNeedByDate(form: Xrm.FormContext, now: Date = new Date()): boolean {
  const value = form.getAttribute<Xrm.Attributes.DateAttribute>(Requisition.needByDate)?.getValue();
  const invalid = value !== null && value !== undefined && value < startOfToday(now);
  controlNotification(form, Requisition.needByDate, invalid ? "A data de necessidade não pode estar no passado." : null, NOTIFICATION.needBy);
  return !invalid;
}
