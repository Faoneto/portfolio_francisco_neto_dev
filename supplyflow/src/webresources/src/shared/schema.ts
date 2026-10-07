/**
 * Logical names and choice values used by the client scripts.
 * Kept in sync with src/dataverse/definitions/schema.json – enforced by test/schema.test.ts.
 */
export const Requisition = {
  entityName: "fno_purchaserequisition",
  entitySetName: "fno_purchaserequisitions",
  supplier: "fno_supplierid",
  costCenter: "fno_costcenterid",
  needByDate: "fno_needbydate",
  justification: "fno_justification",
  stage: "fno_stage",
  totalAmount: "fno_totalamount",
  approvalLevel: "fno_approvallevel",
  isOverBudget: "fno_isoverbudget",
  approvalNotes: "fno_approvalnotes",
  cancellationReason: "fno_cancellationreason",
  sapPurchaseOrder: "fno_sappurchaseorder",
  integrationStatus: "fno_integrationstatus",
  integrationMessage: "fno_integrationmessage",
} as const;

export const Account = {
  entityName: "account",
  cnpj: "fno_cnpj",
  isSupplier: "fno_issupplier",
  supplierStatus: "fno_supplierstatus",
  sapVendorCode: "fno_sapvendorcode",
} as const;

export enum RequisitionStage {
  Draft = 100000000,
  PendingApproval = 100000001,
  Approved = 100000002,
  Rejected = 100000003,
  SentToSap = 100000004,
  PurchaseOrderCreated = 100000005,
  Cancelled = 100000006,
}

export enum SupplierStatus {
  InOnboarding = 100000000,
  Approved = 100000001,
  Blocked = 100000002,
}

export enum IntegrationStatus {
  NotSent = 100000000,
  Queued = 100000001,
  Succeeded = 100000002,
  Failed = 100000003,
}

export const ApprovalLevelLabels: Record<number, string> = {
  100000001: "Gestor do Centro de Custo",
  100000002: "Diretor",
  100000003: "CFO",
};

export const SubmitRequisitionApi = "fno_SubmitRequisition";

export const editableStages: ReadonlySet<RequisitionStage> = new Set([RequisitionStage.Draft, RequisitionStage.Rejected]);

/** XrmEnum.FormType values (ambient const enums are not usable with isolatedModules/esbuild). */
export const FormType = { Create: 1, Update: 2, ReadOnly: 3, Disabled: 4 } as const;
