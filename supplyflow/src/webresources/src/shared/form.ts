/** Small typed helpers over the Client API to keep handlers readable. */

export function getChoice(form: Xrm.FormContext, attribute: string): number | null {
  return form.getAttribute<Xrm.Attributes.OptionSetAttribute>(attribute)?.getValue() ?? null;
}

export function getText(form: Xrm.FormContext, attribute: string): string {
  return form.getAttribute<Xrm.Attributes.StringAttribute>(attribute)?.getValue()?.trim() ?? "";
}

export function setDisabled(form: Xrm.FormContext, attribute: string, disabled: boolean): void {
  form.getAttribute(attribute)?.controls.forEach((c) => (c as Xrm.Controls.StandardControl).setDisabled(disabled));
}

export function setVisible(form: Xrm.FormContext, attribute: string, visible: boolean): void {
  form.getAttribute(attribute)?.controls.forEach((c) => c.setVisible(visible));
}

export function setRequired(form: Xrm.FormContext, attribute: string, required: boolean): void {
  form.getAttribute(attribute)?.setRequiredLevel(required ? "required" : "none");
}

export function controlNotification(form: Xrm.FormContext, attribute: string, message: string | null, id: string): void {
  const control = form.getControl<Xrm.Controls.StandardControl>(attribute);
  if (!control) {
    return;
  }

  if (message) {
    control.setNotification(message, id);
  } else {
    control.clearNotification(id);
  }
}

export function startOfToday(now: Date = new Date()): Date {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}
