# solution/

Destino do **unpack** da solução `SupplyFlow` exportada do ambiente DEV (workflow
[`export-solution.yml`](../.github/workflows/export-solution.yml)). Depois do primeiro export esta pasta
contém `src/Other/Solution.xml`, `Entities/`, `Workflows/`, `PluginAssemblies/`, `WebResources/`,
`Controls/`, `AppModules/` etc., e passa a ser a fonte do pacote **gerenciado** promovido para TEST/PROD
(workflow [`release.yml`](../.github/workflows/release.yml)).

Por que a pasta começa vazia? O modelo de dados, plugins, Custom API e web resources são criados
**como código** pelo `SupplyFlow.Deployer` (ver [docs/alm.md](../docs/alm.md)); formulários, views,
app model-driven, BPF e fluxos são feitos no maker portal em DEV. O export captura **tudo** num único
artefato versionado — escrever XML de solução à mão não é prática recomendada.

Para gerar localmente:

```bash
pac solution export --name SupplyFlow --path out/SupplyFlow.zip
pac solution export --name SupplyFlow --path out/SupplyFlow_managed.zip --managed
pac solution unpack --zipfile out/SupplyFlow.zip --folder solution/src --packagetype Both
```
