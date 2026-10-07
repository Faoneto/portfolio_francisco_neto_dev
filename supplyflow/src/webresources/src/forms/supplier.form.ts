import { Account } from "../shared/schema";
import { formatCnpj, isValidCnpj, normalizeCnpj } from "../shared/cnpj";
import { controlNotification, setRequired } from "../shared/form";

/**
 * Account (supplier) main form.
 *   OnLoad -> SupplyFlow.SupplierForm.onLoad
 * The CNPJ field can alternatively use the CnpjInput PCF control (src/pcf/CnpjInput).
 */

const SUPPLIER_SECTION = { tab: "tab_supplier", section: "section_supplier" } as const;
const NOTIFICATION_ID = "fno_cnpj_invalid";

export function onLoad(executionContext: Xrm.Events.EventContext): void {
  const form = executionContext.getFormContext();
  form.getAttribute(Account.isSupplier)?.addOnChange(onIsSupplierChange);
  form.getAttribute(Account.cnpj)?.addOnChange(onCnpjChange);
  applySupplierRules(form);
}

export function onIsSupplierChange(executionContext: Xrm.Events.EventContext): void {
  applySupplierRules(executionContext.getFormContext());
}

export function onCnpjChange(executionContext: Xrm.Events.EventContext): void {
  const form = executionContext.getFormContext();
  const attribute = form.getAttribute<Xrm.Attributes.StringAttribute>(Account.cnpj);
  const raw = attribute?.getValue();

  if (!raw) {
    controlNotification(form, Account.cnpj, null, NOTIFICATION_ID);
    return;
  }

  if (!isValidCnpj(raw)) {
    controlNotification(form, Account.cnpj, `CNPJ inválido: ${formatCnpj(raw)}`, NOTIFICATION_ID);
    return;
  }

  controlNotification(form, Account.cnpj, null, NOTIFICATION_ID);
  // Store the canonical value (same as the server plugin) so the alternate key matches.
  const normalized = normalizeCnpj(raw);
  if (normalized !== raw) {
    attribute?.setValue(normalized);
  }
}

export function applySupplierRules(form: Xrm.FormContext): void {
  const isSupplier = form.getAttribute<Xrm.Attributes.BooleanAttribute>(Account.isSupplier)?.getValue() === true;
  form.ui.tabs.get(SUPPLIER_SECTION.tab)?.sections.get(SUPPLIER_SECTION.section)?.setVisible(isSupplier);
  setRequired(form, Account.cnpj, isSupplier);
  setRequired(form, Account.supplierStatus, isSupplier);
}
