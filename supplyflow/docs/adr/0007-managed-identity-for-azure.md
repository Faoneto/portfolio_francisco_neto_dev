# 0007 – Managed identity para Azure → Dataverse

## Contexto

A Function precisa ler/gravar no Dataverse e consumir o Service Bus. Client secrets expiram, vazam em
configurações e exigem rotação.

## Decisão

- **User-assigned managed identity** (permite saber o *client id* antes do deploy e reutilizar entre
  slots) registrada como **Application User** no Dataverse.
- `ServiceClient` com *token provider* (`DefaultAzureCredential`) — sem segredo.
- Storage, Service Bus e Application Insights também via Entra ID (`allowSharedKeyAccess: false`,
  `DisableLocalAuth: true`).
- Client secret só para desenvolvimento local (`local.settings.json`, fora do Git).

## Consequências

- ✅ Nenhum segredo da integração em produção.
- ✅ Permissões auditáveis via RBAC e papel de segurança do Dataverse.
- ⚠️ Desenvolvimento local requer `az login` ou app registration própria.
