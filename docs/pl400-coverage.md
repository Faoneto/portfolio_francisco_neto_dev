# Cobertura do exame PL-400 (Microsoft Power Platform Developer)

Mapa entre as áreas avaliadas no exame e **onde cada habilidade aparece neste repositório**. Serve como
roteiro de estudo (abra o arquivo, entenda, altere, rode os testes) e como guia para quem avalia o portfólio.

> Os pesos e a redação oficial do *skills outline* mudam periodicamente — confira a versão atual em
> [Microsoft Learn](https://learn.microsoft.com/credentials/certifications/power-platform-developer-associate/).

## 1. Criar um design técnico

| Habilidade | Evidência |
|------------|-----------|
| Escolher entre plugin, fluxo, business rule, JS, PCF, Azure Function | [architecture.md §2](architecture.md#2-onde-mora-cada-regra-e-por-quê), [ADR 0001](adr/0001-business-logic-in-plugins.md) |
| Desenhar integrações e eventos | [ADR 0005](adr/0005-sap-integration-service-bus.md), [integration.md](integration.md) |
| Modelo de segurança (BU, papéis, hierarquia, FLS) | [security-model.md](security-model.md) |
| Limites de plataforma (timeouts, API limits, sandbox) | [architecture.md §5](architecture.md#5-requisitos-não-funcionais) |

## 2. Construir soluções Power Platform

| Habilidade | Evidência |
|------------|-----------|
| Publisher, prefixo, solução | [`schema.json`](../src/dataverse/definitions/schema.json) (`publisher`, `solution`) |
| Tabelas, colunas, choices globais, relacionamentos (1:N, N:N, cascata) | [`schema.json`](../src/dataverse/definitions/schema.json), [`MetadataBuilder.cs`](../src/dataverse/SupplyFlow.Deployer/Metadata/MetadataBuilder.cs) |
| Chaves alternativas (inclusive compostas) e upsert | [data-model.md](data-model.md#chaves-alternativas-alternate-keys), [`SeedData.cs`](../src/dataverse/SupplyFlow.Deployer/Deployment/SeedData.cs) |
| Autonumber | `fno_name` da requisição (`RC-{DATETIMEUTC:yyyyMM}-{SEQNUM:5}`) |
| Environment variables e connection references | [`schema.json`](../src/dataverse/definitions/schema.json), [`EnvironmentVariableService.cs`](../src/dataverse/SupplyFlow.Plugins/Services/EnvironmentVariableService.cs), [`deployment/settings`](../deployment/settings) |
| ALM: managed/unmanaged, pack/unpack, pipelines, deployment settings | [alm.md](alm.md), [`.github/workflows`](../.github/workflows) |

## 3. Implementar melhorias em Power Apps

| Habilidade | Evidência |
|------------|-----------|
| Comandos com Power Fx (visibilidade) e JavaScript | [setup-guide §6.6](setup-guide.md#66-comandos-modern-commanding), [`requisition.commands.ts`](../src/webresources/src/commands/requisition.commands.ts) |
| Business process flow e business rules | [setup-guide §6.4–6.5](setup-guide.md#64-business-process-flow--ciclo-da-requisição) |
| Diagnóstico (Monitor, trace de plugin) | `PluginBase` grava trace com correlação/usuário; [setup-guide §3](setup-guide.md#3-schema-plugins-custom-api-e-web-resources-como-código) |

## 4. Estender a experiência do usuário

| Habilidade | Evidência |
|------------|-----------|
| Client API: `formContext`, eventos OnLoad/OnSave/OnChange, `preventDefault` | [`requisition.form.ts`](../src/webresources/src/forms/requisition.form.ts) |
| Notificações de formulário e de controle, required level, visibilidade | idem + [`supplier.form.ts`](../src/webresources/src/forms/supplier.form.ts) |
| Filtro de lookup (`addPreSearch` / `addCustomFilter`) | `onLoad` do formulário de requisição |
| `Xrm.WebApi.online.execute` chamando Custom API ligada | [`requisition.commands.ts`](../src/webresources/src/commands/requisition.commands.ts) |
| Diálogos e indicador de progresso (`Xrm.Navigation`, `Xrm.Utility`) | idem |
| PCF – field control | [`CnpjInput`](../src/pcf/CnpjInput) |
| PCF – dataset control, `context.webAPI`, `utils.getEntityMetadata`, paging | [`RequisitionKanban`](../src/pcf/RequisitionKanban) |
| PCF – virtual controls (React/Fluent da plataforma), `feature-usage` | `ControlManifest.Input.xml` |

## 5. Estender a plataforma

| Habilidade | Evidência |
|------------|-----------|
| Pipeline de eventos: PreValidation, PreOperation, PostOperation, sync/async | [`plugin-registration.json`](../src/dataverse/definitions/plugin-registration.json) + plugins em [`Plugins/`](../src/dataverse/SupplyFlow.Plugins/Plugins) |
| Pre/Post images, filtering attributes, `Target`, `ParentContext`, `Depth` | [`LocalPluginContext.cs`](../src/dataverse/SupplyFlow.Plugins/Core/LocalPluginContext.cs) |
| `IOrganizationService` (user × SYSTEM), `QueryExpression`, `FetchXML` aggregate, link-entity | [`RequisitionRepository.cs`](../src/dataverse/SupplyFlow.Plugins/Services/RequisitionRepository.cs) |
| Tratamento de exceções (`InvalidPluginExecutionException`, `FaultException<OrganizationServiceFault>`) | [`PluginBase.cs`](../src/dataverse/SupplyFlow.Plugins/Core/PluginBase.cs) |
| Concorrência otimista (`RowVersion`, `ConcurrencyBehavior`) | [`BudgetService.cs`](../src/dataverse/SupplyFlow.Plugins/Services/BudgetService.cs) |
| Telemetria em plugin (`ILogger` → Application Insights) | `LocalPluginContext.Logger` |
| **Custom API** (definição, parâmetros, plugin, chamada via Web API) | [`SubmitRequisitionApi.cs`](../src/dataverse/SupplyFlow.Plugins/CustomApis/SubmitRequisitionApi.cs), `customApis` no JSON |
| Registro de plugin (assembly assinado, steps, imagens) via SDK | [`PluginRegistrar.cs`](../src/dataverse/SupplyFlow.Deployer/Deployment/PluginRegistrar.cs) |
| Testes de plugin | [`SupplyFlow.Plugins.Tests`](../src/dataverse/SupplyFlow.Plugins.Tests) |
| Custom connector: OpenAPI, policy template, **custom code** | [`src/connectors/brasilapi-cnpj`](../src/connectors/brasilapi-cnpj) |
| Power Automate avançado: gatilho filtrado, Try/Catch, child flow, expressões | [`src/flows`](../src/flows) |

## 6. Desenvolver integrações

| Habilidade | Evidência |
|------------|-----------|
| Publicar eventos do Dataverse: **service endpoint** (Service Bus) | `serviceEndpoints` no JSON, [`PluginRegistrar.UpsertServiceEndpoint`](../src/dataverse/SupplyFlow.Deployer/Deployment/PluginRegistrar.cs) |
| Consumir `RemoteExecutionContext` numa Azure Function | [`RemoteContextParser.cs`](../src/integration/SupplyFlow.Integration/Dataverse/RemoteContextParser.cs) |
| Azure Functions (Service Bus trigger, HTTP trigger) | [`Functions/`](../src/integration/SupplyFlow.Integration/Functions) |
| Autenticação S2S (Application User, managed identity, `ServiceClient`) | [`Program.cs`](../src/integration/SupplyFlow.Integration/Program.cs), [ADR 0007](adr/0007-managed-identity-for-azure.md) |
| Sincronização de dados: alternate keys + `UpsertRequest` | [`SeedData.cs`](../src/dataverse/SupplyFlow.Deployer/Deployment/SeedData.cs) |
| Resiliência: retry, DLQ, idempotência | [integration.md](integration.md#tabela-de-decisão-do-processador) |

## Lacunas conscientes (para estudar à parte)

- Webhooks com autenticação por *HttpHeader* (alternativa ao Service Bus — compare em [ADR 0005](adr/0005-sap-integration-service-bus.md)).
- Canvas apps / component framework em canvas, Power Fx avançado em canvas.
- Virtual tables e Dataverse *elastic tables*.
- Azure DevOps (*Power Platform Build Tools*) — os passos são os mesmos dos workflows do GitHub.
