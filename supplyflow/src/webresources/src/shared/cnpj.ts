/**
 * CNPJ validation – numeric and alphanumeric (IN RFB 2.229/2024, effective July 2026).
 * Mirrors SupplyFlow.Plugins/Domain/Cnpj.cs: the client gives instant feedback, the server plugin is authoritative.
 */
const FIRST_WEIGHTS = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
const SECOND_WEIGHTS = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

export function normalizeCnpj(value: string | null | undefined): string {
  return (value ?? "").replace(/[^0-9a-z]/gi, "").toUpperCase();
}

function checkDigit(base: string, weights: number[]): number {
  const sum = weights.reduce((acc, weight, i) => acc + (base.charCodeAt(i) - 48) * weight, 0);
  const remainder = sum % 11;
  return remainder < 2 ? 0 : 11 - remainder;
}

export function isValidCnpj(value: string | null | undefined): boolean {
  const cnpj = normalizeCnpj(value);
  if (!/^[0-9A-Z]{12}[0-9]{2}$/.test(cnpj)) {
    return false;
  }

  if (/^(.)\1{13}$/.test(cnpj)) {
    return false;
  }

  return checkDigit(cnpj, FIRST_WEIGHTS) === Number(cnpj[12]) && checkDigit(cnpj, SECOND_WEIGHTS) === Number(cnpj[13]);
}

export function formatCnpj(value: string | null | undefined): string {
  const cnpj = normalizeCnpj(value);
  if (cnpj.length !== 14) {
    return value ?? "";
  }

  return `${cnpj.slice(0, 2)}.${cnpj.slice(2, 5)}.${cnpj.slice(5, 8)}/${cnpj.slice(8, 12)}-${cnpj.slice(12)}`;
}
