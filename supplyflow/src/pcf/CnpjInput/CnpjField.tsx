import * as React from "react";
import { Badge, Field, FluentProvider, Input, webLightTheme } from "@fluentui/react-components";
import { CnpjState, evaluateCnpj, maskCnpj, normalizeCnpj } from "./cnpj";

export interface CnpjFieldProps {
  value: string | null;
  disabled: boolean;
  showStatusBadge: boolean;
  /** Called with the canonical (unmasked) value, or null when the field is cleared. */
  onChange: (value: string | null) => void;
}

const messages: Record<CnpjState, string | undefined> = {
  empty: undefined,
  incomplete: "CNPJ incompleto (14 caracteres).",
  invalid: "Dígitos verificadores inválidos.",
  valid: undefined,
};

export const CnpjField: React.FC<CnpjFieldProps> = ({ value, disabled, showStatusBadge, onChange }) => {
  const [text, setText] = React.useState(maskCnpj(value));

  // Keep in sync when the bound value changes outside the control (e.g. Power Automate, another user).
  React.useEffect(() => setText(maskCnpj(value)), [value]);

  const state = evaluateCnpj(text);

  const handleChange = (_: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
    const masked = maskCnpj(data.value);
    setText(masked);
    const normalized = normalizeCnpj(masked);
    // Only push complete values (valid or not) – the server validates and rejects invalid ones on save.
    if (normalized.length === 0) onChange(null);
    else if (normalized.length === 14) onChange(normalized);
  };

  return (
    <FluentProvider theme={webLightTheme} style={{ width: "100%", background: "transparent" }}>
      <Field
        validationState={state === "invalid" || state === "incomplete" ? "error" : "none"}
        validationMessage={messages[state]}
      >
        <Input
          value={text}
          disabled={disabled}
          placeholder="00.000.000/0000-00"
          onChange={handleChange}
          maxLength={18}
          aria-label="CNPJ"
          contentAfter={
            showStatusBadge && state === "valid" ? (
              <Badge appearance="tint" color="success">Válido</Badge>
            ) : showStatusBadge && state === "invalid" ? (
              <Badge appearance="tint" color="danger">Inválido</Badge>
            ) : undefined
          }
        />
      </Field>
    </FluentProvider>
  );
};
