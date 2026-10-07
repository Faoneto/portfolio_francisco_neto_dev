using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Deployer.Definitions;
using SupplyFlow.Deployer.Metadata;

namespace SupplyFlow.Deployer.Deployment;

/// <summary>
/// Applies definitions/schema.json to an environment. Idempotent: only missing components are created,
/// so it can run on every deployment to a DEV environment ("desired state").
/// </summary>
public sealed class SchemaDeployer
{
    private const int SolutionComponentEntity = 1;

    private readonly IOrganizationService _service;
    private readonly ILogger _logger;

    public SchemaDeployer(IOrganizationService service, ILogger logger)
    {
        _service = service;
        _logger = logger;
    }

    public void Deploy(SchemaDefinition schema)
    {
        var errors = DefinitionValidator.Validate(schema);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("schema.json is invalid:\n" + string.Join("\n", errors));
        }

        EnsurePublisherAndSolution(schema);
        var solution = schema.Solution.UniqueName;
        var builder = new MetadataBuilder(GetBaseLanguage(), solution);

        EnsureGlobalChoices(schema, builder);
        var existing = GetExistingMetadata(schema.Tables.Select(t => t.LogicalName));

        foreach (var table in schema.Tables)
        {
            EnsureTable(table, builder, existing, solution);
        }

        // Refresh: tables created above now exist and their lookups/keys can be created.
        existing = GetExistingMetadata(schema.Tables.Select(t => t.LogicalName));

        foreach (var lookup in schema.Lookups)
        {
            var exists = existing.TryGetValue(lookup.ReferencingTable, out var metadata)
                && metadata.Attributes.Any(a => a.LogicalName == lookup.LogicalName);
            if (exists)
            {
                _logger.LogDebug("Lookup {Lookup} exists", lookup.RelationshipName);
                continue;
            }

            _logger.LogInformation("Creating lookup {Table}.{Column} → {Referenced}", lookup.ReferencingTable, lookup.LogicalName, lookup.ReferencedTable);
            _service.Execute(builder.BuildLookup(lookup));
        }

        foreach (var relationship in schema.ManyToMany)
        {
            var found = _service.TryExecute<RetrieveRelationshipResponse>(new RetrieveRelationshipRequest { Name = relationship.SchemaName });
            if (found is not null)
            {
                continue;
            }

            _logger.LogInformation("Creating N:N {Relationship}", relationship.SchemaName);
            _service.Execute(builder.BuildManyToMany(relationship));
        }

        existing = GetExistingMetadata(schema.Tables.Select(t => t.LogicalName));
        foreach (var table in schema.Tables)
        {
            var keys = existing[table.LogicalName].Keys ?? Array.Empty<EntityKeyMetadata>();
            foreach (var key in table.Keys.Where(k => keys.All(e => !e.LogicalName.Equals(k.SchemaName, StringComparison.OrdinalIgnoreCase))))
            {
                // Key creation starts an asynchronous index job; status can be followed in the maker portal.
                _logger.LogInformation("Creating alternate key {Key} on {Table}", key.SchemaName, table.LogicalName);
                _service.Execute(builder.BuildKey(table.LogicalName, key));
            }
        }

        EnsureEnvironmentVariables(schema);

