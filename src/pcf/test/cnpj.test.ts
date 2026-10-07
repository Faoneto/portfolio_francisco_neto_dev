import { evaluateCnpj, isValidCnpj, maskCnpj, normalizeCnpj } from "../CnpjInput/cnpj";

describe("CnpjInput helpers", () => {
  it("masks progressively while typing", () => {
    expect(maskCnpj("11")).toBe("11");
    expect(maskCnpj("11222")).toBe("11.222");
    expect(maskCnpj("11222333")).toBe("11.222.333");
    expect(maskCnpj("112223330001")).toBe("11.222.333/0001");
    expect(maskCnpj("11222333000181")).toBe("11.222.333/0001-81");
    expect(maskCnpj("12abc34501de35")).toBe("12.ABC.345/01DE-35");
  });

  it("truncates extra characters", () => {
    expect(normalizeCnpj("11.222.333/0001-8199")).toBe("11222333000181");
  });

  it("evaluates states", () => {
    expect(evaluateCnpj("")).toBe("empty");
    expect(evaluateCnpj("11.222")).toBe("incomplete");
    expect(evaluateCnpj("11.222.333/0001-81")).toBe("valid");
    expect(evaluateCnpj("11.222.333/0001-82")).toBe("invalid");
  });

  it("agrees with the official alphanumeric example", () => {
    expect(isValidCnpj("12.ABC.345/01DE-35")).toBe(true);
    expect(isValidCnpj("AAAAAAAAAAAA00")).toBe(false);
  });
});
