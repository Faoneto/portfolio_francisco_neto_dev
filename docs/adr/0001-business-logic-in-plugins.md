# 0001 – Regras de negócio críticas em plugins; pessoas em Power Automate

## Contexto

As regras do processo (transições de etapa, alçada, orçamento, segregação de funções, CNPJ) precisam
valer para **todos os canais**: formulário, Kanban (PCF), Web API, importação, fluxos e a integração.
Power Automate roda de forma assíncrona e fora da transação; business rules só cobrem validações simples
de formulário (e o escopo *Entity* não faz consultas).

## Decisão

- Regras que **impedem** gravações ou **derivam** dados ficam em plugins síncronos (PreValidation /
  PreOperation / PostOperation), com lógica de domínio pura e testável (`Domain/`).
- Power Automate cuida da **orquestração de pessoas** (aprovações, lembretes, notificações) e
  integrações leves via conectores.
- JavaScript/PCF repetem algumas validações apenas para **feedback imediato**.

## Consequências

- ✅ Uma única fonte de verdade; impossível "furar" a regra por outro canal.
- ✅ Erros de validação aparecem para o usuário na hora (mesma transação).
- ⚠️ Mais código pro-code a manter → mitigado com 95+ testes e pipeline.
- ⚠️ Plugins síncronos precisam ser rápidos (limite de 2 min) → nenhuma chamada externa neles
  (ver [0005](0005-sap-integration-service-bus.md)).
