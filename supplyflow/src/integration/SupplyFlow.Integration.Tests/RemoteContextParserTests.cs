using System;
using System.IO;
using SupplyFlow.Integration.Dataverse;
using Xunit;

namespace SupplyFlow.Integration.Tests;

public class RemoteContextParserTests
{
    private static string Sample => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "requisition-approved.json"));

    [Fact]
    public void Parses_context_header()
    {
        var evt = RemoteContextParser.Parse(Sample);

        Assert.Equal("Update", evt.MessageName);
        Assert.Equal("fno_purchaserequisition", evt.PrimaryEntityName);
        Assert.Equal(Guid.Parse("a7c3e9f1-5b6d-4e2a-9c8b-1d2e3f4a5b6c"), evt.PrimaryEntityId);
        Assert.Equal(Guid.Parse("9b8f6c1e-2d3a-4b5c-9d8e-7f6a5b4c3d2e"), evt.CorrelationId);
        Assert.Equal(1, evt.Depth);
    }

    [Fact]
    public void Reads_typed_attribute_values_from_post_image()
    {
        var evt = RemoteContextParser.Parse(Sample);

        Assert.Equal(100000002, evt.Get<int>("fno_stage"));
        Assert.Equal(30306.00m, evt.Get<decimal>("fno_totalamount"));
        Assert.Equal("RC-202610-00042", evt.Get<string>("fno_name"));
        Assert.Equal(new DateTime(2025, 10, 20, 0, 0, 0, DateTimeKind.Utc), evt.Get<DateTime>("fno_needbydate"));

        var supplier = evt.Get<EntityReferenceValue>("fno_supplierid")!;
        Assert.Equal("account", supplier.LogicalName);
        Assert.Equal("Rolamentos Amazônia Ltda", supplier.Name);
    }

    [Fact]
    public void Falls_back_to_target_when_attribute_is_not_in_the_image()
    {
        var evt = RemoteContextParser.Parse(Sample);

        Assert.Equal(new DateTime(2025, 10, 7, 15, 13, 20, DateTimeKind.Utc), evt.Get<DateTime>("modifiedon"));
    }

    [Fact]
    public void Handles_context_without_images()
    {
        const string json = """{ "MessageName": "Update", "PrimaryEntityName": "fno_purchaserequisition", "PrimaryEntityId": "a7c3e9f1-5b6d-4e2a-9c8b-1d2e3f4a5b6c" }""";

        var evt = RemoteContextParser.Parse(json);

        Assert.Empty(evt.PostImage);
        Assert.Equal(0, evt.Get<int>("fno_stage"));
    }
}
