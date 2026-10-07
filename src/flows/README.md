# Power Automate – fluxos do SupplyFlow

Todos os fluxos são **solution-aware** (vivem na solução `SupplyFlow`), usam **connection references** e
**environment variables** — nada de conexão pessoal ou URL fixa. Isso é o que permite promover DEV → TEST → PROD
pelo pipeline sem editar fluxo em produção.

| # | Fluxo | Gatilho | Arquivo |
|---|-------|---------|---------|
| F1 | Aprovação de Requisição (multinível) | Dataverse – `fno_stage` muda para *Em aprovação* | [`SupplyFlow-AprovacaoRequisicao.json`](SupplyFlow-AprovacaoRequisicao.json) |
| F2 | Notificar requisitante – Pedido SAP criado | Dataverse – `fno_sappurchaseorder` preenchido | especificação abaixo |
| F3 | Escalonamento de aprovações pendentes | Recorrência (dias úteis, 08:45 BRT) | especificação abaixo |
| F4 | Enriquecer fornecedor via CNPJ (BrasilAPI) | Dataverse – `fno_cnpj` alterado em fornecedor | especificação abaixo |
| F5 | Notificar canal de Suprimentos (*child flow*) | Manual (chamado por outros fluxos) | [`SupplyFlow-NotificarCanalSuprimentos.json`](SupplyFlow-NotificarCanalSuprimentos.json) |
| F6 | Monitor de falhas de integração | Dataverse – `fno_integrationlog` criado com status *Falha* | especificação abaixo |

> Os JSON seguem o formato que o Power Automate grava dentro da solução (`Workflows/*.json`:
> `connectionReferences` + `definition`). Eles servem como **referência versionada e revisável**; a fonte
> de verdade passa a ser a solução exportada em `solution/` após o primeiro `export-solution` (ver
> [docs/alm.md](../../docs/alm.md)). O passo a passo de criação no portal está em
> [docs/setup-guide.md](../../docs/setup-guide.md#7-power-automate).

## Padrões aplicados (o que avaliar numa revisão de fluxo sênior)

- **Filtro no gatilho, não no fluxo**: `filteringattributes` + `filterexpression` (OData) no gatilho do
  Dataverse. O fluxo só roda quando a etapa vira *Em aprovação* — economiza execuções (limites de API do
  Power Platform) e evita loops.
- **Try / Catch com `Scope` + `runAfter`**: o escopo `Catch` roda em `Failed`/`TimedOut`, filtra
  `result('Try')` para achar a ação que falhou, avisa o time via *child flow* e termina a execução como
  **Failed** (para aparecer no monitoramento e em alertas — nunca "engolir" erro).
- **Lógica de negócio fica no servidor**: o fluxo **não** calcula alçada. O nível exigido vem de
  `fno_approvallevel`, calculado pelo plugin `RequisitionLifecyclePlugin` com a matriz de alçadas e o
  orçamento. O fluxo só orquestra pessoas.
- **Aprovação sequencial** com `Foreach` + `concurrency.repetitions = 1`, montada a partir do nível
  (`range()` + `Select`). Sem `Terminate` dentro de loop: o loop usa a variável `Rejeitada` para
  pular os níveis seguintes.
- **Segregação de funções** garantida pelo plugin (o requisitante não consegue aprovar a própria
  requisição nem via API). O histórico (quem aprovou, quando, comentário) é gravado em
  `fno_approvalnotes`.
- **Child flow reutilizável (F5)** para notificações: um único lugar para mudar o canal/formato do card.
- **Retry policy exponencial** em chamadas externas (Teams) e `limit.timeout` explícito na aprovação.
- **Links profundos** para o registro no app usando a variável de ambiente `fno_AppUrl`.

## Connection references

| Logical name | Conector |
|--------------|----------|
| `fno_sharedcommondataserviceforapps_supplyflow` | Microsoft Dataverse |
| `fno_sharedapprovals_supplyflow` | Approvals |
| `fno_sharedteams_supplyflow` | Microsoft Teams |
| `fno_supplyflowbrasilapicnpj_supplyflow` | SupplyFlow BrasilAPI CNPJ (custom connector) |

Os IDs de conexão de cada ambiente vão nos arquivos `deployment/settings/<ambiente>.json` usados pelo pipeline.

---

## F2 – Notificar requisitante: Pedido SAP criado

- **Gatilho**: *When a row is added, modified or deleted* — tabela `fno_purchaserequisition`, mudança =
  *Modified*, escopo *Organization*, colunas de filtro `fno_sappurchaseorder`,
  filtro `fno_stage eq 100000005`.
- **Ações**:
  1. *Get a row by ID* com `$expand=fno_RequesterId($select=fullname,internalemailaddress),fno_SupplierId($select=name)`.
  2. *Post adaptive card in a chat or channel* (Flow bot → requisitante) com número da RC, número do
     pedido SAP, fornecedor, valor e botão "Abrir requisição" (`fno_AppUrl`).
- **Por que existe**: fecha o ciclo com o usuário sem ele precisar abrir o app.

## F3 – Escalonamento de aprovações pendentes

- **Gatilho**: *Recurrence* – segunda a sexta, 08:45, fuso `E. South America Standard Time`.
- **Ações**:
  1. *List rows* em `fno_purchaserequisitions` com
     `$filter=fno_stage eq 100000001 and fno_submittedon lt @{addDays(utcNow(), mul(-1, int(parameters('Prazo de aprovação (dias) (fno_ApprovalTimeoutDays)'))))}`
     e `$select` mínimo (somente colunas usadas).
  2. *Apply to each* com **concorrência 5**: chama o child flow F5 com severidade `warning`
     ("RC-… aguarda aprovação há N dias").
  3. Se houver mais de 10 pendências, envia um único resumo em vez de 10 mensagens (evita ruído).
- **Variável de ambiente**: `fno_ApprovalTimeoutDays`.

## F4 – Enriquecer fornecedor via CNPJ (BrasilAPI)

- **Gatilho**: Dataverse, tabela `account`, *Added or Modified*, colunas `fno_cnpj`,
  filtro `fno_issupplier eq true and fno_cnpj ne null`.
- **Ações**:
  1. **Custom connector** *SupplyFlow BrasilAPI CNPJ* → `GetCompanyByCnpj` (o código C# do conector já
     normaliza o CNPJ e devolve `isActive`).
  2. *Update a row* (account): `fno_registrationstatus`, `fno_registrationcheckedon = utcNow()` e
     endereço (`address1_*`) **somente se estiver vazio** (expressão `coalesce`).
  3. Condição `isActive = false` → atualizar `fno_supplierstatus = Bloqueado (100000002)` e chamar F5
     com severidade `error`.
  4. Tratamento do 404 (CNPJ não encontrado) no `Catch`: grava a situação "NÃO ENCONTRADO" e notifica.
- **Observação**: o fluxo atualiza colunas que **não** estão no `filteringattributes` do próprio gatilho
  (`fno_cnpj`), então não entra em loop.

## F6 – Monitor de falhas de integração

- **Gatilho**: Dataverse, tabela `fno_integrationlog`, *Added*, filtro `fno_status eq 100000001`.
- **Ações**: *Get a row* da requisição relacionada → F5 com severidade `error`, mensagem
  `fno_errormessage` e link para o registro. Complementa os alertas do Application Insights
  (que cobrem a parte Azure).
