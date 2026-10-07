using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SupplyFlow.Integration.Sap;

/// <summary>
/// Purchase order payload shaped after SAP S/4HANA OData API_PURCHASEORDER_PROCESS_SRV (A_PurchaseOrder).
/// </summary>
public sealed class SapPurchaseOrder
{
    public string PurchaseOrderType { get; set; } = "NB";

    public string CompanyCode { get; set; } = string.Empty;

    public string PurchasingOrganization { get; set; } = string.Empty;

    public string PurchasingGroup { get; set; } = string.Empty;

    public string Supplier { get; set; } = string.Empty;

    public string DocumentCurrency { get; set; } = "BRL";

    /// <summary>Requisition number, used by SAP as external reference and idempotency key.</summary>
    public string CorrespncExternalReference { get; set; } = string.Empty;

    [JsonPropertyName("to_PurchaseOrderItem")]
    public List<SapPurchaseOrderItem> Items { get; set; } = new();
}

public sealed class SapPurchaseOrderItem
{
    public string PurchaseOrderItem { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    public string PurchaseOrderItemText { get; set; } = string.Empty;

    public string Plant { get; set; } = string.Empty;

    public decimal OrderQuantity { get; set; }

    public string PurchaseOrderQuantityUnit { get; set; } = string.Empty;

    public decimal NetPriceAmount { get; set; }

    /// <summary>K = cost center account assignment.</summary>
    public string AccountAssignmentCategory { get; set; } = "K";

    [JsonPropertyName("to_AccountAssignment")]
    public List<SapAccountAssignment> AccountAssignments { get; set; } = new();

    [JsonPropertyName("to_ScheduleLine")]
    public List<SapScheduleLine> ScheduleLines { get; set; } = new();
}

public sealed class SapAccountAssignment
{
    public string CostCenter { get; set; } = string.Empty;
}

public sealed class SapScheduleLine
{
    public DateTime ScheduleLineDeliveryDate { get; set; }

    public decimal ScheduleLineOrderQuantity { get; set; }
}

public sealed class SapPurchaseOrderResponse
{
    public string PurchaseOrder { get; set; } = string.Empty;
}

public sealed class SapOptions
{
    public const string Section = "Sap";

    public string BaseUrl { get; set; } = string.Empty;

    public string CompanyCode { get; set; } = "1000";

    public string PurchasingOrganization { get; set; } = "1000";

    public string PurchasingGroup { get; set; } = "001";

    public string Plant { get; set; } = "1010";

    /// <summary>Must match the queue MaxDeliveryCount: on the last attempt the requisition is flagged as failed.</summary>
    public int MaxDeliveryCount { get; set; } = 5;
}

/// <summary>SAP rejected the document (4xx) – retrying will not help.</summary>
public sealed class SapBusinessException : Exception
{
    public SapBusinessException(string message)
        : base(message)
    {
    }
}
