# 0002 – Total da requisição por plugin síncrono

## Contexto

`fno_totalamount` define a alçada de aprovação e o comprometimento do orçamento. Alternativas:

| Opção | Avaliação |
|-------|-----------|
| Rollup column | Recalculada por job assíncrono (padrão: a cada 12 h) ou sob demanda — o total pode estar desatualizado no momento da submissão |
| Formula column (Power Fx) | Não agrega registros filhos |
| Fluxo no Power Automate | Assíncrono; janela em que o total está errado; consome execuções |
| **Plugin PostOperation síncrono** | Transacional e imediato |

## Decisão

`RequisitionLineTotalsPlugin` (PostOperation, síncrono) recalcula o total com **FetchXML aggregate**
(`sum`) a cada Create/Update/Delete de item, e `RequisitionSubmissionService` recalcula novamente na
submissão (defesa em profundidade).

## Consequências

- ✅ Total sempre correto dentro da transação; alçada nunca calculada sobre valor velho.
- ✅ Agregação no servidor (uma consulta) em vez de paginar itens.
- ⚠️ Cada alteração de item gera um Update no cabeçalho → aceitável para o volume (dezenas de itens).
- ⚠️ Exclusão em cascata da requisição dispara o plugin nos itens → tratado com
  `IsCascadeDeleteFrom(parentEntity)` usando o `ParentContext`.
