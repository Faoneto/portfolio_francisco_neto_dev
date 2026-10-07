using System;
using System.Collections.Generic;
using System.Linq;
using SupplyFlow.Integration.Dataverse;

namespace SupplyFlow.Integration.Sap;

public static class SapPurchaseOrderMapper
{
    /// <summary>Maps a requisition to a SAP purchase order. Returns validation errors instead of throwing.</summary>
    public static (SapPurchaseOrder? Order, IReadOnlyList<string> Errors) Map(RequisitionSnapshot requisition, SapOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(requisition.SupplierSapCode))
        {
            errors.Add($"Fornecedor \"{requisition.SupplierName}\" sem código SAP.");
        }

        if (string.IsNullOrWhiteSpace(requisition.CostCenterCode))
        {
            errors.Add("Centro de custo sem código.");
        }

        if (requisition.Lines.Count == 0)
        {
            errors.Add("Requisição sem itens.");
        }

        errors.AddRange(requisition.Lines
            .Where(l => string.IsNullOrWhiteSpace(l.MaterialCode))
            .Select(l => $"Item {l.LineNumber} sem código de material."));

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var deliveryFallback = requisition.NeedByDate ?? DateTime.UtcNow.Date.AddDays(7);
        var order = new SapPurchaseOrder
        {
            CompanyCode = options.CompanyCode,
            PurchasingOrganization = options.PurchasingOrganization,
            PurchasingGroup = options.PurchasingGroup,
            Supplier = requisition.SupplierSapCode!.PadLeft(10, '0'),
            CorrespncExternalReference = requisition.Number,
            Items = requisition.Lines.Select(line => new SapPurchaseOrderItem
            {
                PurchaseOrderItem = line.LineNumber.ToString("D5"),
                Material = line.MaterialCode,
                PurchaseOrderItemText = Truncate(line.Description, 40),
                Plant = options.Plant,
                OrderQuantity = line.Quantity,
                PurchaseOrderQuantityUnit = line.UnitOfMeasure,
                NetPriceAmount = line.UnitPrice,
                AccountAssignments = { new SapAccountAssignment { CostCenter = requisition.CostCenterCode! } },
                ScheduleLines =
                {
                    new SapScheduleLine
                    {
                        ScheduleLineDeliveryDate = (line.DeliveryDate ?? deliveryFallback).Date,
                        ScheduleLineOrderQuantity = line.Quantity,
                    },
                },
            }).ToList(),
        };

        return (order, errors);
    }

    private static string Truncate(string? value, int length)
    {
        value ??= string.Empty;
        return value.Length <= length ? value : value[..length];
    }
}
