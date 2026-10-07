using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplyFlow.Deployer.Definitions;

public static class DefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SchemaDefinition LoadSchema(string path) => Load<SchemaDefinition>(path);

    public static RegistrationDefinition LoadRegistration(string path) => Load<RegistrationDefinition>(path);

    private static T Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Definition file not found: {path}", path);
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Definition file is empty: {path}");
    }
}

/// <summary>
/// Offline consistency checks – run in CI (unit tests) so a broken definition never reaches an environment.
/// </summary>
public static class DefinitionValidator
{
    public static IReadOnlyList<string> Validate(SchemaDefinition schema)
    {
        var errors = new List<string>();
        var prefix = schema.Publisher.Prefix + "_";
        var globalChoices = schema.GlobalChoices.Select(c => c.SchemaName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = schema.Tables.Select(t => t.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var systemTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "account", "contact", "systemuser", "team" };
        var minOptionValue = schema.Publisher.OptionValuePrefix * 10_000;
        var maxOptionValue = minOptionValue + 9_999;

        void CheckPrefix(string name, string what)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{what} '{name}' must start with publisher prefix '{prefix}'.");
            }
        }

        void CheckOptions(IEnumerable<ChoiceOption> options, string owner)
        {
            foreach (var option in options)
            {
                if (option.Value < minOptionValue || option.Value > maxOptionValue)
                {
                    errors.Add($"{owner}: option {option.Value} is outside the publisher range {minOptionValue}-{maxOptionValue}.");
                }
            }

            foreach (var duplicate in options.GroupBy(o => o.Value).Where(g => g.Count() > 1))
            {
                errors.Add($"{owner}: duplicated option value {duplicate.Key}.");
            }
        }

        foreach (var choice in schema.GlobalChoices)
        {
            CheckPrefix(choice.SchemaName, "Global choice");
            CheckOptions(choice.Options, choice.SchemaName);
        }

        foreach (var table in schema.Tables)
        {
            if (!table.Existing)
            {
                CheckPrefix(table.SchemaName, "Table");
                if (string.IsNullOrWhiteSpace(table.PrimaryName.SchemaName))
                {
                    errors.Add($"Table '{table.SchemaName}' has no primary name column.");
                }
            }

            var columns = table.Columns.Select(c => c.LogicalName).ToList();
            foreach (var duplicate in columns.GroupBy(c => c).Where(g => g.Count() > 1))
            {
                errors.Add($"Table '{table.SchemaName}': duplicated column '{duplicate.Key}'.");
            }

            foreach (var column in table.Columns)
            {
                CheckPrefix(column.SchemaName, $"Column of {table.SchemaName}");
                switch (column.Type)
                {
                    case ColumnType.Choice when column.GlobalChoice is null && (column.Options is null || column.Options.Count == 0):
                        errors.Add($"Choice column '{column.SchemaName}' needs a globalChoice or options.");
                        break;
                    case ColumnType.Choice when column.GlobalChoice is not null && !globalChoices.Contains(column.GlobalChoice):
                        errors.Add($"Choice column '{column.SchemaName}' references unknown global choice '{column.GlobalChoice}'.");
                        break;
                    case ColumnType.Choice when column.Options is not null:
                        CheckOptions(column.Options, column.SchemaName);
                        break;
                    case ColumnType.String or ColumnType.Memo when column.MaxLength is null:
                        errors.Add($"Text column '{column.SchemaName}' needs maxLength.");
                        break;
                }
            }

            var lookupColumns = schema.Lookups
                .Where(l => l.ReferencingTable.Equals(table.LogicalName, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.LogicalName);
            var knownColumns = columns.Concat(lookupColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in table.Keys)
            {
                CheckPrefix(key.SchemaName, $"Key of {table.SchemaName}");
                foreach (var keyColumn in key.Columns.Where(c => !knownColumns.Contains(c)))
                {
                    errors.Add($"Key '{key.SchemaName}' references unknown column '{keyColumn}'.");
                }
            }
        }

        foreach (var lookup in schema.Lookups)
        {
            CheckPrefix(lookup.SchemaName, "Lookup");
            CheckPrefix(lookup.RelationshipName, "Relationship");
            foreach (var table in new[] { lookup.ReferencingTable, lookup.ReferencedTable })
            {
                if (!tables.Contains(table) && !systemTables.Contains(table))
                {
                    errors.Add($"Lookup '{lookup.RelationshipName}' references unknown table '{table}'.");
                }
            }
        }

        foreach (var relationship in schema.Lookups.GroupBy(l => l.RelationshipName).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicated relationship name '{relationship.Key}'.");
        }

        foreach (var variable in schema.EnvironmentVariables)
        {
            CheckPrefix(variable.SchemaName, "Environment variable");
        }

        return errors;
    }

