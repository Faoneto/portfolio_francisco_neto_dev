import { formatCnpj, isValidCnpj, normalizeCnpj } from "../src/shared/cnpj";

describe("CNPJ", () => {
  it.each(["11222333000181", "11.222.333/0001-81", "12ABC34501DE35", "12.abc.345/01de-35"])("accepts %s", (value) => {
    expect(isValidCnpj(value)).toBe(true);
  });

  it.each(["", "11222333000182", "00000000000000", "12ABC34501DE3A", "1122233300018"])("rejects '%s'", (value) => {
    expect(isValidCnpj(value)).toBe(false);
  });

  it("normalizes and formats", () => {
    expect(normalizeCnpj("12.abc.345/01de-35")).toBe("12ABC34501DE35");
    expect(formatCnpj("11222333000181")).toBe("11.222.333/0001-81");
    expect(formatCnpj("123")).toBe("123");
  });
});
