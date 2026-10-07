# Custom connector – SupplyFlow BrasilAPI CNPJ

Conector personalizado que consulta a situação cadastral de um CNPJ na Receita Federal através da
[BrasilAPI](https://brasilapi.com.br/docs#tag/CNPJ). Usado pelo fluxo **F4 – Enriquecer fornecedor via CNPJ**.

| Arquivo | Conteúdo |
|---------|----------|
| `apiDefinition.swagger.json` | OpenAPI 2.0 com **schema de resposta próprio** (inglês, estável) e metadados `x-ms-*` para o designer |
| `apiProperties.json` | Publisher, cor, *policy template* `setheader` (User-Agent) e habilitação do script na operação |
| `script.csx` | **Custom code** (C#): normaliza o CNPJ de entrada, mapeia o payload da BrasilAPI para o schema do conector e adiciona `isActive` |

## Por que um custom connector (e não um HTTP genérico no fluxo)?

- **Reuso e governança**: makers usam uma ação tipada ("Consultar CNPJ") em vez de montar URL e
  `Parse JSON` em cada fluxo; o conector pode ser classificado em políticas de DLP.
- **Contrato estável**: se a BrasilAPI mudar um campo, ajusta-se o `script.csx` e nenhum fluxo quebra.
- **ALM**: o conector vai dentro da solução e é promovido junto com os fluxos.

## Deploy

```bash
pac auth create --environment https://<sua-org-dev>.crm2.dynamics.com
pac connector create \
  --api-definition-file src/connectors/brasilapi-cnpj/apiDefinition.swagger.json \
  --api-properties-file src/connectors/brasilapi-cnpj/apiProperties.json \
  --script-file src/connectors/brasilapi-cnpj/script.csx \
  --solution-unique-name SupplyFlow

# atualizações posteriores
pac connector update --connector-id <id> --api-definition-file ... --api-properties-file ... --script-file ...
```

Depois do deploy, abra o conector no maker portal → **Test** e consulte um CNPJ real (ex.: `00.000.000/0001-91`)
para confirmar o mapeamento dos campos da BrasilAPI (`razao_social`, `descricao_situacao_cadastral`,
`cnae_fiscal`, `logradouro`, `municipio`, `uf`, `cep`, …).

## Limites conhecidos

- A BrasilAPI é um serviço comunitário sem SLA; em produção o mesmo contrato do conector pode apontar
  para um provedor pago (Serpro, por exemplo) trocando apenas `host`/autenticação e o `script.csx`.
- O código customizado de conectores tem limite de 2 minutos de execução e não aceita pacotes externos.
