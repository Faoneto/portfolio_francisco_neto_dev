# 0006 – Etapa customizada (`fno_stage`) com máquina de estados em plugin

## Contexto

O Dataverse oferece `statecode`/`statuscode` com *status reason transitions*. Porém: a requisição tem 7
etapas, regras diferentes por transição (validações, SoD, número SAP obrigatório) e precisa continuar
**ativa** (editável por plugin/integração) em quase todas as etapas.

## Decisão

- Choice local `fno_stage` + `RequisitionStateMachine` (tabela de transições) aplicada pelo
  `RequisitionLifecyclePlugin`.
- `statecode` permanece *Ativo*; a desativação de RCs concluídas/canceladas pode ser feita por job de
  arquivamento (fora do escopo).
- O BPF "Ciclo da Requisição" é **visual** (guia o usuário), não a fonte da regra.

## Consequências

- ✅ Transições testadas unitariamente (tabela) e regras ricas por transição.
- ✅ Mensagens de erro em português claro para o usuário.
- ⚠️ É preciso documentar a tabela (ver diagrama no código e em `architecture.md`).
