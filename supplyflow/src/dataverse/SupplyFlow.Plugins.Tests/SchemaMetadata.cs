using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk.Metadata;
using SupplyFlow.Deployer.Definitions;
using SupplyFlow.Deployer.Metadata;

namespace SupplyFlow.Plugins.Tests;

/// <summary>
/// Builds FakeXrmEasy metadata from definitions/schema.json – the same file the Deployer applies to
/// real environments – so tests run against the real column types (and fail if the schema drifts).
/// </summary>
public static class SchemaMetadata
{
    private static readonly Lazy<IReadOnlyList<EntityMetadata>> Cached = new(Build);

    public static string DefinitionsFolder => Path.Combine(AppContext.BaseDirectory, "definitions");

    public static SchemaDefinition Schema => DefinitionLoader.LoadSchema(Path.Combine(DefinitionsFolder, "schema.json"));

    public static RegistrationDefinition Registration =>
        DefinitionLoader.LoadRegistration(Path.Combine(DefinitionsFolder, "plugin-registration.json"));

    public static IReadOnlyList<EntityMetadata> All => Cached.Value;

    private static IReadOnlyList<EntityMetadata> Build()
    {
        var schema = Schema;
        var builder = new MetadataBuilder(1046, schema.Solution.UniqueName);
        var result = new List<EntityMetadata>();

        foreach (var table in schema.Tables)
        {
            var attributes = new List<AttributeMetadata>
            {
                new StateAttributeMetadata { LogicalName = "statecode" },
                new StatusAttributeMetadata { LogicalName = "statuscode" },
                new UniqueIdentifierAttributeMetadata { LogicalName = table.LogicalName + "id" },
                new LookupAttributeMetadata { LogicalName = "ownerid" },
            };

            attributes.Add(table.Existing
                ? new StringAttributeMetadata { LogicalName = "name" }
                : new StringAttributeMetadata { LogicalName = table.PrimaryName.SchemaName.ToLowerInvariant() });

            foreach (var column in table.Columns)
            {
                var attribute = builder.BuildAttribute(column);
                attribute.LogicalName = column.LogicalName;
                attributes.Add(attribute);
            }

            attributes.AddRange(schema.Lookups
                .Where(l => l.ReferencingTable == table.LogicalName)
                .Select(l => new LookupAttributeMetadata { LogicalName = l.LogicalName, Targets = new[] { l.ReferencedTable } }));

            result.Add(Entity(table.LogicalName, attributes));
        }

        foreach (var relationship in schema.ManyToMany)
        {
            result.Add(Entity(relationship.IntersectEntity, new AttributeMetadata[]
            {
                new UniqueIdentifierAttributeMetadata { LogicalName = relationship.Entity1 + "id" },
                new UniqueIdentifierAttributeMetadata { LogicalName = relationship.Entity2 + "id" },
            }));
        }

        return result;
    }

    private static EntityMetadata Entity(string logicalName, IEnumerable<AttributeMetadata> attributes)
    {
        var metadata = new EntityMetadata { LogicalName = logicalName };
        metadata.SetAttributeCollection(attributes.ToArray());
        return metadata;
    }

    private static void SetAttributeCollection(this EntityMetadata metadata, AttributeMetadata[] attributes)
    {
        // EntityMetadata.Attributes has no public setter in the SDK.
        typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(metadata, attributes);
    }
}
