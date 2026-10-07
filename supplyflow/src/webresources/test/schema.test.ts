import * as fs from "fs";
import * as path from "path";
import { Account, IntegrationStatus, Requisition, RequisitionStage, SupplierStatus } from "../src/shared/schema";

/** Cross-language drift check: TypeScript constants vs the schema deployed to Dataverse. */
const schema = JSON.parse(
  fs.readFileSync(path.join(__dirname, "../../dataverse/definitions/schema.json"), "utf8"),
) as {
  tables: Array<{ schemaName: string; columns: Array<{ schemaName: string; options?: Array<{ value: number }> }> }>;
  lookups: Array<{ schemaName: string; referencingTable: string }>;
};

function columnsOf(table: string): Set<string> {
  const t = schema.tables.find((x) => x.schemaName.toLowerCase() === table)!;
  const lookups = schema.lookups.filter((l) => l.referencingTable === table).map((l) => l.schemaName.toLowerCase());
  return new Set([...t.columns.map((c) => c.schemaName.toLowerCase()), ...lookups]);
}

function optionsOf(table: string, column: string): number[] {
  const t = schema.tables.find((x) => x.schemaName.toLowerCase() === table)!;
  return t.columns.find((c) => c.schemaName.toLowerCase() === column)!.options!.map((o) => o.value).sort();
}

const enumValues = (e: object): number[] => Object.values(e).filter((v): v is number => typeof v === "number").sort();

describe("schema.ts matches schema.json", () => {
  it("requisition columns exist", () => {
    const columns = columnsOf(Requisition.entityName);
    Object.entries(Requisition)
      .filter(([key]) => !["entityName", "entitySetName"].includes(key))
      .forEach(([, value]) => expect(columns).toContain(value));
  });

  it("account columns exist", () => {
    const columns = columnsOf(Account.entityName);
    Object.entries(Account)
      .filter(([key]) => key !== "entityName")
      .forEach(([, value]) => expect(columns).toContain(value));
  });

  it("choice values match", () => {
    expect(enumValues(RequisitionStage)).toEqual(optionsOf("fno_purchaserequisition", "fno_stage"));
    expect(enumValues(IntegrationStatus)).toEqual(optionsOf("fno_purchaserequisition", "fno_integrationstatus"));
    expect(enumValues(SupplierStatus)).toEqual(optionsOf("account", "fno_supplierstatus"));
  });
});
