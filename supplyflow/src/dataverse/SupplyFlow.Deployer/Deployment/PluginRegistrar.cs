using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Deployer.Definitions;

namespace SupplyFlow.Deployer.Deployment;

/// <summary>
/// Registers the plugin assembly, plugin types, steps, images, Custom APIs and Service Bus endpoints
/// from definitions/plugin-registration.json ("registration as code" – no manual Plugin Registration Tool clicks).
/// </summary>
/// <remarks>
/// Steps of managed plugin types that are no longer declared are deleted, so the environment converges to the file.
/// </remarks>
public sealed class PluginRegistrar
{
    private const int IsolationModeSandbox = 2;
    private const int SourceTypeDatabase = 0;

    private readonly IOrganizationService _service;
    private readonly ILogger _logger;
    private readonly string _solution;

    public PluginRegistrar(IOrganizationService service, ILogger logger, string solutionUniqueName)
    {
        _service = service;
        _logger = logger;
        _solution = solutionUniqueName;
    }

    public void Register(RegistrationDefinition registration, string assemblyPath)
    {
        var assemblyId = UpsertAssembly(assemblyPath);
        var pluginTypeIds = new Dictionary<string, Guid>();

        foreach (var plugin in registration.Plugins)
        {
            var pluginTypeId = UpsertPluginType(assemblyId, plugin);
            pluginTypeIds[plugin.Type] = pluginTypeId;
            SyncSteps(new EntityReference("plugintype", pluginTypeId), plugin.Steps);
        }

        foreach (var api in registration.CustomApis)
        {
            UpsertCustomApi(api, pluginTypeIds[api.PluginType]);
        }

        foreach (var endpoint in registration.ServiceEndpoints)
        {
            var endpointId = UpsertServiceEndpoint(endpoint);
            if (endpointId is not null)
            {
                SyncSteps(new EntityReference("serviceendpoint", endpointId.Value), endpoint.Steps);
            }
        }
    }

    private Guid UpsertAssembly(string assemblyPath)
    {
        var name = AssemblyName.GetAssemblyName(assemblyPath);
        var publicKeyToken = name.GetPublicKeyToken();
        if (publicKeyToken is null || publicKeyToken.Length == 0)
        {
            throw new InvalidOperationException("Plugin assemblies must be strong-name signed.");
        }

        var content = Convert.ToBase64String(File.ReadAllBytes(assemblyPath));
        var existing = _service.FindFirst("pluginassembly", "name", name.Name!, "pluginassemblyid", "version");

        if (existing is not null)
        {
            _logger.LogInformation("Updating plugin assembly {Assembly} {Old} → {New}", name.Name, existing.GetAttributeValue<string>("version"), name.Version);
            _service.Update(new Entity("pluginassembly", existing.Id)
            {
                ["content"] = content,
                ["version"] = name.Version!.ToString(),
            });
            return existing.Id;
        }

        _logger.LogInformation("Registering plugin assembly {Assembly} {Version}", name.Name, name.Version);
        return _service.CreateInSolution(new Entity("pluginassembly")
        {
            ["name"] = name.Name,
            ["content"] = content,
            ["version"] = name.Version!.ToString(),
            ["culture"] = string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName,
            ["publickeytoken"] = string.Concat(publicKeyToken.Select(b => b.ToString("x2"))),
            ["isolationmode"] = new OptionSetValue(IsolationModeSandbox),
            ["sourcetype"] = new OptionSetValue(SourceTypeDatabase),
        }, _solution);
    }

    private Guid UpsertPluginType(Guid assemblyId, PluginTypeDefinition plugin)
    {
        var query = new QueryExpression("plugintype") { ColumnSet = new ColumnSet("plugintypeid"), TopCount = 1 };
        query.Criteria.AddCondition("typename", ConditionOperator.Equal, plugin.Type);
        query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);
        var existing = _service.RetrieveMultiple(query).Entities.FirstOrDefault();
        if (existing is not null)
        {
            return existing.Id;
        }

