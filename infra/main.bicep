// SupplyFlow – Azure integration infrastructure
//   Dataverse ──(service endpoint, SAS send-only)──▶ Service Bus queue ──▶ Function App (Flex Consumption)
//   Function ──(user-assigned managed identity)──▶ Dataverse Web API / Service Bus / Storage – no secrets.
//
// Deploy:
//   az group create -n rg-supplyflow-dev -l brazilsouth
//   az deployment group create -g rg-supplyflow-dev -f infra/main.bicep -p infra/main.dev.bicepparam

targetScope = 'resourceGroup'

@description('Azure region. Brazil South keeps data close to the Dataverse environment (crm2).')
param location string = resourceGroup().location

@allowed(['dev', 'test', 'prod'])
param environmentName string = 'dev'

@description('Short prefix used in resource names.')
@maxLength(10)
param namePrefix string = 'supplyflow'

@description('Dataverse environment URL, e.g. https://contoso-dev.crm2.dynamics.com')
param dataverseUrl string

@description('SAP OData base URL. Leave empty to use the built-in SAP mock function.')
param sapBaseUrl string = ''

@description('Must match Sap__MaxDeliveryCount in the Function settings.')
@minValue(1)
@maxValue(10)
param maxDeliveryCount int = 5

var suffix = uniqueString(resourceGroup().id, environmentName)
var baseName = '${namePrefix}-${environmentName}'
var queueName = 'sap-purchase-requisitions'
var deploymentContainer = 'deploymentpackage'
var functionAppName = 'func-${baseName}-${suffix}'
var tags = {
  application: 'SupplyFlow'
  environment: environmentName
  'managed-by': 'bicep'
}

// Built-in role definition ids
var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  monitoringMetricsPublisher: '3913510d-42f4-4e42-8a64-420c390055eb'
}

// ---------- Observability ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${baseName}-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${baseName}-${suffix}'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: true
  }
}

// ---------- Identity ----------
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${baseName}-${suffix}'
  location: location
  tags: tags
}

// ---------- Storage (Functions host + deployment package) ----------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('st${replace(namePrefix, '-', '')}${environmentName}${suffix}', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }

  resource blobs 'blobServices' = {
    name: 'default'

    resource deployment 'containers' = {
      name: deploymentContainer
    }
  }
}

// ---------- Service Bus ----------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${baseName}-${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' }
  properties: {
    minimumTlsVersion: '1.2'
  }

  resource queue 'queues' = {
    name: queueName
    properties: {
      maxDeliveryCount: maxDeliveryCount
      lockDuration: 'PT2M'
      defaultMessageTimeToLive: 'P7D'
      deadLetteringOnMessageExpiration: true
      requiresDuplicateDetection: true
      duplicateDetectionHistoryTimeWindow: 'PT10M'
    }

    // Dataverse service endpoints authenticate with SAS: grant Send only, scoped to this queue.
    resource dataverseSend 'authorizationRules' = {
      name: 'dataverse-send'
      properties: {
        rights: ['Send']
      }
    }
  }
}

// ---------- Function App (Flex Consumption, .NET 8 isolated) ----------
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${baseName}-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identity.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING', value: 'ClientId=${identity.properties.clientId};Authorization=AAD' }
        { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
        { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
        { name: 'ServiceBusConnection__credential', value: 'managedidentity' }
        { name: 'ServiceBusConnection__clientId', value: identity.properties.clientId }
        { name: 'SapQueueName', value: queueName }
        { name: 'Dataverse__Url', value: dataverseUrl }
        { name: 'Sap__BaseUrl', value: empty(sapBaseUrl) ? 'https://${functionAppName}.azurewebsites.net/api/sap-mock/' : sapBaseUrl }
        { name: 'Sap__MaxDeliveryCount', value: string(maxDeliveryCount) }
        { name: 'AzureWebJobs.SapMock.Disabled', value: string(environmentName == 'prod') }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
  }
}

// ---------- RBAC (least privilege for the managed identity) ----------
resource storageRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [roles.storageBlobDataOwner, roles.storageQueueDataContributor, roles.storageTableDataContributor]: {
    name: guid(storage.id, identity.id, role)
    scope: storage
    properties: {
      principalId: identity.properties.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    }
  }
]

resource queueReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus::queue.id, identity.id, roles.serviceBusDataReceiver)
  scope: serviceBus::queue
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataReceiver)
  }
}

resource metricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(insights.id, identity.id, roles.monitoringMetricsPublisher)
  scope: insights
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.monitoringMetricsPublisher)
  }
}

// ---------- Outputs ----------
@description('Create a Dataverse Application User with this Client ID and the "SupplyFlow Integration" security role.')
output managedIdentityClientId string = identity.properties.clientId

output functionAppName string = functionApp.name
output serviceBusNamespace string = 'sb://${serviceBus.name}.servicebus.windows.net/'
output queueName string = queueName

@description('Read the SAS key for the Dataverse service endpoint with: az servicebus queue authorization-rule keys list ...')
output dataverseSasRuleName string = serviceBus::queue::dataverseSend.name
