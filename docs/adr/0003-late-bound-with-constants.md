# 0003 – Late-bound com constantes em vez de early-bound

## Contexto

Classes early-bound (`pac modelbuilder build`) dão tipagem forte, mas precisam ser regeneradas a cada
mudança de schema, aumentam o assembly de plugin e acoplam o build a um ambiente conectado.

## Decisão

Usar `Entity` late-bound com **constantes e enums** em `Model/Schema.cs`, e garantir a consistência com
testes de *drift* (`DefinitionConsistencyTests`) que comparam cada constante e cada enum com o
`schema.json` — inclusive do lado TypeScript (`test/schema.test.ts`).

## Consequências

- ✅ Build totalmente offline (CI sem conexão a ambiente).
- ✅ Typos de nome lógico são pegos pelo teste de drift, não em produção.
- ✅ Assembly pequeno.
- ⚠️ Menos IntelliSense em atributos → mitigado com extensões (`GetMoney`, `GetChoice<TEnum>`).
- Revisitar se o modelo crescer muito (dezenas de tabelas) — aí early-bound gerado no CI passa a compensar.
