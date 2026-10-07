using System;
using System.Diagnostics;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace SupplyFlow.Plugins.Core;

/// <summary>
/// Base class for every SupplyFlow plugin.
/// </summary>
/// <remarks>
/// Responsibilities kept here so concrete plugins only contain business intent:
/// <list type="bullet">
///   <item>Builds a <see cref="LocalPluginContext"/> (services, tracing, telemetry).</item>
///   <item>Guards against registration mistakes (wrong entity / message / stage).</item>
///   <item>Translates unexpected exceptions into <see cref="InvalidPluginExecutionException"/>
///         while preserving business messages that are already user-friendly.</item>
///   <item>Emits timing information to the trace log and Application Insights (ILogger).</item>
/// </list>
/// Plugins are instantiated once and cached by the platform, so implementations MUST be stateless:
/// never keep IOrganizationService or execution data in instance/static fields.
/// </remarks>
public abstract class PluginBase : IPlugin
{
    protected PluginBase(string? unsecureConfiguration = null, string? secureConfiguration = null)
    {
        UnsecureConfiguration = unsecureConfiguration;
        SecureConfiguration = secureConfiguration;
    }

    protected string? UnsecureConfiguration { get; }

    protected string? SecureConfiguration { get; }

    protected string PluginName => GetType().Name;

    public void Execute(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new InvalidPluginExecutionException(nameof(serviceProvider));
        }

        var context = new LocalPluginContext(serviceProvider);
        var stopwatch = Stopwatch.StartNew();
        var execution = context.ExecutionContext;

        context.Trace(
            $"Entered {PluginName} | {execution.MessageName} {execution.PrimaryEntityName} " +
            $"| stage {execution.Stage} | depth {execution.Depth} | user {execution.UserId} (initiating {execution.InitiatingUserId}) " +
            $"| correlation {execution.CorrelationId}");

        try
        {
            ExecuteDataversePlugin(context);
        }
        catch (InvalidPluginExecutionException)
        {
            // Business validation – message already written for the end user.
            throw;
        }
        catch (FaultException<OrganizationServiceFault> fault)
        {
            context.LogError(fault, $"{PluginName}: Dataverse fault {fault.Detail?.ErrorCode}");
            throw new InvalidPluginExecutionException(
                $"Ocorreu um erro ao processar a operação ({PluginName}). Código: {fault.Detail?.ErrorCode}. " +
                "Se o problema persistir, contate o suporte informando o horário da tentativa.",
                fault);
        }
        catch (Exception ex)
        {
            context.LogError(ex, $"{PluginName}: unexpected error");
            throw new InvalidPluginExecutionException(
                $"Erro inesperado em {PluginName}. Contate o suporte. Correlation: {execution.CorrelationId}",
                ex);
        }
        finally
        {
            context.Trace($"Exiting {PluginName} after {stopwatch.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>Implement the plugin business logic.</summary>
    protected abstract void ExecuteDataversePlugin(LocalPluginContext context);
}
