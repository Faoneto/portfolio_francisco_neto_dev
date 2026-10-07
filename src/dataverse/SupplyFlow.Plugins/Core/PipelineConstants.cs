namespace SupplyFlow.Plugins.Core;

public enum PluginStage
{
    PreValidation = 10,
    PreOperation = 20,
    MainOperation = 30,
    PostOperation = 40,
}

public static class MessageNames
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
}

public static class ImageNames
{
    public const string PreImage = "PreImage";
    public const string PostImage = "PostImage";
}

/// <summary>Dataverse error codes handled explicitly by the plugins.</summary>
public static class DataverseErrorCodes
{
    /// <summary>0x80060882 – ConcurrencyVersionMismatch (optimistic concurrency failure).</summary>
    public const int ConcurrencyVersionMismatch = unchecked((int)0x80060882);
}
