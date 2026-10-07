# Integração Dataverse → Azure → SAP

## Fluxo

1. A requisição muda para **Aprovada** (pelo fluxo de aprovação).
2. O step **assíncrono** `SupplyFlow: requisition approved → Service Bus` (registrado no *service
   endpoint*, PostOperation, `filteringattributes = fno_stage`, com PostImage) publica o
   `RemoteExecutionContext` em JSON na fila `sap-purchase-requisitions`.
3. A Azure Function [`SapPurchaseOrderFunction`](../src/integration/SupplyFlow.Integration/Functions/SapPurchaseOrderFunction.cs)
   consome a mensagem com `AutoCompleteMessages = false` e delega ao
   [`RequisitionIntegrationProcessor`](../src/integration/SupplyFlow.Integration/Processing/RequisitionIntegrationProcessor.cs).
4. O processador lê a RC e os itens no Dataverse (managed identity), mapeia para o payload do
   `API_PURCHASEORDER_PROCESS_SRV`, chama o SAP e grava o número do pedido.

## Por que Service Bus (e não plugin com HTTP ou fluxo com HTTP)?

| Opção | Problema |
|-------|----------|
| Plugin síncrono chamando o SAP | Segura a transação do Dataverse; timeout de 2 min; SAP fora do ar = usuário não consegue aprovar |
| Plugin assíncrono chamando o SAP | Sem fila persistente/DLQ; retry limitado; código de integração dentro do sandbox |
| Power Automate com HTTP | Possível, mas retry/idempotência/DLQ ficam frágeis e o volume consome *requests* do Power Platform |
| **Service endpoint → Service Bus → Function** ✅ | Desacoplado, entrega garantida, retry com back-off, DLQ, escala independente, código testável |

Detalhes da decisão: [ADR 0005](adr/0005-sap-integration-service-bus.md).

## Tabela de decisão do processador

| Situação | Resultado | Efeito no Dataverse |
|----------|-----------|---------------------|
| Payload inválido | **Dead-letter** | — |
| PostImage com etapa ≠ Aprovada (ex.: eco das próprias atualizações da Function) | **Complete** (ignorado) | — |
| RC já tem `fno_sappurchaseorder` | **Complete** (idempotência) | Log *Ignorada* |
| RC cancelada entre a aprovação e o processamento | **Complete** (evento obsoleto) | — |
| Dados mestres incompletos (fornecedor sem código SAP…) | **Dead-letter** | Integração = *Falha* + log |
| SAP 2xx | **Complete** | Etapa = *Pedido criado*, nº do pedido, log *Sucesso* |
| SAP 4xx (erro de negócio, ex.: fornecedor bloqueado) | **Dead-letter** (não adianta tentar de novo) | Integração = *Falha* + log |
| SAP 5xx / 429 / timeout, tentativa < máx. | **Abandon** → Service Bus reentrega | Log da tentativa |
| SAP 5xx / 429 / timeout, última tentativa | **Dead-letter** | Integração = *Falha* + log |

Reprocessamento: botão **"Reenviar ao SAP"** (visível quando *Enviada ao SAP* + *Falha*) volta a etapa
para *Aprovada*, o que publica um novo evento. O orçamento **não** é comprometido de novo
(`fno_budgetcommitted`).

## Resiliência em camadas

| Camada | Mecanismo |
|--------|-----------|
| HTTP (por chamada) | `AddStandardResilienceHandler()`: retry exponencial com jitter, circuit breaker, timeout por tentativa e total |
| Mensagem | `maxDeliveryCount = 5`, `lockDuration = 2 min`, DLQ, *duplicate detection* (10 min) |
| Negócio | Checagem de estado atual + `Idempotency-Key` = número da RC |

## Observabilidade

- **Application Insights**: escopo de log com `RequisitionId`, `CorrelationId` (o mesmo do Dataverse),
  `MessageId` e `DeliveryCount` — é possível seguir uma RC do plugin até o SAP.
- **`fno_integrationlog`**: visão de negócio (payload enviado, duração, tentativa, erro) acessível no app.
- **Fluxo F6**: alerta no Teams para cada falha.

Consultas úteis (KQL):

```kusto
// Taxa de sucesso por hora
traces
| where message startswith "Message " and message has "->"
| extend disposition = extract(@"-> (\w+):", 1, message)
| summarize count() by disposition, bin(timestamp, 1h)
| render timechart

// Linha do tempo de uma requisição
union traces, exceptions
| where customDimensions.RequisitionId == "<guid>"
| project timestamp, severityLevel, message, outerMessage
| order by timestamp asc
```

## Rodando localmente

```bash
cd src/integration/SupplyFlow.Integration
cp local.settings.sample.json local.settings.json   # preencha Dataverse__* e o namespace do Service Bus
func start                                           # Azure Functions Core Tools v4
```

O endpoint `POST /api/sap-mock/purchaseorders` simula o SAP:

- fornecedor `0000099999` → **422** (fornecedor bloqueado → dead-letter);
- header `x-mock-fail: 503` → **503** (transitório → retry).

## Infraestrutura

[`infra/main.bicep`](../infra/main.bicep) cria Service Bus (fila + regra SAS *Send* para o Dataverse),
Function App **Flex Consumption** (.NET 8 isolated), **user-assigned managed identity** com RBAC de menor
privilégio, Storage sem chave compartilhada (`allowSharedKeyAccess: false`), Log Analytics e
Application Insights com autenticação Entra ID.

Depois do deploy:

1. Crie um **Application User** no Dataverse com o `managedIdentityClientId` (output do Bicep) e o papel
   *SupplyFlow Integração*.
2. Leia a chave SAS `dataverse-send` e rode `Deployer plugins` com `SUPPLYFLOW_SB_NAMESPACE` e
   `SUPPLYFLOW_SB_SASKEY` para registrar o service endpoint e o step.
3. Ligue a variável de ambiente `fno_SapIntegrationEnabled = yes`.
