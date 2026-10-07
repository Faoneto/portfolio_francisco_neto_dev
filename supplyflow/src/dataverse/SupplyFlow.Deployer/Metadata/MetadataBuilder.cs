using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using SupplyFlow.Deployer.Definitions;

namespace SupplyFlow.Deployer.Metadata;

/// <summary>
/// Pure translation of the JSON definitions into Dataverse metadata requests.
/// No I/O – fully unit-testable.
/// </summary>
public sealed class MetadataBuilder
{
    private readonly int _languageCode;
    private readonly string _solutionUniqueName;

    public MetadataBuilder(int languageCode, string solutionUniqueName)
    {
        _languageCode = languageCode;
        _solutionUniqueName = solutionUniqueName;
    }

    public Label Label(string? text) => new(text ?? string.Empty, _languageCode);

    public CreateOptionSetRequest BuildGlobalChoice(ChoiceDefinition choice)
    {
        var optionSet = new OptionSetMetadata
        {
            Name = choice.SchemaName,
            DisplayName = Label(choice.DisplayName),
            Description = Label(choice.Description),
            IsGlobal = true,
            OptionSetType = OptionSetType.Picklist,
        };

        foreach (var option in choice.Options)
        {
            optionSet.Options.Add(BuildOption(option));
        }

        return new CreateOptionSetRequest { OptionSet = optionSet, SolutionUniqueName = _solutionUniqueName };
    }

    public CreateEntityRequest BuildTable(TableDefinition table)
    {
        if (table.Existing)
        {
            throw new InvalidOperationException($"'{table.SchemaName}' is an existing table and cannot be created.");
        }

        return new CreateEntityRequest
        {
            Entity = new EntityMetadata
            {
                SchemaName = table.SchemaName,
                DisplayName = Label(table.DisplayName),
                DisplayCollectionName = Label(table.PluralName),
                Description = Label(table.Description),
                OwnershipType = table.Ownership == TableOwnership.UserOwned ? OwnershipTypes.UserOwned : OwnershipTypes.OrganizationOwned,
                IsActivity = false,
                IsAuditEnabled = new BooleanManagedProperty(table.Audit),
                ChangeTrackingEnabled = table.ChangeTracking,
            },
            PrimaryAttribute = new StringAttributeMetadata
            {
                SchemaName = table.PrimaryName.SchemaName,
                DisplayName = Label(table.PrimaryName.DisplayName),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(
                    table.PrimaryName.AutoNumberFormat is null ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None),
                MaxLength = table.PrimaryName.MaxLength,
                FormatName = StringFormatName.Text,
                AutoNumberFormat = table.PrimaryName.AutoNumberFormat,
            },
            HasNotes = table.HasNotes,
            HasActivities = table.HasActivities,
            SolutionUniqueName = _solutionUniqueName,
        };
    }

    public CreateAttributeRequest BuildColumn(string tableLogicalName, ColumnDefinition column)
    {
        return new CreateAttributeRequest
        {
            EntityName = tableLogicalName,
            Attribute = BuildAttribute(column),
            SolutionUniqueName = _solutionUniqueName,
        };
    }

    public AttributeMetadata BuildAttribute(ColumnDefinition column)
    {
        AttributeMetadata attribute = column.Type switch
        {
            ColumnType.String => new StringAttributeMetadata
            {
                MaxLength = column.MaxLength,
                FormatName = StringFormatName.Text,
                AutoNumberFormat = column.AutoNumberFormat,
            },
            ColumnType.Memo => new MemoAttributeMetadata
            {
                MaxLength = column.MaxLength,
                Format = StringFormat.TextArea,
            },
            ColumnType.Integer => new IntegerAttributeMetadata
            {
                Format = IntegerFormat.None,
                MinValue = (int?)column.MinValue ?? int.MinValue,
                MaxValue = (int?)column.MaxValue ?? int.MaxValue,
            },
            ColumnType.Decimal => new DecimalAttributeMetadata
            {
                Precision = column.Precision ?? 2,
                MinValue = column.MinValue is null ? null : (decimal)column.MinValue.Value,
                MaxValue = column.MaxValue is null ? null : (decimal)column.MaxValue.Value,
            },
            ColumnType.Money => new MoneyAttributeMetadata
            {
                // PrecisionSource 0 = use the Precision property (instead of org/currency precision).
                PrecisionSource = 0,
                Precision = column.Precision ?? 2,
                MinValue = column.MinValue ?? 0,
                MaxValue = column.MaxValue ?? 1_000_000_000,
            },
            ColumnType.Boolean => new BooleanAttributeMetadata
            {
                OptionSet = new BooleanOptionSetMetadata(
                    new OptionMetadata(Label(column.TrueLabel), 1),
                    new OptionMetadata(Label(column.FalseLabel), 0)),
                DefaultValue = column.DefaultBoolean ?? false,
            },
            ColumnType.Choice => BuildPicklist(column),
            ColumnType.DateOnly => new DateTimeAttributeMetadata
            {
                Format = DateTimeFormat.DateOnly,
                DateTimeBehavior = DateTimeBehavior.DateOnly,
            },
            ColumnType.DateTime => new DateTimeAttributeMetadata
            {
                Format = DateTimeFormat.DateAndTime,
                DateTimeBehavior = DateTimeBehavior.UserLocal,
            },
            _ => throw new NotSupportedException($"Column type {column.Type} is not supported."),
        };

        attribute.SchemaName = column.SchemaName;
        attribute.DisplayName = Label(column.DisplayName);
        attribute.Description = Label(column.Description);
        attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(ToRequiredLevel(column.Required));
        attribute.IsAuditEnabled = new BooleanManagedProperty(column.Audit);
        attribute.IsSecured = column.Secured;
        return attribute;
    }

