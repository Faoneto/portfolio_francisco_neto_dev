using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Deployer.Definitions;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Tests.Integration;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Definitions;

/// <summary>
/// Guards against drift between the C# code (Schema.cs, enums, plugin classes) and the JSON definitions
/// that are deployed to Dataverse.
/// </summary>
public class DefinitionConsistencyTests
{
    private readonly SchemaDefinition _schema = SchemaMetadata.Schema;
    private readonly RegistrationDefinition _registration = SchemaMetadata.Registration;

    [Fact]
    public void Schema_definition_is_valid()
    {
        Assert.Empty(DefinitionValidator.Validate(_schema));
    }

    [Fact]
    public void Registration_definition_is_valid()
    {
        Assert.Empty(DefinitionValidator.Validate(_registration, _schema));
    }

    [Fact]
    public void Every_registered_type_is_a_plugin_with_parameterless_constructor()
    {
        foreach (var plugin in _registration.Plugins)
        {
            var type = RegistrationDrivenPipeline.ResolvePluginType(plugin.Type);
            Assert.True(typeof(IPlugin).IsAssignableFrom(type), $"{plugin.Type} does not implement IPlugin");
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        }
    }

    [Fact]
    public void Every_plugin_in_the_assembly_is_registered()
    {
        var registered = _registration.Plugins.Select(p => p.Type).ToHashSet();
        var plugins = typeof(PluginBase).Assembly.GetTypes()
            .Where(t => typeof(PluginBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.FullName!);

        Assert.All(plugins, p => Assert.Contains(p, registered));
    }

    [Fact]
    public void Every_logical_name_in_Schema_cs_exists_in_schema_json()
    {
        var known = KnownNames();
        var constants = typeof(Schema).GetNestedTypes()
            .Where(t => t != typeof(Schema.CustomApis))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: $"{f.DeclaringType!.Name}.{f.Name}", Value: (string)f.GetRawConstantValue()!))
            .Where(c => c.Value.StartsWith(_schema.Publisher.Prefix + "_", StringComparison.OrdinalIgnoreCase));

        Assert.All(constants, c => Assert.True(known.Contains(c.Value), $"{c.Name} = '{c.Value}' not found in schema.json"));
    }

    [Fact]
    public void Custom_api_contract_matches_constants()
    {
        var api = Assert.Single(_registration.CustomApis, a => a.UniqueName == Schema.CustomApis.SubmitRequisition);
        var outputs = api.ResponseProperties.Select(p => p.UniqueName).ToList();

        Assert.Contains(Schema.CustomApis.ApprovalLevelOutput, outputs);
        Assert.Contains(Schema.CustomApis.IsOverBudgetOutput, outputs);
        Assert.Contains(Schema.CustomApis.MessageOutput, outputs);
        Assert.Equal(Schema.Requisition.EntityName, api.BoundEntity);
    }

    [Theory]
    [InlineData(typeof(RequisitionStage), "fno_purchaserequisition", "fno_stage")]
    [InlineData(typeof(SupplierStatus), "account", "fno_supplierstatus")]
    [InlineData(typeof(IntegrationStatus), "fno_purchaserequisition", "fno_integrationstatus")]
    [InlineData(typeof(ApprovalLevel), null, "fno_approvallevel")]
    [InlineData(typeof(MaterialGroup), null, "fno_materialgroup")]
    public void Enums_match_choice_options(Type enumType, string? table, string choice)
    {
        var options = table is null
            ? _schema.GlobalChoices.Single(c => c.SchemaName == choice).Options
            : _schema.Tables.Single(t => t.LogicalName == table).Columns.Single(c => c.LogicalName == choice).Options!;

        var enumValues = Enum.GetValues(enumType).Cast<int>().OrderBy(v => v);
        Assert.Equal(enumValues, options.Select(o => o.Value).OrderBy(v => v));
    }

    private HashSet<string> KnownNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in _schema.Tables)
        {
            names.Add(table.LogicalName);
            names.Add(table.LogicalName + "id");
            if (!table.Existing)
            {
                names.Add(table.PrimaryName.SchemaName);
            }

            names.UnionWith(table.Columns.Select(c => c.LogicalName));
        }

        names.UnionWith(_schema.Lookups.Select(l => l.LogicalName));
        names.UnionWith(_schema.ManyToMany.SelectMany(m => new[] { m.SchemaName, m.IntersectEntity }));
        names.UnionWith(_schema.EnvironmentVariables.Select(v => v.SchemaName));
        return names;
    }
}
