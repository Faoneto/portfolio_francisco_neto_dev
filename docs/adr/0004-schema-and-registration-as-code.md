# 0004 – Schema e registro de plugins como código

## Contexto

Em muitos projetos o modelo de dados e os steps de plugin são criados à mão no portal / Plugin
Registration Tool: difícil revisar, fácil esquecer uma imagem ou um filtering attribute, impossível
recriar um ambiente do zero de forma confiável.

## Decisão

- `definitions/schema.json` e `definitions/plugin-registration.json` descrevem o **estado desejado**.
- `SupplyFlow.Deployer` aplica de forma **idempotente** (cria o que falta, atualiza steps/imagens,
  remove steps não declarados) usando o SDK de metadados (`CreateEntityRequest`,
  `CreateAttributeRequest`, `CreateOneToManyRequest`, `CreateManyToManyRequest`,
  `CreateEntityKeyRequest`, `CreateOptionSetRequest`, `RetrieveMetadataChangesRequest`).
- Os **mesmos arquivos** alimentam os testes: metadados do FakeXrmEasy são gerados do `schema.json` e
  a simulação de pipeline registra exatamente os steps do JSON.
- Componentes low-code (forms, views, app, fluxos) continuam no maker portal e entram no Git pelo
  export/unpack da solução — não se escreve XML de solução à mão.

## Consequências

- ✅ Ambiente DEV recriável com um comando; revisão de schema em PR.
- ✅ Um step registrado com imagem errada quebra o teste ponta a ponta antes do deploy.
- ✅ Validação offline (`Deployer validate`) no CI.
- ⚠️ Ferramenta própria a manter → escopo propositalmente limitado ao que o projeto usa.
