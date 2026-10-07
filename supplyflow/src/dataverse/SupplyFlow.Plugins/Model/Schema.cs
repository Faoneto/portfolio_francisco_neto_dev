namespace SupplyFlow.Plugins.Model;

/// <summary>
/// Strongly-typed logical names and choice values for the SupplyFlow schema.
/// </summary>
/// <remarks>
/// Late-bound + constants instead of generated early-bound classes (see docs/adr/0003-late-bound-with-constants.md).
/// Values here MUST match src/dataverse/SupplyFlow.Deployer/definitions/schema.json.
/// </remarks>
public static class Schema
{
    public const string Prefix = "fno_";

    public static class Account
    {
        public const string EntityName = "account";
        public const string PrimaryId = "accountid";
        public const string Name = "name";
        public const string StateCode = "statecode";
        public const string Cnpj = "fno_cnpj";
        public const string IsSupplier = "fno_issupplier";
        public const string SupplierStatus = "fno_supplierstatus";
        public const string SapVendorCode = "fno_sapvendorcode";
    }

    public static class Material
    {
        public const string EntityName = "fno_material";
        public const string PrimaryId = "fno_materialid";
        public const string Name = "fno_name";
        public const string MaterialCode = "fno_materialcode";
        public const string MaterialGroup = "fno_materialgroup";
        public const string StandardPrice = "fno_standardprice";
        public const string UnitOfMeasure = "fno_unitofmeasure";
    }

    /// <summary>N:N "approved materials per supplier" (account x fno_material).</summary>
    public static class SupplierMaterial
    {
        public const string RelationshipName = "fno_account_fno_material";
        public const string IntersectEntityName = "fno_account_fno_material";
        public const string AccountId = "accountid";
        public const string MaterialId = "fno_materialid";
    }

    public static class CostCenter
    {
        public const string EntityName = "fno_costcenter";
        public const string PrimaryId = "fno_costcenterid";
        public const string Name = "fno_name";
        public const string Code = "fno_code";
        public const string ManagerId = "fno_managerid";
        public const string AnnualBudget = "fno_annualbudget";
        public const string CommittedAmount = "fno_committedamount";
        public const string AvailableBudget = "fno_availablebudget";
    }

    public static class ApprovalPolicy
    {
        public const string EntityName = "fno_approvalpolicy";
        public const string PrimaryId = "fno_approvalpolicyid";
        public const string Name = "fno_name";
        public const string Level = "fno_level";
        public const string MinAmount = "fno_minamount";
        public const string MaterialGroup = "fno_materialgroup";
        public const string StateCode = "statecode";
    }

    public static class Requisition
    {
        public const string EntityName = "fno_purchaserequisition";
        public const string PrimaryId = "fno_purchaserequisitionid";
        public const string Name = "fno_name";
        public const string SupplierId = "fno_supplierid";
        public const string CostCenterId = "fno_costcenterid";
        public const string RequesterId = "fno_requesterid";
        public const string NeedByDate = "fno_needbydate";
        public const string Justification = "fno_justification";
        public const string Stage = "fno_stage";
        public const string TotalAmount = "fno_totalamount";
        public const string ApprovalLevel = "fno_approvallevel";
        public const string IsOverBudget = "fno_isoverbudget";
        public const string ApprovalNotes = "fno_approvalnotes";
        public const string SubmittedOn = "fno_submittedon";
        public const string ApprovedOn = "fno_approvedon";
        public const string CancellationReason = "fno_cancellationreason";
        public const string SapPurchaseOrder = "fno_sappurchaseorder";
        public const string IntegrationStatus = "fno_integrationstatus";
        public const string IntegrationMessage = "fno_integrationmessage";
        public const string BudgetCommitted = "fno_budgetcommitted";
    }

    public static class RequisitionLine
    {
        public const string EntityName = "fno_requisitionline";
        public const string PrimaryId = "fno_requisitionlineid";
        public const string Name = "fno_name";
        public const string RequisitionId = "fno_requisitionid";
        public const string MaterialId = "fno_materialid";
        public const string MaterialGroup = "fno_materialgroup";
        public const string Quantity = "fno_quantity";
        public const string UnitPrice = "fno_unitprice";
        public const string LineTotal = "fno_linetotal";
        public const string DeliveryDate = "fno_deliverydate";
    }

    public static class EnvironmentVariables
    {
        public const string RequireSupplierMaterialApproval = "fno_RequireSupplierMaterialApproval";
        public const string SapIntegrationEnabled = "fno_SapIntegrationEnabled";
    }

    public static class CustomApis
    {
        public const string SubmitRequisition = "fno_SubmitRequisition";
        public const string ApprovalLevelOutput = "ApprovalLevel";
        public const string IsOverBudgetOutput = "IsOverBudget";
        public const string MessageOutput = "Message";
        public const string CommentInput = "Comment";
    }
}

/// <summary>fno_stage – lifecycle of a purchase requisition.</summary>
public enum RequisitionStage
{
    Draft = 100_000_000,
    PendingApproval = 100_000_001,
    Approved = 100_000_002,
    Rejected = 100_000_003,
    SentToSap = 100_000_004,
    PurchaseOrderCreated = 100_000_005,
    Cancelled = 100_000_006,
}

/// <summary>fno_supplierstatus – supplier onboarding status.</summary>
public enum SupplierStatus
{
    InOnboarding = 100_000_000,
    Approved = 100_000_001,
    Blocked = 100_000_002,
}

/// <summary>fno_approvallevel – required approval authority.</summary>
public enum ApprovalLevel
{
    Manager = 100_000_001,
    Director = 100_000_002,
    Cfo = 100_000_003,
}

/// <summary>fno_materialgroup – global choice shared by material, line and approval policy.</summary>
public enum MaterialGroup
{
    Mro = 100_000_000,
    RawMaterial = 100_000_001,
    Packaging = 100_000_002,
    Services = 100_000_003,
    InformationTechnology = 100_000_004,
}

/// <summary>fno_integrationstatus – SAP integration status.</summary>
public enum IntegrationStatus
{
    NotSent = 100_000_000,
    Queued = 100_000_001,
    Succeeded = 100_000_002,
    Failed = 100_000_003,
}
