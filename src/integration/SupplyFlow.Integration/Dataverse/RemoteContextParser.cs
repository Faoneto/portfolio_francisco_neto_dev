using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace SupplyFlow.Integration.Dataverse;

public sealed record EntityReferenceValue(string LogicalName, Guid Id, string? Name);

/// <summary>Minimal, typed view of the RemoteExecutionContext posted by a Dataverse service endpoint.</summary>
public sealed record DataverseEvent(
    string MessageName,
    string PrimaryEntityName,
    Guid PrimaryEntityId,
    Guid CorrelationId,
    Guid InitiatingUserId,
    int Depth,
    IReadOnlyDictionary<string, object?> Target,
    IReadOnlyDictionary<string, object?> PostImage)
{
    public T? Get<T>(string attribute)
    {
        if (PostImage.TryGetValue(attribute, out var value) || Target.TryGetValue(attribute, out value))
        {
            return value is T typed ? typed : default;
        }

        return default;
    }
}

/// <summary>
/// Parses the JSON RemoteExecutionContext (service endpoint message format = JSON).
/// </summary>
/// <remarks>
/// The payload uses WCF DataContract JSON conventions ("__type" hints, key/value arrays, "/Date(ms)/").
/// A small tolerant parser over System.Text.Json is used instead of DataContractJsonSerializer, which
/// needs every known type registered and silently turns unknown attribute values into strings.
/// </remarks>
public static class RemoteContextParser
{
    public static DataverseEvent Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var target = root.TryGetProperty("InputParameters", out var inputs)
            ? KeyValues(inputs).Where(kv => kv.Key == "Target").Select(kv => ReadEntityAttributes(kv.Value)).FirstOrDefault()
            : null;

        var postImage = root.TryGetProperty("PostEntityImages", out var images)
            ? KeyValues(images).Select(kv => ReadEntityAttributes(kv.Value)).FirstOrDefault()
            : null;

        return new DataverseEvent(
            root.GetProperty("MessageName").GetString() ?? string.Empty,
            root.GetProperty("PrimaryEntityName").GetString() ?? string.Empty,
            ReadGuid(root, "PrimaryEntityId"),
            ReadGuid(root, "CorrelationId"),
            ReadGuid(root, "InitiatingUserId"),
            root.TryGetProperty("Depth", out var depth) ? depth.GetInt32() : 1,
            target ?? new Dictionary<string, object?>(),
            postImage ?? new Dictionary<string, object?>());
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> KeyValues(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            yield return new KeyValuePair<string, JsonElement>(item.GetProperty("key").GetString() ?? string.Empty, item.GetProperty("value"));
        }
    }

    private static Dictionary<string, object?> ReadEntityAttributes(JsonElement entity)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity.ValueKind != JsonValueKind.Object || !entity.TryGetProperty("Attributes", out var list))
        {
            return attributes;
        }

        foreach (var (key, value) in KeyValues(list))
        {
            attributes[key] = ReadValue(value);
        }

        return attributes;
    }

    private static object? ReadValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                return value.TryGetInt32(out var i) ? i : value.GetDecimal();
            case JsonValueKind.String:
                return ReadString(value.GetString()!);
        }

        var type = value.TryGetProperty("__type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var typeName = type.Split(':')[0];

        return typeName switch
        {
            "OptionSetValue" => value.GetProperty("Value").GetInt32(),
            "Money" => value.GetProperty("Value").GetDecimal(),
            "EntityReference" => new EntityReferenceValue(
                value.GetProperty("LogicalName").GetString() ?? string.Empty,
                value.GetProperty("Id").GetGuid(),
                value.TryGetProperty("Name", out var name) ? name.GetString() : null),
            _ => value.GetRawText(),
        };
    }

    private static object ReadString(string text)
    {
        // WCF JSON date: "/Date(1759850000000)/" or "/Date(1759850000000-0300)/"
        if (text.StartsWith("/Date(", StringComparison.Ordinal) && text.EndsWith(")/", StringComparison.Ordinal))
        {
            var inner = text.Substring(6, text.Length - 8);
            var end = inner.IndexOfAny(new[] { '+', '-' }, 1);
            var milliseconds = long.Parse(end > 0 ? inner[..end] : inner, CultureInfo.InvariantCulture);
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        return Guid.TryParse(text, out var guid) ? guid : text;
    }

    private static Guid ReadGuid(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid)
            ? guid
            : Guid.Empty;
    }
}
