using System;
using Microsoft.Xrm.Sdk;

namespace SupplyFlow.Plugins.Core;

public static class EntityExtensions
{
    public static decimal GetMoney(this Entity entity, string attribute)
    {
        return entity.GetAttributeValue<Money>(attribute)?.Value ?? 0m;
    }

    public static int? GetChoice(this Entity entity, string attribute)
    {
        return entity.GetAttributeValue<OptionSetValue>(attribute)?.Value;
    }

    public static TEnum? GetChoice<TEnum>(this Entity entity, string attribute)
        where TEnum : struct, Enum
    {
        var value = entity.GetAttributeValue<OptionSetValue>(attribute);
        return value is null ? null : (TEnum)Enum.ToObject(typeof(TEnum), value.Value);
    }

    public static void SetChoice<TEnum>(this Entity entity, string attribute, TEnum value)
        where TEnum : struct, Enum
    {
        entity[attribute] = new OptionSetValue(Convert.ToInt32(value));
    }

    /// <summary>True when the attribute is present in the Target (i.e. it is being changed).</summary>
    public static bool IsChanging(this Entity target, string attribute)
    {
        return target.Attributes.ContainsKey(attribute);
    }
}
