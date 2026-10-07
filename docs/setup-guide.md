# Guia de montagem – do zero a uma demo completa

Tempo estimado: **1 dia** para a parte de código + **1 a 2 dias** para a camada low-code (formulários,
app, fluxos). Tudo funciona no **Power Apps Developer Plan** (gratuito) + uma assinatura Azure
(o custo da integração em Flex Consumption + Service Bus Standard é de poucos dólares/mês em demo).

## 0. Pré-requisitos

| Ferramenta | Para quê |
|------------|----------|
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | Plugins, Deployer, Azure Function |
| [Node.js 20](https://nodejs.org) | Web resources e PCF |
| [Power Platform CLI (`pac`)](https://learn.microsoft.com/power-platform/developer/cli/introduction) | PCF, conector, solução |
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) + Bicep | Infraestrutura |
| [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | Rodar a Function localmente |
| VS Code ou Visual Studio 2022 | — |

## 1. Ambiente Dataverse

1. Crie um ambiente **Developer** em <https://admin.powerplatform.microsoft.com> (com banco Dataverse,
   idioma Português (Brasil), moeda BRL).
2. Anote a URL (ex.: `https://supplyflow-dev.crm2.dynamics.com`).

## 2. Build e testes locais

```bash
dotnet build SupplyFlow.sln -c Release
dotnet test SupplyFlow.sln -c Release          # 95+ testes de plugin/deployer + 20 da integração

cd src/webresources && npm ci && npm test && npm run build && cd ../..
cd src/pcf && npm ci && npm run build && npm test && cd ../..
```

## 3. Schema, plugins, Custom API e web resources (como código)

Login interativo (abre o navegador):

```bash
cd src/dataverse
dotnet run --project SupplyFlow.Deployer -- schema       --url https://supplyflow-dev.crm2.dynamics.com
dotnet run --project SupplyFlow.Deployer -- plugins      --url https://supplyflow-dev.crm2.dynamics.com \
  --assembly SupplyFlow.Plugins/bin/Release/net462/SupplyFlow.Plugins.dll
dotnet run --project SupplyFlow.Deployer -- webresources --url https://supplyflow-dev.crm2.dynamics.com \
  --folder ../webresources/dist
dotnet run --project SupplyFlow.Deployer -- seed         --url https://supplyflow-dev.crm2.dynamics.com
```

Confira em **make.powerapps.com → Soluções → SupplyFlow**: tabelas, chaves alternativas (status
*Active* após alguns minutos), variáveis de ambiente, assembly, steps e a Custom API.

> Dica de estudo: abra o **Plugin Registration Tool** (`pac tool prt`) e compare o que o Deployer
> registrou com o `plugin-registration.json`. Use o **Plugin Profiler** para depurar um step.

## 4. PCF

```bash
cd src/pcf
pac auth create --environment https://supplyflow-dev.crm2.dynamics.com
pac pcf push --publisher-prefix fno --solution-unique-name SupplyFlow
```

## 5. Segurança

1. Crie as BUs *Unidade Norte*, *Unidade Sudeste* e *Centro de Serviços Compartilhados*.
2. Crie os papéis da tabela em [security-model.md](security-model.md) (copie o *Basic User* e ajuste) e
   adicione-os à solução.
3. Crie o perfil de **field security** "SupplyFlow – Orçamento" (coluna `fno_annualbudget`).
4. Habilite **Hierarchy security → Manager hierarchy** (profundidade 3) em *Configurações → Usuários + permissões*.
5. Crie pelo menos 3 usuários de teste (requisitante, gestor, diretor) para demonstrar a segregação de
   funções — o Developer Plan permite adicionar usuários do seu tenant.

## 6. Model-driven app (maker portal)

### 6.1 Formulário principal – Requisição de Compra

| Área | Conteúdo |
|------|----------|
| Cabeçalho | Etapa, Valor total, Nível de aprovação, Proprietário |
| Aba **Geral** | Fornecedor, Centro de custo, Data de necessidade, Prioridade, Justificativa, Motivo do cancelamento |
| Aba **Itens** | Subgrid de *Itens da Requisição* (**editable grid**: Material, Quantidade, Preço unitário, Total) |
| Aba **Aprovação** | Motivos da alçada, Acima do orçamento, Enviada em, Aprovada em, **Timeline** |
| Aba **Integração SAP** | Pedido SAP, Status da integração, Mensagem, subgrid de *Logs de Integração* |

- **Eventos** (Propriedades do formulário → Bibliotecas → `fno_/js/requisition.form.js`):
  - `OnLoad` → `SupplyFlow.RequisitionForm.onLoad` ✅ *Passar contexto de execução*
  - `OnSave` → `SupplyFlow.RequisitionForm.onSave` ✅ *Passar contexto de execução*
- Campos calculados (Valor total, Nível, Acima do orçamento) como **somente leitura**.

### 6.2 Formulário – Conta (Fornecedor)

- Nova aba `tab_supplier` com seção `section_supplier` (os nomes são usados pelo script): CNPJ, É
  fornecedor, Status de homologação, Código SAP, Situação cadastral, Verificada em; subgrid
  **Materiais Homologados** (N:N).
- No campo **CNPJ**, adicione o componente **CNPJ (SupplyFlow)** (PCF `CnpjInput`).
- `OnLoad` → `SupplyFlow.SupplierForm.onLoad` (biblioteca `fno_/js/supplier.form.js`).

### 6.3 Views

| Tabela | View | Filtro |
|--------|------|--------|
| Requisição | Minhas requisições abertas | Requisitante = usuário atual, etapa ∉ {Pedido criado, Cancelada} |
| Requisição | **Kanban** | Etapa ≠ Cancelada — usar o componente **Kanban de Requisições** na view |
| Requisição | Acima do orçamento | `fno_isoverbudget = Sim` |
| Requisição | Falhas de integração | Status da integração = Falha |
| Conta | Fornecedores homologados | `fno_issupplier = Sim` e status = Homologado |

### 6.4 Business Process Flow – "Ciclo da Requisição"

| Estágio | Etapas (campos) |
|---------|-----------------|
| Preparação | Fornecedor, Centro de custo, Data de necessidade, Justificativa |
| Aprovação | Nível de aprovação (somente leitura), Acima do orçamento |
| Compras | Pedido SAP |

> O BPF guia o usuário; a regra de verdade está no plugin ([ADR 0006](adr/0006-custom-stage-state-machine.md)).

### 6.5 Business rules

- **Item da requisição**: se *Quantidade* > 1000 → mensagem de recomendação "Confirme a unidade de medida".
- **Requisição**: se *Prioridade* = Urgente → *Data de necessidade* obrigatória e recomendação
  "Urgências exigem justificativa detalhada".
- **Centro de custo**: bloquear *Valor comprometido* e *Saldo disponível* no formulário.

### 6.6 Comandos (modern commanding)

No designer de app → editar a barra de comandos do **formulário principal** de Requisição:

| Botão | Ação | Visibilidade |
|-------|------|--------------|
| Enviar para aprovação | JavaScript – biblioteca `fno_/js/requisition.commands.js`, função `SupplyFlow.RequisitionCommands.submit`, parâmetro **PrimaryControl** | Power Fx: `Self.Selected.Item.Etapa in [ 'Etapa (Requisições de Compra)'.Rascunho, 'Etapa (Requisições de Compra)'.Rejeitada ]` |
| Reenviar ao SAP | `SupplyFlow.RequisitionCommands.resendToSap` | Power Fx: etapa = *Enviada ao SAP* e status da integração = *Falha* |

> Alternativa clássica: Ribbon Workbench com *enable rules* chamando `isSubmitVisible`/`isResendVisible`.

### 6.7 App e dashboard

- App **SupplyFlow – Suprimentos** com áreas: *Compras* (Requisições), *Cadastros* (Fornecedores,
  Materiais, Centros de custo, Alçadas), *Integração* (Logs).
- Dashboard com gráficos: valor por etapa, valor por centro de custo, RCs por fornecedor.
- 💡 **Diferencial para o seu perfil**: um relatório **Power BI** (conector Dataverse) com lead time de
  aprovação, consumo de orçamento e taxa de falha da integração, embutido no dashboard do app.

## 7. Power Automate

1. Crie as **connection references** listadas em [src/flows/README.md](../src/flows/README.md) dentro da solução.
2. Crie o **child flow F5** primeiro (fluxos filhos precisam estar na solução e ter "Run only users"
   configurado para usar as connection references).
3. Crie o **F1 (aprovação)** seguindo o JSON de referência — preste atenção em:
   *Filter rows* `fno_stage eq 100000001`, *Select columns* `fno_stage`, *Run as* *Flow owner*,
   `Apply to each` com **Concurrency = 1**, escopos `Try`/`Catch` e *Configure run after*.
4. Crie F2, F3, F4 e F6 pelas especificações.
5. Custom connector:

   ```bash
   pac connector create --api-definition-file src/connectors/brasilapi-cnpj/apiDefinition.swagger.json \
     --api-properties-file src/connectors/brasilapi-cnpj/apiProperties.json \
     --script-file src/connectors/brasilapi-cnpj/script.csx --solution-unique-name SupplyFlow
   ```

## 8. Integração Azure

```bash
az login
az group create -n rg-supplyflow-dev -l brazilsouth
az deployment group create -g rg-supplyflow-dev -f infra/main.bicep \
  -p environmentName=dev dataverseUrl=https://supplyflow-dev.crm2.dynamics.com

# publicar a Function
cd src/integration/SupplyFlow.Integration
func azure functionapp publish <functionAppName-do-output>
```

1. **Application User** no Dataverse: *Admin center → Ambiente → Usuários S2S → Novo usuário de
   aplicativo* com o `managedIdentityClientId` do output e o papel *SupplyFlow Integração*.
2. Registre o service endpoint + step:

   ```bash
   export SUPPLYFLOW_SB_NAMESPACE="sb://<namespace>.servicebus.windows.net/"
   export SUPPLYFLOW_SB_SASKEY=$(az servicebus queue authorization-rule keys list -g rg-supplyflow-dev \
     --namespace-name <namespace> --queue-name sap-purchase-requisitions --name dataverse-send --query primaryKey -o tsv)
   dotnet run --project src/dataverse/SupplyFlow.Deployer -- plugins --url <env-url> --assembly <dll>
   ```

3. Altere a variável de ambiente `fno_SapIntegrationEnabled` para **Sim**.

## 9. GitHub Actions

Configure environments `dev`, `test`, `prod` no repositório com os secrets/variáveis de
[alm.md](alm.md#segredos-e-variáveis-do-github). Depois:

1. **Deploy to DEV** (manual) → aplica o código.
2. **Export solution from DEV** (manual, com versão) → abre PR com `solution/src`.
3. Merge + tag `v1.0.0` → **Release** para TEST e PROD.

## 10. Checklist de evidências para o portfólio

Salve em `docs/images/` e referencie no README:

- [ ] Formulário da requisição com itens, cabeçalho e notificação "Aguardando aprovação: Diretor"
- [ ] Erro amigável do plugin (ex.: "Segregação de funções…" ou "Transição inválida…")
- [ ] Kanban (PCF) com totais por coluna e o erro ao arrastar para uma etapa inválida
- [ ] Campo CNPJ (PCF) validando um CNPJ alfanumérico
- [ ] Card de aprovação no Teams/Outlook com a tabela de itens
- [ ] Execução do fluxo com Try/Catch no histórico
- [ ] Mensagem no Service Bus / execução da Function no Application Insights
- [ ] RC com etapa *Pedido criado* e número SAP + registro em *Logs de Integração*
- [ ] Pipeline do GitHub Actions verde
- [ ] (Opcional) Vídeo de 3–5 min seguindo [demo-script.md](demo-script.md)
