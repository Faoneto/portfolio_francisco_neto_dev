import js from "@eslint/js";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist/**", "coverage/**", "*.js", "*.mjs"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ["src/**/*.ts"],
    rules: {
      // Xrm.Page is deprecated since v9 – always use the formContext from the execution context.
      "no-restricted-properties": ["error", { object: "Xrm", property: "Page", message: "Use executionContext.getFormContext()." }],
      "@typescript-eslint/explicit-module-boundary-types": "error",
    },
  },
);
