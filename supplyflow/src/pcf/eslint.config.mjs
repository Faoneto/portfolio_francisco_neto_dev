import js from "@eslint/js";
import tseslint from "typescript-eslint";
import react from "eslint-plugin-react";

export default tseslint.config(
  { ignores: ["out/**", "obj/**", "bin/**", "**/generated/**", "*.js", "*.mjs"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    plugins: { react },
    settings: { react: { version: "16.14" } },
    rules: { ...react.configs.recommended.rules, "react/prop-types": "off" },
  },
);
