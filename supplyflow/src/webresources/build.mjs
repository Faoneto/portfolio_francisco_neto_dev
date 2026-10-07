// Bundles each entry point into a single web resource file exposed under the global "SupplyFlow" namespace,
// e.g. dist/fno_/js/requisition.form.js -> window.SupplyFlow.RequisitionForm.onLoad
import { build, context } from "esbuild";

const entries = [
  { in: "src/forms/requisition.form.ts", out: "requisition.form", global: "SupplyFlow.RequisitionForm" },
  { in: "src/forms/supplier.form.ts", out: "supplier.form", global: "SupplyFlow.SupplierForm" },
  { in: "src/commands/requisition.commands.ts", out: "requisition.commands", global: "SupplyFlow.RequisitionCommands" },
];

const watch = process.argv.includes("--watch");

for (const entry of entries) {
  const options = {
    entryPoints: [entry.in],
    outfile: `dist/fno_/js/${entry.out}.js`,
    bundle: true,
    format: "iife",
    globalName: entry.global,
    target: "es2020",
    sourcemap: watch ? "inline" : false,
    minify: !watch,
    legalComments: "none",
    banner: { js: "/* SupplyFlow – generated from TypeScript (src/webresources). Do not edit. */" },
  };

  if (watch) {
    const ctx = await context(options);
    await ctx.watch();
  } else {
    await build(options);
    console.log(`built ${options.outfile} -> ${entry.global}`);
  }
}
