using System.Collections.Generic;

namespace SupplyFlow.Deployer.Definitions;

/// <summary>Root of definitions/schema.json – the desired state of the SupplyFlow data model.</summary>
public sealed class SchemaDefinition
{
    public PublisherDefinition Publisher { get; set; } = new();

    public SolutionDefinition Solution { get; set; } = new();

    public List<ChoiceDefinition> GlobalChoices { get; set; } = new();

    public List<TableDefinition> Tables { get; set; } = new();

    public List<LookupDefinition> Lookups { get; set; } = new();

    public List<ManyToManyDefinition> ManyToMany { get; set; } = new();

    public List<EnvironmentVariableDefinition> EnvironmentVariables { get; set; } = new();
}

public sealed class PublisherDefinition
{
    public string UniqueName { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public int OptionValuePrefix { get; set; }
}

public sealed class SolutionDefinition
{
    public string UniqueName { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0.0";

    public string? Description { get; set; }
}

public sealed class ChoiceDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<ChoiceOption> Options { get; set; } = new();
}

public sealed class ChoiceOption
{
    public int Value { get; set; }

    public string Label { get; set; } = string.Empty;

    public string? Color { get; set; }
}

public enum TableOwnership
{
    UserOwned,
    OrganizationOwned,
}

public sealed class TableDefinition
{
    /// <summary>Schema name, e.g. fno_PurchaseRequisition. For existing tables (account) use the logical name.</summary>
    public string SchemaName { get; set; } = string.Empty;

    /// <summary>True for out-of-the-box tables that are only extended (columns/keys added).</summary>
    public bool Existing { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string PluralName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TableOwnership Ownership { get; set; } = TableOwnership.UserOwned;

    public PrimaryNameDefinition PrimaryName { get; set; } = new();

    public bool HasNotes { get; set; }

    public bool HasActivities { get; set; }

    public bool ChangeTracking { get; set; } = true;

    public bool Audit { get; set; } = true;

    public List<ColumnDefinition> Columns { get; set; } = new();

    public List<KeyDefinition> Keys { get; set; } = new();

    public string LogicalName => SchemaName.ToLowerInvariant();
}

public sealed class PrimaryNameDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int MaxLength { get; set; } = 100;

    public string? AutoNumberFormat { get; set; }
}

public enum ColumnType
{
    String,
    Memo,
    Integer,
    Decimal,
    Money,
    Boolean,
    Choice,
    DateOnly,
    DateTime,
}

public enum RequiredLevel
{
    None,
    Recommended,
    ApplicationRequired,
}

public sealed class ColumnDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public ColumnType Type { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public RequiredLevel Required { get; set; } = RequiredLevel.None;

    public int? MaxLength { get; set; }

    public int? Precision { get; set; }

    public double? MinValue { get; set; }

    public double? MaxValue { get; set; }

    /// <summary>Choice columns: schema name of a global choice (preferred) …</summary>
    public string? GlobalChoice { get; set; }

    /// <summary>… or local options.</summary>
    public List<ChoiceOption>? Options { get; set; }

    public int? DefaultChoice { get; set; }

    public bool? DefaultBoolean { get; set; }

    public string TrueLabel { get; set; } = "Sim";

    public string FalseLabel { get; set; } = "Não";

    public string? AutoNumberFormat { get; set; }

    public bool Audit { get; set; } = true;

    /// <summary>Enables column-level (field) security.</summary>
    public bool Secured { get; set; }

    public string LogicalName => SchemaName.ToLowerInvariant();
}

public sealed class KeyDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<string> Columns { get; set; } = new();
}

public enum CascadeBehavior
{
    /// <summary>Child records follow the parent (assign, share, delete…).</summary>
    Parental,

    /// <summary>Lookup is cleared when the parent is deleted.</summary>
    ReferentialRemoveLink,

    /// <summary>Parent cannot be deleted while children exist.</summary>
    ReferentialRestrict,
}

public sealed class LookupDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public string ReferencingTable { get; set; } = string.Empty;

    public string ReferencedTable { get; set; } = string.Empty;

    public string RelationshipName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public RequiredLevel Required { get; set; } = RequiredLevel.None;

    public CascadeBehavior Cascade { get; set; } = CascadeBehavior.ReferentialRemoveLink;

    public string LogicalName => SchemaName.ToLowerInvariant();
}

public sealed class ManyToManyDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public string Entity1 { get; set; } = string.Empty;

    public string Entity2 { get; set; } = string.Empty;

    public string IntersectEntity { get; set; } = string.Empty;

    public string Entity1MenuLabel { get; set; } = string.Empty;

    public string Entity2MenuLabel { get; set; } = string.Empty;
}

public enum EnvironmentVariableType
{
    String = 100_000_000,
    Number = 100_000_001,
    Boolean = 100_000_002,
    Json = 100_000_003,
}

public sealed class EnvironmentVariableDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public EnvironmentVariableType Type { get; set; }

    public string? DefaultValue { get; set; }
}
