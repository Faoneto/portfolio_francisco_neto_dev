using System;
using System.Linq;
using System.Reflection;
using FakeXrmEasy.Abstractions;
using FakeXrmEasy.Abstractions.Plugins.Enums;
using FakeXrmEasy.Pipeline;
using FakeXrmEasy.Plugins.Definitions;
using FakeXrmEasy.Plugins.PluginImages;
using FakeXrmEasy.Plugins.PluginSteps;
using SupplyFlow.Deployer.Definitions;
using SupplyFlow.Plugins.Core;

namespace SupplyFlow.Plugins.Tests.Integration;

/// <summary>
/// Registers in FakeXrmEasy's pipeline simulation exactly the steps declared in plugin-registration.json.
/// If a step is registered with the wrong stage, a missing image or a missing filtering attribute,
/// the end-to-end tests fail – the registration file is tested, not only the C# code.
/// </summary>
public static class RegistrationDrivenPipeline
{
    private static readonly MethodInfo RegisterMethod = typeof(IXrmFakedContextPipelineExtensions)
        .GetMethods()
        .Single(m => m.Name == nameof(IXrmFakedContextPipelineExtensions.RegisterPluginStep)
                     && m.GetGenericArguments().Length == 1
                     && m.GetParameters().Length == 2);

    public static int RegisterAll(IXrmFakedContext context, RegistrationDefinition registration)
    {
        var count = 0;
        foreach (var plugin in registration.Plugins)
        {
            var type = ResolvePluginType(plugin.Type);
            foreach (var step in plugin.Steps)
            {
                var definition = new PluginStepDefinition
                {
                    MessageName = step.Message,
                    EntityLogicalName = step.Entity,
                    Stage = (ProcessingStepStage)(int)step.Stage,
                    Mode = (ProcessingStepMode)(int)step.Mode,
                    Rank = step.Rank,
                    FilteringAttributes = step.FilteringAttributes,
                    ImagesDefinitions = step.Images
                        .Select(i => (IPluginImageDefinition)new PluginImageDefinition(
                            i.Name, (ProcessingStepImageType)(int)i.Type, i.Attributes.ToArray()))
                        .ToList(),
                };

                RegisterMethod.MakeGenericMethod(type).Invoke(null, new object[] { context, definition });
                count++;
            }
        }

        return count;
    }

    public static Type ResolvePluginType(string typeName)
    {
        return typeof(PluginBase).Assembly.GetType(typeName, throwOnError: true)!;
    }
}
