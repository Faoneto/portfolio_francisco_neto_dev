using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.PluginTelemetry;

namespace SupplyFlow.Plugins.Core;

/// <summary>
/// Lazily resolves the services a plugin needs and exposes helpers for the most common
/// pipeline operations (target, images, registration guards).
/// </summary>
public sealed class LocalPluginContext
{
    private readonly Lazy<IOrganizationService> _userService;
    private readonly Lazy<IOrganizationService> _systemService;

    public LocalPluginContext(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        ExecutionContext = serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext
            ?? throw new InvalidPluginExecutionException("IPluginExecutionContext not available.");
        TracingService = serviceProvider.GetService(typeof(ITracingService)) as ITracingService;
        Logger = serviceProvider.GetService(typeof(ILogger)) as ILogger;

        var factory = serviceProvider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory
            ?? throw new InvalidPluginExecutionException("IOrganizationServiceFactory not available.");
        _userService = new Lazy<IOrganizationService>(() => factory.CreateOrganizationService(ExecutionContext.UserId));
        _systemService = new Lazy<IOrganizationService>(() => factory.CreateOrganizationService(null));
    }

    public IServiceProvider ServiceProvider { get; }

    public IPluginExecutionContext ExecutionContext { get; }

    public ITracingService? TracingService { get; }

    /// <summary>Application Insights logger (null when telemetry export is not configured).</summary>
    public ILogger? Logger { get; }

    /// <summary>Service running with the privileges of the calling user (respects security roles).</summary>
    public IOrganizationService UserService => _userService.Value;

    /// <summary>Service running as SYSTEM. Use only for reads/writes the user must not need privileges for.</summary>
    public IOrganizationService SystemService => _systemService.Value;

    public string MessageName => ExecutionContext.MessageName;

    public PluginStage Stage => (PluginStage)ExecutionContext.Stage;

    public void Trace(string message)
    {
        TracingService?.Trace(message);
    }

    public void LogError(Exception exception, string message)
    {
        Trace($"{message}: {exception}");
        Logger?.LogError(exception, message);
    }

    /// <summary>Returns the Target entity for Create/Update messages.</summary>
    public Entity GetTarget()
    {
        if (ExecutionContext.InputParameters.TryGetValue("Target", out var target) && target is Entity entity)
        {
            return entity;
        }

        throw new InvalidPluginExecutionException($"Target entity not found for message {MessageName}.");
    }

    /// <summary>Returns the Target reference for Delete (and bound Custom API) messages.</summary>
    public EntityReference GetTargetReference()
    {
        if (ExecutionContext.InputParameters.TryGetValue("Target", out var target))
        {
            switch (target)
            {
                case EntityReference reference:
                    return reference;
                case Entity entity:
                    return entity.ToEntityReference();
            }
        }

        throw new InvalidPluginExecutionException($"Target reference not found for message {MessageName}.");
    }

    public Entity? GetPreImage(string name = ImageNames.PreImage)
    {
        return ExecutionContext.PreEntityImages.TryGetValue(name, out var image) ? image : null;
    }

    public Entity? GetPostImage(string name = ImageNames.PostImage)
    {
        return ExecutionContext.PostEntityImages.TryGetValue(name, out var image) ? image : null;
    }

    /// <summary>
    /// Target attributes overlaid on the pre-image: the "future" state of the record during Update.
    /// </summary>
    public Entity GetMergedTarget(string preImageName = ImageNames.PreImage)
    {
        var target = GetTarget();
        var merged = new Entity(target.LogicalName, target.Id);
        var preImage = GetPreImage(preImageName);

        if (preImage is not null)
        {
            foreach (var attribute in preImage.Attributes)
            {
                merged[attribute.Key] = attribute.Value;
            }
        }

        foreach (var attribute in target.Attributes)
        {
            merged[attribute.Key] = attribute.Value;
        }

        return merged;
    }

    /// <summary>
    /// Fails fast when the step is registered against the wrong entity/message/stage.
    /// Cheap insurance against misconfigured registrations reaching production.
    /// </summary>
    public void EnsureRegistration(string entityName, PluginStage stage, params string[] messages)
    {
        var execution = ExecutionContext;
        var entityMatches = string.Equals(execution.PrimaryEntityName, entityName, StringComparison.OrdinalIgnoreCase);
        var messageMatches = messages.Any(m => string.Equals(m, execution.MessageName, StringComparison.OrdinalIgnoreCase));

        if (!entityMatches || !messageMatches || Stage != stage)
        {
            throw new InvalidPluginExecutionException(
                $"Invalid registration: expected {string.Join("/", messages)} of {entityName} at {stage}, " +
                $"got {execution.MessageName} of {execution.PrimaryEntityName} at {Stage}.");
        }
    }

    /// <summary>
    /// True when this execution was triggered by a cascade Delete of the given parent entity
    /// (e.g. lines being deleted because the requisition is being deleted).
    /// </summary>
    public bool IsCascadeDeleteFrom(string parentEntityName)
    {
        for (var parent = ExecutionContext.ParentContext; parent is not null; parent = parent.ParentContext)
        {
            if (parent.MessageName == MessageNames.Delete &&
                string.Equals(parent.PrimaryEntityName, parentEntityName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
