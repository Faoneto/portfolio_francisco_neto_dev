/**
 * CNPJ helpers for the PCF control. Same algorithm as SupplyFlow.Plugins/Domain/Cnpj.cs and
 * src/webresources/src/shared/cnpj.ts (numeric + alphanumeric CNPJ, IN RFB 2.229/2024).
 */
const FIRST_WEIGHTS = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
const SECOND_WEIGHTS = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

export function normalizeCnpj(value: string | null | undefined): string {
  return (value ?? "").replace(/[^0-9a-z]/gi, "").toUpperCase().slice(0, 14);
}

function checkDigit(base: string, weights: number[]): number {
  const sum = weights.reduce((acc, weight, i) => acc + (base.charCodeAt(i) - 48) * weight, 0);
  const remainder = sum % 11;
  return remainder < 2 ? 0 : 11 - remainder;
}

export function isValidCnpj(value: string | null | undefined): boolean {
  const cnpj = normalizeCnpj(value);
  if (!/^[0-9A-Z]{12}[0-9]{2}$/.test(cnpj) || /^(.)\1{13}$/.test(cnpj)) {
    return false;
  }

  return checkDigit(cnpj, FIRST_WEIGHTS) === Number(cnpj[12]) && checkDigit(cnpj, SECOND_WEIGHTS) === Number(cnpj[13]);
}

/** Progressive mask applied while typing: 12.ABC.345/01DE-35 */
export function maskCnpj(value: string | null | undefined): string {
  const c = normalizeCnpj(value);
  const parts = [c.slice(0, 2), c.slice(2, 5), c.slice(5, 8), c.slice(8, 12), c.slice(12, 14)];
  let masked = parts[0];
  if (parts[1]) masked += `.${parts[1]}`;
  if (parts[2]) masked += `.${parts[2]}`;
  if (parts[3]) masked += `/${parts[3]}`;
  if (parts[4]) masked += `-${parts[4]}`;
  return masked;
}

export type CnpjState = "empty" | "incomplete" | "valid" | "invalid";

export function evaluateCnpj(value: string | null | undefined): CnpjState {
  const cnpj = normalizeCnpj(value);
  if (cnpj.length === 0) return "empty";
  if (cnpj.length < 14) return "incomplete";
  return isValidCnpj(cnpj) ? "valid" : "invalid";
}
