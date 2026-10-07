import { onCnpjChange, onLoad } from "../src/forms/supplier.form";
import { Account } from "../src/shared/schema";
import { FakeForm } from "./fakeForm";

function form(values: Record<string, unknown>): FakeForm {
  return new FakeForm({ [Account.isSupplier]: false, [Account.cnpj]: null, [Account.supplierStatus]: null, ...values });
}

describe("supplier form", () => {
  it("shows the supplier section and requires CNPJ only for suppliers", () => {
    const f = form({ [Account.isSupplier]: true });
    onLoad(f.context());

    expect(f.sections.get("tab_supplier/section_supplier")?.visible).toBe(true);
    expect(f.getAttribute(Account.cnpj)!.requiredLevel).toBe("required");
  });

  it("hides the supplier section for customers", () => {
    const f = form({ [Account.isSupplier]: false });
    onLoad(f.context());

    expect(f.sections.get("tab_supplier/section_supplier")?.visible).toBe(false);
  });

  it("normalizes a valid masked CNPJ", () => {
    const f = form({ [Account.cnpj]: "12.abc.345/01de-35" });
    onCnpjChange(f.context());

    expect(f.getAttribute(Account.cnpj)!.value).toBe("12ABC34501DE35");
    expect(f.getControl(Account.cnpj)!.notifications.size).toBe(0);
  });

  it("flags an invalid CNPJ without changing it", () => {
    const f = form({ [Account.cnpj]: "11222333000182" });
    onCnpjChange(f.context());

    expect(f.getAttribute(Account.cnpj)!.value).toBe("11222333000182");
    expect(f.getControl(Account.cnpj)!.notifications.get("fno_cnpj_invalid")).toContain("11.222.333/0001-82");
  });

  it("clears the notification when CNPJ is emptied", () => {
    const f = form({ [Account.cnpj]: null });
    f.getControl(Account.cnpj)!.notifications.set("fno_cnpj_invalid", "x");
    onCnpjChange(f.context());

    expect(f.getControl(Account.cnpj)!.notifications.size).toBe(0);
  });
});