    public CreateOneToManyRequest BuildLookup(LookupDefinition lookup)
    {
        return new CreateOneToManyRequest
        {
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = lookup.RelationshipName,
                ReferencedEntity = lookup.ReferencedTable,
                ReferencingEntity = lookup.ReferencingTable,
                AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseCollectionName,
                    Group = AssociatedMenuGroup.Details,
                    Order = 10000,
                },
                CascadeConfiguration = BuildCascade(lookup.Cascade),
            },
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = lookup.SchemaName,
                DisplayName = Label(lookup.DisplayName),
                Description = Label(lookup.Description),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(ToRequiredLevel(lookup.Required)),
            },
            SolutionUniqueName = _solutionUniqueName,
        };
    }

    public CreateManyToManyRequest BuildManyToMany(ManyToManyDefinition relationship)
    {
        return new CreateManyToManyRequest
        {
            IntersectEntitySchemaName = relationship.IntersectEntity,
            ManyToManyRelationship = new ManyToManyRelationshipMetadata
            {
                SchemaName = relationship.SchemaName,
                Entity1LogicalName = relationship.Entity1,
                Entity2LogicalName = relationship.Entity2,
                Entity1AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseLabel,
                    Group = AssociatedMenuGroup.Details,
                    Label = Label(relationship.Entity1MenuLabel),
                    Order = 10000,
                },
                Entity2AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseLabel,
                    Group = AssociatedMenuGroup.Details,
                    Label = Label(relationship.Entity2MenuLabel),
                    Order = 10000,
                },
            },
            SolutionUniqueName = _solutionUniqueName,
        };
    }

    public CreateEntityKeyRequest BuildKey(string tableLogicalName, KeyDefinition key)
    {
        return new CreateEntityKeyRequest
        {
            EntityName = tableLogicalName,
            EntityKey = new EntityKeyMetadata
            {
                SchemaName = key.SchemaName,
                DisplayName = Label(key.DisplayName),
                KeyAttributes = key.Columns.ToArray(),
            },
            SolutionUniqueName = _solutionUniqueName,
        };
    }

    public OptionMetadata BuildOption(ChoiceOption option)
    {
        return new OptionMetadata(Label(option.Label), option.Value) { Color = option.Color };
    }

    public static CascadeConfiguration BuildCascade(CascadeBehavior behavior)
    {
        return behavior switch
        {
            CascadeBehavior.Parental => new CascadeConfiguration
            {
                Assign = CascadeType.Cascade,
                Delete = CascadeType.Cascade,
                Merge = CascadeType.Cascade,
                Reparent = CascadeType.Cascade,
                Share = CascadeType.Cascade,
                Unshare = CascadeType.Cascade,
            },
            _ => new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = behavior == CascadeBehavior.ReferentialRestrict ? CascadeType.Restrict : CascadeType.RemoveLink,
                Merge = CascadeType.Cascade,
                Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade,
            },
        };
    }

    private PicklistAttributeMetadata BuildPicklist(ColumnDefinition column)
    {
        OptionSetMetadata optionSet;
        if (column.GlobalChoice is not null)
        {
            optionSet = new OptionSetMetadata { IsGlobal = true, Name = column.GlobalChoice };
        }
        else
        {
            optionSet = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
            foreach (var option in column.Options!)
            {
                optionSet.Options.Add(BuildOption(option));
            }
        }

        return new PicklistAttributeMetadata { OptionSet = optionSet, DefaultFormValue = column.DefaultChoice };
    }

    private static AttributeRequiredLevel ToRequiredLevel(RequiredLevel level) => level switch
    {
        RequiredLevel.ApplicationRequired => AttributeRequiredLevel.ApplicationRequired,
        RequiredLevel.Recommended => AttributeRequiredLevel.Recommended,
        _ => AttributeRequiredLevel.None,
    };
}
