/* Minimal in-memory implementation of the parts of Xrm.FormContext used by the scripts. */

export class FakeControl {
  disabled = false;
  visible = true;
  notifications = new Map<string, string>();
  focused = false;
  preSearch: Array<() => void> = [];
  customFilters: Array<{ filter: string; entity?: string }> = [];

  setDisabled(value: boolean): void {
    this.disabled = value;
  }
  setVisible(value: boolean): void {
    this.visible = value;
  }
  setNotification(message: string, id: string): boolean {
    this.notifications.set(id, message);
    return true;
  }
  clearNotification(id: string): boolean {
    return this.notifications.delete(id);
  }
  setFocus(): void {
    this.focused = true;
  }
  addPreSearch(handler: () => void): void {
    this.preSearch.push(handler);
  }
  addCustomFilter(filter: string, entity?: string): void {
    this.customFilters.push({ filter, entity });
  }
}

export class FakeAttribute {
  readonly controls: { forEach: (cb: (c: FakeControl) => void) => void };
  requiredLevel = "none";
  onChange: Array<(ctx: unknown) => void> = [];

  constructor(public value: unknown, public readonly control = new FakeControl()) {
    this.controls = { forEach: (cb) => cb(this.control) };
  }

  getValue(): unknown {
    return this.value;
  }
  setValue(value: unknown): void {
    this.value = value;
  }
  setRequiredLevel(level: string): void {
    this.requiredLevel = level;
  }
  addOnChange(handler: (ctx: unknown) => void): void {
    this.onChange.push(handler);
  }
}

export class FakeForm {
  readonly attributes = new Map<string, FakeAttribute>();
  readonly formNotifications = new Map<string, { message: string; level: string }>();
  readonly sections = new Map<string, { visible: boolean }>();
  formType = 2;
  dirty = false;
  saved = 0;
  refreshed = 0;
  id = "{6F9619FF-8B86-D011-B42D-00C04FC964FF}";

  readonly ui = {
    getFormType: () => this.formType,
    setFormNotification: (message: string, level: string, id: string) => {
      this.formNotifications.set(id, { message, level });
      return true;
    },
    clearFormNotification: (id: string) => this.formNotifications.delete(id),
    tabs: {
      get: (tab: string) => ({
        sections: {
          get: (section: string) => {
            const key = `${tab}/${section}`;
            if (!this.sections.has(key)) {
              this.sections.set(key, { visible: true });
            }
            const state = this.sections.get(key)!;
            return { setVisible: (v: boolean) => (state.visible = v) };
          },
        },
      }),
    },
  };

  readonly data = {
    entity: { getIsDirty: () => this.dirty, getId: () => this.id },
    save: async () => {
      this.saved++;
    },
    refresh: async () => {
      this.refreshed++;
    },
  };

  constructor(values: Record<string, unknown>) {
    Object.entries(values).forEach(([name, value]) => this.attributes.set(name, new FakeAttribute(value)));
  }

  getAttribute(name: string): FakeAttribute | null {
    return this.attributes.get(name) ?? null;
  }

  getControl(name: string): FakeControl | null {
    return this.attributes.get(name)?.control ?? null;
  }

  asXrm(): Xrm.FormContext {
    return this as unknown as Xrm.FormContext;
  }

  context(): Xrm.Events.EventContext {
    return { getFormContext: () => this.asXrm() } as unknown as Xrm.Events.EventContext;
  }

  saveContext(): { ctx: Xrm.Events.SaveEventContext; prevented: () => boolean } {
    let prevented = false;
    const ctx = {
      getFormContext: () => this.asXrm(),
      getEventArgs: () => ({ preventDefault: () => (prevented = true) }),
    } as unknown as Xrm.Events.SaveEventContext;
    return { ctx, prevented: () => prevented };
  }
}