        _logger.LogInformation("Publishing customizations…");
        _service.Execute(new PublishAllXmlRequest());
        _logger.LogInformation("Schema deployed to solution {Solution}", solution);
    }

    private void EnsurePublisherAndSolution(SchemaDefinition schema)
    {
        var publisher = _service.FindFirst("publisher", "uniquename", schema.Publisher.UniqueName, "publisherid");
        var publisherId = publisher?.Id ?? _service.Create(new Entity("publisher")
        {
            ["uniquename"] = schema.Publisher.UniqueName,
            ["friendlyname"] = schema.Publisher.FriendlyName,
            ["customizationprefix"] = schema.Publisher.Prefix,
            ["customizationoptionvalueprefix"] = schema.Publisher.OptionValuePrefix,
        });

        var solution = _service.FindFirst("solution", "uniquename", schema.Solution.UniqueName, "solutionid");
        if (solution is null)
        {
            _logger.LogInformation("Creating solution {Solution}", schema.Solution.UniqueName);
            _service.Create(new Entity("solution")
            {
                ["uniquename"] = schema.Solution.UniqueName,
                ["friendlyname"] = schema.Solution.FriendlyName,
                ["description"] = schema.Solution.Description,
                ["version"] = schema.Solution.Version,
                ["publisherid"] = new EntityReference("publisher", publisherId),
            });
        }
    }

    private int GetBaseLanguage()
    {
        var query = new QueryExpression("organization") { ColumnSet = new ColumnSet("languagecode"), TopCount = 1 };
        var language = _service.RetrieveMultiple(query).Entities[0].GetAttributeValue<int>("languagecode");
        _logger.LogInformation("Environment base language: {Lcid}", language);
        return language;
    }

    private void EnsureGlobalChoices(SchemaDefinition schema, MetadataBuilder builder)
    {
        foreach (var choice in schema.GlobalChoices)
        {
            var existing = _service.TryExecute<RetrieveOptionSetResponse>(new RetrieveOptionSetRequest { Name = choice.SchemaName });
            if (existing is null)
            {
                _logger.LogInformation("Creating global choice {Choice}", choice.SchemaName);
                _service.Execute(builder.BuildGlobalChoice(choice));
                continue;
            }

            var current = ((OptionSetMetadata)existing.OptionSetMetadata).Options.Select(o => o.Value).ToHashSet();
            foreach (var option in choice.Options.Where(o => !current.Contains(o.Value)))
            {
                _logger.LogInformation("Adding option {Value} to {Choice}", option.Value, choice.SchemaName);
                _service.Execute(new InsertOptionValueRequest
                {
                    OptionSetName = choice.SchemaName,
                    Value = option.Value,
                    Label = builder.Label(option.Label),
                    SolutionUniqueName = schema.Solution.UniqueName,
                });
            }
        }
    }

    private void EnsureTable(TableDefinition table, MetadataBuilder builder, IDictionary<string, EntityMetadata> existing, string solution)
    {
        if (!existing.TryGetValue(table.LogicalName, out var metadata))
        {
            if (table.Existing)
            {
                throw new InvalidOperationException($"Table {table.LogicalName} is marked as existing but was not found.");
            }

            _logger.LogInformation("Creating table {Table}", table.SchemaName);
            _service.Execute(builder.BuildTable(table));
            metadata = new EntityMetadata();
        }
        else if (table.Existing)
        {
            // Add the OOB table to the solution without its subcomponents: only our columns travel with it.
            _service.Execute(new AddSolutionComponentRequest
            {
                ComponentType = SolutionComponentEntity,
                ComponentId = metadata.MetadataId!.Value,
                SolutionUniqueName = solution,
                DoNotIncludeSubcomponents = true,
            });
        }

        var columns = (metadata.Attributes ?? Array.Empty<AttributeMetadata>()).Select(a => a.LogicalName).ToHashSet();
        foreach (var column in table.Columns.Where(c => !columns.Contains(c.LogicalName)))
        {
            _logger.LogInformation("Creating column {Table}.{Column} ({Type})", table.LogicalName, column.LogicalName, column.Type);
            _service.Execute(builder.BuildColumn(table.LogicalName, column));
        }
    }

    private Dictionary<string, EntityMetadata> GetExistingMetadata(IEnumerable<string> logicalNames)
    {
        var query = new EntityQueryExpression
        {
            Criteria = new MetadataFilterExpression(LogicalOperator.And)
            {
                Conditions = { new MetadataConditionExpression("LogicalName", MetadataConditionOperator.In, logicalNames.ToArray()) },
            },
            Properties = new MetadataPropertiesExpression("LogicalName", "MetadataId", "Attributes", "Keys"),
            AttributeQuery = new AttributeQueryExpression { Properties = new MetadataPropertiesExpression("LogicalName") },
            KeyQuery = new EntityKeyQueryExpression { Properties = new MetadataPropertiesExpression("LogicalName") },
        };

        var response = (RetrieveMetadataChangesResponse)_service.Execute(new RetrieveMetadataChangesRequest { Query = query });
        return response.EntityMetadata.ToDictionary(e => e.LogicalName, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureEnvironmentVariables(SchemaDefinition schema)
    {
        foreach (var variable in schema.EnvironmentVariables)
        {
            if (_service.FindFirst("environmentvariabledefinition", "schemaname", variable.SchemaName) is not null)
            {
                continue;
            }

            _logger.LogInformation("Creating environment variable {Variable}", variable.SchemaName);
            _service.CreateInSolution(new Entity("environmentvariabledefinition")
            {
                ["schemaname"] = variable.SchemaName,
                ["displayname"] = variable.DisplayName,
                ["description"] = variable.Description,
                ["type"] = new OptionSetValue((int)variable.Type),
                ["defaultvalue"] = variable.DefaultValue,
            }, schema.Solution.UniqueName);
        }
    }
}