    public static IReadOnlyList<string> Validate(RegistrationDefinition registration, SchemaDefinition schema)
    {
        var errors = new List<string>();
        var knownTables = schema.Tables.Select(t => t.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allSteps = registration.Plugins.SelectMany(p => p.Steps)
            .Concat(registration.ServiceEndpoints.SelectMany(e => e.Steps))
            .ToList();

        foreach (var duplicate in allSteps.GroupBy(s => s.Name).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicated step name '{duplicate.Key}'.");
        }

        foreach (var step in allSteps)
        {
            if (!knownTables.Contains(step.Entity))
            {
                errors.Add($"Step '{step.Name}' targets table '{step.Entity}' that is not in schema.json.");
            }

            if (step.Message == "Create" && step.Images.Any(i => i.Type != ImageType.PostImage))
            {
                errors.Add($"Step '{step.Name}': Create has no pre-image.");
            }

            if (step.Message == "Delete" && step.Images.Any(i => i.Type != ImageType.PreImage))
            {
                errors.Add($"Step '{step.Name}': Delete has no post-image.");
            }

            if (step.Stage != StepStage.PostOperation && step.Images.Any(i => i.Type != ImageType.PreImage))
            {
                errors.Add($"Step '{step.Name}': post-images are only available in PostOperation.");
            }

            if (step.Mode == StepMode.Asynchronous && step.Stage != StepStage.PostOperation)
            {
                errors.Add($"Step '{step.Name}': asynchronous steps must be PostOperation.");
            }

            if (step.FilteringAttributes.Count > 0 && step.Message != "Update")
            {
                errors.Add($"Step '{step.Name}': filtering attributes only apply to Update.");
            }

            if (step.Message == "Update" && step.FilteringAttributes.Count == 0)
            {
                errors.Add($"Step '{step.Name}': Update steps must declare filtering attributes (performance).");
            }

            var table = schema.Tables.FirstOrDefault(t => t.LogicalName.Equals(step.Entity, StringComparison.OrdinalIgnoreCase));
            if (table is null)
            {
                continue;
            }

            var columns = KnownColumns(schema, table);
            foreach (var attribute in step.FilteringAttributes.Concat(step.Images.SelectMany(i => i.Attributes)))
            {
                if (!columns.Contains(attribute))
                {
                    errors.Add($"Step '{step.Name}' references unknown column '{step.Entity}.{attribute}'.");
                }
            }
        }

        var pluginTypes = registration.Plugins.Select(p => p.Type).ToHashSet();
        foreach (var api in registration.CustomApis)
        {
            if (!pluginTypes.Contains(api.PluginType))
            {
                errors.Add($"Custom API '{api.UniqueName}' references plugin type '{api.PluginType}' that is not registered.");
            }

            if (api.BindingType == CustomApiBindingType.Entity && string.IsNullOrEmpty(api.BoundEntity))
            {
                errors.Add($"Custom API '{api.UniqueName}' is entity-bound but has no boundEntity.");
            }
        }

        return errors;
    }

    private static HashSet<string> KnownColumns(SchemaDefinition schema, TableDefinition table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "statecode", "statuscode", "ownerid", "createdon", "modifiedon", table.LogicalName + "id",
        };

        if (!table.Existing)
        {
            columns.Add(table.PrimaryName.SchemaName.ToLowerInvariant());
        }

        columns.UnionWith(table.Columns.Select(c => c.LogicalName));
        columns.UnionWith(schema.Lookups
            .Where(l => l.ReferencingTable.Equals(table.LogicalName, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.LogicalName));
        return columns;
    }
}
