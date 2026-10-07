using System.Collections.Generic;

namespace SupplyFlow.Deployer.Definitions;

/// <summary>Root of definitions/plugin-registration.json – plugin steps, images, Custom APIs and service endpoints.</summary>
public sealed class RegistrationDefinition
{
    public string Assembly { get; set; } = string.Empty;

    public List<PluginTypeDefinition> Plugins { get; set; } = new();

    public List<CustomApiDefinition> CustomApis { get; set; } = new();

    public List<ServiceEndpointDefinition> ServiceEndpoints { get; set; } = new();
}

public sealed class PluginTypeDefinition
{
    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<StepDefinition> Steps { get; set; } = new();
}

public enum StepStage
{
    PreValidation = 10,
    PreOperation = 20,
    PostOperation = 40,
}

public enum StepMode
{
    Synchronous = 0,
    Asynchronous = 1,
}

public sealed class StepDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Entity { get; set; } = string.Empty;

    public StepStage Stage { get; set; }

    public StepMode Mode { get; set; } = StepMode.Synchronous;

    public int Rank { get; set; } = 1;

    public List<string> FilteringAttributes { get; set; } = new();

    public string? UnsecureConfiguration { get; set; }

    public bool AsyncAutoDelete { get; set; }

    public List<ImageDefinition> Images { get; set; } = new();
}

public enum ImageType
{
    PreImage = 0,
    PostImage = 1,
    Both = 2,
}

public sealed class ImageDefinition
{
    public string Name { get; set; } = string.Empty;

    public ImageType Type { get; set; }

    public List<string> Attributes { get; set; } = new();
}

public enum CustomApiBindingType
{
    Global = 0,
    Entity = 1,
    EntityCollection = 2,
}

public enum CustomApiStepType
{
    None = 0,
    AsyncOnly = 1,
    SyncAndAsync = 2,
}

/// <summary>customapirequestparameter / customapiresponseproperty "type" choice.</summary>
public enum CustomApiParameterType
{
    Boolean = 0,
    DateTime = 1,
    Decimal = 2,
    Entity = 3,
    EntityCollection = 4,
    EntityReference = 5,
    Float = 6,
    Integer = 7,
    Money = 8,
    Picklist = 9,
    String = 10,
    StringArray = 11,
    Guid = 12,
}

public sealed class CustomApiDefinition
{
    public string UniqueName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public CustomApiBindingType BindingType { get; set; }

    public string? BoundEntity { get; set; }

    public bool IsFunction { get; set; }

    public bool IsPrivate { get; set; }

    public CustomApiStepType AllowedStepType { get; set; } = CustomApiStepType.SyncAndAsync;

    public string? ExecutePrivilegeName { get; set; }

    public string PluginType { get; set; } = string.Empty;

    public List<CustomApiParameterDefinition> RequestParameters { get; set; } = new();

    public List<CustomApiParameterDefinition> ResponseProperties { get; set; } = new();
}

public sealed class CustomApiParameterDefinition
{
    public string UniqueName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public CustomApiParameterType Type { get; set; }

    public bool IsOptional { get; set; }

    public string? EntityLogicalName { get; set; }
}

/// <summary>Azure Service Bus queue that receives the RemoteExecutionContext (Dataverse → Azure).</summary>
public sealed class ServiceEndpointDefinition
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>e.g. sb://contoso.servicebus.windows.net/ – may be overridden by SUPPLYFLOW_SB_NAMESPACE.</summary>
    public string NamespaceAddress { get; set; } = string.Empty;

    public string QueueName { get; set; } = string.Empty;

    public string SasKeyName { get; set; } = string.Empty;

    /// <summary>Environment variable that holds the SAS key at deploy time (never stored in source control).</summary>
    public string SasKeyEnvironmentVariable { get; set; } = "SUPPLYFLOW_SB_SASKEY";

    public List<StepDefinition> Steps { get; set; } = new();
}
