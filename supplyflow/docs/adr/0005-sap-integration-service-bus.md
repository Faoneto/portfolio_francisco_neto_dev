# 0005 – Integração SAP via service endpoint → Service Bus → Azure Function

## Contexto

Após a aprovação, a RC precisa virar pedido de compra no SAP MM. O SAP pode estar indisponível, lento
ou rejeitar o documento por regra de negócio; a aprovação no Dataverse não pode depender disso.

## Decisão

- Step **assíncrono** no *service endpoint* (contrato *Queue*, formato JSON, SAS *Send-only*) publica o
  `RemoteExecutionContext` no **Azure Service Bus**.
- **Azure Function** (.NET 8 isolated) consome com liquidação explícita: *complete* / *abandon*
  (transitório) / *dead-letter* (negócio ou última tentativa).
- Idempotência por estado (`fno_sappurchaseorder`), por cabeçalho (`Idempotency-Key`) e por
  *duplicate detection* da fila.
- O contexto é lido por um parser tolerante (System.Text.Json) em vez de `DataContractJsonSerializer`.

## Alternativas consideradas

- **Webhook direto para a Function (HTTP)**: mais simples, mas sem fila persistente → perda de eventos
  se a Function estiver fora.
- **Service Bus Sessions** por requisição: garantiria ordem por RC; desnecessário hoje porque cada RC
  gera um único evento relevante — fica como evolução.
- **Dual-write / Virtual tables**: não se aplica a SAP ECC/S4 neste cenário.

## Consequências

- ✅ Aprovação nunca bloqueia por causa do SAP; retry e DLQ nativos; escala independente.
- ✅ Código de integração 100% testável (processador sem tipos do Functions).
- ⚠️ Consistência eventual: a RC fica "Enviada ao SAP" por alguns segundos → comunicado no formulário.
- ⚠️ Mais peças de infraestrutura → Bicep + managed identity reduzem custo operacional.
