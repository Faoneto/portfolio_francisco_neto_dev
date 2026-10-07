using System.Linq;
using Microsoft.Xrm.Sdk.Metadata;
using SupplyFlow.Deployer.Definitions;
using SupplyFlow.Deployer.Metadata;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Definitions;

public class MetadataBuilderTests
{
    private readonly MetadataBuilder _builder = new(1046, "SupplyFlow");
    private readonly SchemaDefinition _schema = SchemaMetadata.Schema;

    [Fact]
    public void Requisition_number_uses_autonumber_and_is_created_in_the_solution()
    {
        var table = _schema.Tables.Single(t => t.LogicalName == "fno_purchaserequisition");

        var request = _builder.BuildTable(table);

        Assert.Equal("RC-{DATETIMEUTC:yyyyMM}-{SEQNUM:5}", request.PrimaryAttribute.AutoNumberFormat);
        Assert.Equal(OwnershipTypes.UserOwned, request.Entity.OwnershipType);
        Assert.Equal("SupplyFlow", request.SolutionUniqueName);
        Assert.True(request.HasActivities);
    }

    [Fact]
    public void Money_columns_use_explicit_precision()
    {
        var column = new ColumnDefinition { SchemaName = "fno_UnitPrice", Type = ColumnType.Money, Precision = 4, DisplayName = "Preço" };

        var attribute = (MoneyAttributeMetadata)_builder.BuildAttribute(column);

        Assert.Equal(0, attribute.PrecisionSource);
        Assert.Equal(4, attribute.Precision);
    }

    [Fact]
    public void Choice_columns_reference_global_choices()
    {
        var column = new ColumnDefinition { SchemaName = "fno_MaterialGroup", Type = ColumnType.Choice, GlobalChoice = "fno_materialgroup", DisplayName = "Grupo" };

        var attribute = (PicklistAttributeMetadata)_builder.BuildAttribute(column);

        Assert.True(attribute.OptionSet.IsGlobal);
        Assert.Equal("fno_materialgroup", attribute.OptionSet.Name);
    }

    [Fact]
    public void Requisition_lines_are_parental_children_of_the_requisition()
    {
        var lookup = _schema.Lookups.Single(l => l.RelationshipName == "fno_purchaserequisition_fno_requisitionline");

        var cascade = _builder.BuildLookup(lookup).OneToManyRelationship.CascadeConfiguration;

        Assert.Equal(CascadeType.Cascade, cascade.Delete);
        Assert.Equal(CascadeType.Cascade, cascade.Assign);
    }

    [Fact]
    public void Suppliers_with_requisitions_cannot_be_deleted()
    {
        var lookup = _schema.Lookups.Single(l => l.RelationshipName == "fno_account_fno_purchaserequisition_supplierid");

        var cascade = _builder.BuildLookup(lookup).OneToManyRelationship.CascadeConfiguration;

        Assert.Equal(CascadeType.Restrict, cascade.Delete);
    }

    [Fact]
    public void Date_only_columns_have_date_only_behavior()
    {
        var column = new ColumnDefinition { SchemaName = "fno_NeedByDate", Type = ColumnType.DateOnly, DisplayName = "Data" };

        var attribute = (DateTimeAttributeMetadata)_builder.BuildAttribute(column);

        Assert.Equal(DateTimeBehavior.DateOnly, attribute.DateTimeBehavior);
    }
}
