# Roteiro de demonstração (5 minutos)

Use para gravar um vídeo para o portfólio/LinkedIn ou para apresentar ao vivo numa entrevista técnica.
Prepare antes: dados de exemplo (`Deployer seed`), 3 usuários (requisitante, gestor, diretor) e a
integração Azure ligada.

| Tempo | Cena | O que mostrar / dizer |
|-------|------|-----------------------|
| 0:00 | **Problema** | "Requisições de compra por e-mail/planilha, sem alçada clara, digitação dupla no SAP. O SupplyFlow resolve isso no Dataverse com integração SAP." (Uma frase sobre a experiência real com compras/SAP na Nestlé/Suzano.) |
| 0:30 | **Fornecedor** | Cadastrar fornecedor com CNPJ **alfanumérico** (PCF valida na hora). Tentar duplicar o CNPJ → mensagem do plugin com o nome do fornecedor existente. |
| 1:15 | **Requisição** | Criar RC, adicionar itens (preço vem do material, total recalculado). Tentar material não homologado → erro (N:N). |
| 2:00 | **Enviar para aprovação** | Botão do command bar → Custom API → diálogo "Aprovador necessário: Diretor". Mostrar campos bloqueados e a notificação no formulário. |
| 2:30 | **Segregação de funções** | Como requisitante, arrastar o card no **Kanban** para *Aprovada* → erro do servidor exibido no PCF. "A regra está no servidor, vale para qualquer canal." |
| 3:00 | **Aprovação** | Card de aprovação no Teams (tabela de itens, motivos da alçada). Gestor aprova, Diretor aprova. |
| 3:40 | **Integração** | RC muda para *Pedido criado* com nº SAP em segundos. Mostrar Application Insights (correlação) e o *Log de Integração*. Opcional: forçar fornecedor bloqueado → DLQ + botão "Reenviar ao SAP". |
| 4:20 | **Engenharia** | GitHub: pipeline verde, 150+ testes, `schema.json`/`plugin-registration.json`, ADRs. "Ambiente recriável com um comando; release gerenciado com aprovação." |
| 4:50 | **Fecho** | "Low-code onde acelera, pro-code onde garante — e tudo com ALM." |

## Perguntas que costumam vir (e onde está a resposta)

| Pergunta | Onde |
|----------|------|
| Por que plugin e não Power Automate para a alçada? | [ADR 0001](adr/0001-business-logic-in-plugins.md) |
| Por que não rollup column para o total? | [ADR 0002](adr/0002-header-totals-sync-plugin.md) |
| PreValidation × PreOperation × PostOperation — por que cada escolha? | [architecture.md §2](architecture.md#2-onde-mora-cada-regra-e-por-quê) e comentários `<remarks>` de cada plugin |
| Como evita loop infinito? | Filtering attributes + checagem de etapa + `ParentContext` ([`LocalPluginContext`](../src/dataverse/SupplyFlow.Plugins/Core/LocalPluginContext.cs)) |
| E se duas aprovações gastarem o mesmo orçamento ao mesmo tempo? | [`BudgetService`](../src/dataverse/SupplyFlow.Plugins/Services/BudgetService.cs) – concorrência otimista |
| E se o SAP cair? | [integration.md](integration.md#tabela-de-decisão-do-processador) |
| Como testa plugin sem ambiente? | FakeXrmEasy + pipeline simulado a partir do JSON de registro |
| Como promove para produção? | [alm.md](alm.md) |