        _logger.LogInformation("Registering plugin type {Type}", plugin.Type);
        return _service.Create(new Entity("plugintype")
        {
            ["typename"] = plugin.Type,
            ["name"] = plugin.Type,
            ["friendlyname"] = plugin.Type.Split('.').Last(),
            ["description"] = plugin.Description,
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
        });
    }

    private void SyncSteps(EntityReference eventHandler, IReadOnlyCollection<StepDefinition> steps)
    {
        var query = new QueryExpression("sdkmessageprocessingstep") { ColumnSet = new ColumnSet("name") };
        query.Criteria.AddCondition("eventhandler", ConditionOperator.Equal, eventHandler.Id);
        // Never touch the platform-managed main operation (stage 30) that backs a Custom API.
        query.Criteria.AddCondition("stage", ConditionOperator.NotEqual, 30);
        var registered = _service.RetrieveMultiple(query).Entities.ToDictionary(e => e.GetAttributeValue<string>("name"), e => e.Id);

        foreach (var orphan in registered.Where(r => steps.All(s => s.Name != r.Key)))
        {
            _logger.LogWarning("Deleting step no longer declared: {Step}", orphan.Key);
            _service.Delete("sdkmessageprocessingstep", orphan.Value);
        }

        foreach (var step in steps)
        {
            var messageId = GetMessageId(step.Message);
            var record = new Entity("sdkmessageprocessingstep")
            {
                ["name"] = step.Name,
                ["description"] = step.Name,
                ["eventhandler"] = eventHandler,
                ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", GetMessageFilterId(messageId, step.Entity)),
                ["stage"] = new OptionSetValue((int)step.Stage),
                ["mode"] = new OptionSetValue((int)step.Mode),
                ["rank"] = step.Rank,
                ["filteringattributes"] = step.FilteringAttributes.Count == 0 ? null : string.Join(",", step.FilteringAttributes),
                ["configuration"] = step.UnsecureConfiguration,
                ["asyncautodelete"] = step.Mode == StepMode.Asynchronous && step.AsyncAutoDelete,
                ["supporteddeployment"] = new OptionSetValue(0), // server only
            };

            Guid stepId;
            if (registered.TryGetValue(step.Name, out stepId))
            {
                record.Id = stepId;
                _service.Update(record);
                _logger.LogInformation("Updated step {Step}", step.Name);
            }
            else
            {
                stepId = _service.CreateInSolution(record, _solution);
                _logger.LogInformation("Registered step {Step}", step.Name);
            }

            SyncImages(stepId, step);
        }
    }

    private void SyncImages(Guid stepId, StepDefinition step)
    {
        var query = new QueryExpression("sdkmessageprocessingstepimage") { ColumnSet = new ColumnSet("entityalias") };
        query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId);
        var registered = _service.RetrieveMultiple(query).Entities.ToDictionary(e => e.GetAttributeValue<string>("entityalias"), e => e.Id);

        foreach (var orphan in registered.Where(r => step.Images.All(i => i.Name != r.Key)))
        {
            _service.Delete("sdkmessageprocessingstepimage", orphan.Value);
        }

        foreach (var image in step.Images)
        {
            var entity = new Entity("sdkmessageprocessingstepimage")
            {
                ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
                ["name"] = image.Name,
                ["entityalias"] = image.Name,
                ["imagetype"] = new OptionSetValue((int)image.Type),
                ["attributes"] = string.Join(",", image.Attributes),
                // Create/Delete expose the record as "Id"/"Target"; Update uses "Target".
                ["messagepropertyname"] = step.Message == "Create" ? "Id" : "Target",
            };

            if (registered.TryGetValue(image.Name, out var imageId))
            {
                entity.Id = imageId;
                _service.Update(entity);
            }
            else
            {
                _service.Create(entity);
            }
        }
    }

    private void UpsertCustomApi(CustomApiDefinition api, Guid pluginTypeId)
    {
        var existing = _service.FindFirst("customapi", "uniquename", api.UniqueName, "customapiid");
        var record = new Entity("customapi")
        {
            ["displayname"] = api.DisplayName,
            ["description"] = api.Description ?? api.DisplayName,
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
            ["executeprivilegename"] = api.ExecutePrivilegeName,
        };

        Guid apiId;
        if (existing is not null)
        {
            // uniquename, bindingtype, boundentitylogicalname and isfunction are immutable after creation.
            record.Id = apiId = existing.Id;
            _service.Update(record);
            _logger.LogInformation("Updated Custom API {Api}", api.UniqueName);
        }
        else
        {
            record["uniquename"] = api.UniqueName;
            record["name"] = api.UniqueName;
            record["bindingtype"] = new OptionSetValue((int)api.BindingType);
            record["boundentitylogicalname"] = api.BoundEntity;
            record["isfunction"] = api.IsFunction;
            record["isprivate"] = api.IsPrivate;
            record["allowedcustomprocessingsteptype"] = new OptionSetValue((int)api.AllowedStepType);
            apiId = _service.CreateInSolution(record, _solution);
            _logger.LogInformation("Created Custom API {Api}", api.UniqueName);
        }

        foreach (var parameter in api.RequestParameters)
        {
            UpsertApiMember("customapirequestparameter", apiId, api.UniqueName, parameter, isRequest: true);
        }

        foreach (var property in api.ResponseProperties)
        {
            UpsertApiMember("customapiresponseproperty", apiId, api.UniqueName, property, isRequest: false);
        }
    }

    private void UpsertApiMember(string entityName, Guid apiId, string apiName, CustomApiParameterDefinition member, bool isRequest)
    {
        var query = new QueryExpression(entityName) { ColumnSet = new ColumnSet(false), TopCount = 1 };
        query.Criteria.AddCondition("customapiid", ConditionOperator.Equal, apiId);
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, member.UniqueName);
        if (_service.RetrieveMultiple(query).Entities.Count > 0)
        {
            return;
        }

        var record = new Entity(entityName)
        {
            ["customapiid"] = new EntityReference("customapi", apiId),
            ["uniquename"] = member.UniqueName,
            ["name"] = $"{apiName}.{member.UniqueName}",
            ["displayname"] = member.DisplayName,
            ["description"] = member.Description ?? member.DisplayName,
            ["type"] = new OptionSetValue((int)member.Type),
            ["logicalentityname"] = member.EntityLogicalName,
        };

        if (isRequest)
        {
            record["isoptional"] = member.IsOptional;
        }

        _service.CreateInSolution(record, _solution);
    }

    private Guid? UpsertServiceEndpoint(ServiceEndpointDefinition endpoint)
    {
        var sasKey = Environment.GetEnvironmentVariable(endpoint.SasKeyEnvironmentVariable);
        var namespaceAddress = Environment.GetEnvironmentVariable("SUPPLYFLOW_SB_NAMESPACE") ?? endpoint.NamespaceAddress;
        if (string.IsNullOrWhiteSpace(sasKey) || namespaceAddress.Contains('<'))
        {
            _logger.LogWarning(
                "Skipping service endpoint {Endpoint}: set SUPPLYFLOW_SB_NAMESPACE and {Variable} to register it.",
                endpoint.Name, endpoint.SasKeyEnvironmentVariable);
            return null;
        }

        var record = new Entity("serviceendpoint")
        {
            ["name"] = endpoint.Name,
            ["description"] = endpoint.Description,
            ["contract"] = new OptionSetValue(2),        // Queue
            ["connectionmode"] = new OptionSetValue(1),  // Normal
            ["authtype"] = new OptionSetValue(2),        // SASKey
            ["messageformat"] = new OptionSetValue(2),   // JSON
            ["userclaim"] = new OptionSetValue(1),       // None
            ["namespaceaddress"] = namespaceAddress,
            ["path"] = endpoint.QueueName,
            ["saskeyname"] = endpoint.SasKeyName,
            ["saskey"] = sasKey,
        };

        var existing = _service.FindFirst("serviceendpoint", "name", endpoint.Name, "serviceendpointid");
        if (existing is not null)
        {
            record.Id = existing.Id;
            _service.Update(record);
            return existing.Id;
        }

        _logger.LogInformation("Registering service endpoint {Endpoint}", endpoint.Name);
        return _service.CreateInSolution(record, _solution);
    }

    private Guid GetMessageId(string message)
    {
        return _service.FindFirst("sdkmessage", "name", message, "sdkmessageid")?.Id
            ?? throw new InvalidOperationException($"SDK message '{message}' not found.");
    }

    private Guid GetMessageFilterId(Guid messageId, string entity)
    {
        var query = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("sdkmessagefilterid"), TopCount = 1 };
        query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
        query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, entity);
        return _service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"Message filter for '{entity}' not found (is the table created and published?).");
    }
}
