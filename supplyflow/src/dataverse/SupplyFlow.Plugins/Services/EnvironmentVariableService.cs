using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SupplyFlow.Plugins.Services;

/// <summary>
/// Reads Dataverse environment variables (current value, falling back to default value).
/// </summary>
/// <remarks>
/// Values are cached per organization for a short period. A static cache is safe here because it is
/// thread-safe, holds only immutable strings and never holds an IOrganizationService instance.
/// The short TTL keeps configuration changes visible without a plugin re-registration.
/// </remarks>
public sealed class EnvironmentVariableService
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);

    private readonly IOrganizationService _service;
    private readonly Guid _organizationId;
    private readonly Func<DateTime> _utcNow;

    public EnvironmentVariableService(IOrganizationService service, Guid organizationId, Func<DateTime>? utcNow = null)
    {
        _service = service;
        _organizationId = organizationId;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string? GetString(string schemaName)
    {
        var key = $"{_organizationId:N}|{schemaName}";
        var now = _utcNow();

        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresOn > now)
        {
            return cached.Value;
        }

        var value = Load(schemaName);
        Cache[key] = new CacheEntry(value, now.Add(TimeToLive));
        return value;
    }

    public bool GetBoolean(string schemaName, bool defaultValue)
    {
        var raw = GetString(schemaName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        // Dataverse stores Yes/No environment variables as "yes"/"no"; accept true/false as well.
        return raw!.Trim().ToLowerInvariant() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => defaultValue,
        };
    }

    /// <summary>Test hook – clears the process-wide cache.</summary>
    public static void ClearCache() => Cache.Clear();

    private string? Load(string schemaName)
    {
        var query = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("defaultvalue"),
            TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName) } },
        };

        var valueLink = query.AddLink(
            "environmentvariablevalue", "environmentvariabledefinitionid", "environmentvariabledefinitionid", JoinOperator.LeftOuter);
        valueLink.EntityAlias = "v";
        valueLink.Columns = new ColumnSet("value");

        var definition = _service.RetrieveMultiple(query).Entities.FirstOrDefault();
        if (definition is null)
        {
            return null;
        }

        var currentValue = definition.GetAttributeValue<AliasedValue>("v.value")?.Value as string;
        return !string.IsNullOrEmpty(currentValue) ? currentValue : definition.GetAttributeValue<string>("defaultvalue");
    }

    private sealed record CacheEntry(string? Value, DateTime ExpiresOn);
}
