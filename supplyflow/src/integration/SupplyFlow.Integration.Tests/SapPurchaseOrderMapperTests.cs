using System;
using SupplyFlow.Integration.Dataverse;
using SupplyFlow.Integration.Sap;
using Xunit;

namespace SupplyFlow.Integration.Tests;

public class SapPurchaseOrderMapperTests
{
    private static readonly SapOptions Options = new() { CompanyCode = "1000", PurchasingOrganization = "1000", PurchasingGroup = "001", Plant = "1010" };

    public static RequisitionSnapshot Requisition(string? vendor = "100231", string? costCenter = "CC1001") => new(
        Guid.NewGuid(),
        "RC-202610-00042",
        100_000_002,
        null,
        vendor,
        "Rolamentos Amazônia Ltda",
        costCenter,
        new DateTime(2026, 10, 20),
        30_306m,
        new[]
        {
            new RequisitionLineSnapshot(10, "000000000010004711", "Rolamento rígido de esferas 6205-2RS – linha 2 – parada programada", "UN", 40m, 48.90m, null),
            new RequisitionLineSnapshot(20, "000000000010004712", "Correia transportadora 800mm", "M", 90m, 315m, new DateTime(2026, 10, 15)),
        });

    [Fact]
    public void Maps_header_items_account_assignment_and_schedule()
    {
        var (order, errors) = SapPurchaseOrderMapper.Map(Requisition(), Options);

        Assert.Empty(errors);
        Assert.NotNull(order);
        Assert.Equal("NB", order!.PurchaseOrderType);
        Assert.Equal("0000100231", order.Supplier);
        Assert.Equal("RC-202610-00042", order.CorrespncExternalReference);
        Assert.Equal(2, order.Items.Count);

        var first = order.Items[0];
        Assert.Equal("00010", first.PurchaseOrderItem);
        Assert.Equal(40, first.PurchaseOrderItemText.Length);
        Assert.Equal("K", first.AccountAssignmentCategory);
        Assert.Equal("CC1001", first.AccountAssignments[0].CostCenter);
        Assert.Equal(new DateTime(2026, 10, 20), first.ScheduleLines[0].ScheduleLineDeliveryDate); // falls back to need-by date
        Assert.Equal(new DateTime(2026, 10, 15), order.Items[1].ScheduleLines[0].ScheduleLineDeliveryDate);
    }

    [Fact]
    public void Reports_every_missing_mandatory_field()
    {
        var (order, errors) = SapPurchaseOrderMapper.Map(Requisition(vendor: null, costCenter: " "), Options);

        Assert.Null(order);
        Assert.Contains(errors, e => e.Contains("sem código SAP"));
        Assert.Contains(errors, e => e.Contains("Centro de custo"));
    }
}
