using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Domain;
using SupplyFlow.Plugins.Model;

namespace SupplyFlow.Plugins.Plugins.Supplier;

/// <summary>
/// Validates, normalizes and de-duplicates the supplier CNPJ.
/// </summary>
/// <remarks>
/// Registration: account | Create, Update (filtering: fno_cnpj, fno_issupplier) | PreValidation | Sync.
/// PreImage (Update): fno_cnpj, fno_issupplier.
/// <para>
/// PreValidation runs before the database transaction and before security checks of the main operation,
/// so an invalid CNPJ is rejected as early and as cheaply as possible.
/// The alternate key on fno_cnpj is the hard guarantee of uniqueness; this plugin gives the user a friendly
/// message naming the existing supplier. The duplicate lookup uses the SYSTEM service because the user may
/// not have read access to the account that already owns the CNPJ.
/// </para>
/// </remarks>
public sealed class ValidateSupplierCnpjPlugin : PluginBase
{
    public ValidateSupplierCnpjPlugin()
        : this(null, null)
    {
    }

    /// <summary>Constructor used by Dataverse when the step has unsecure/secure configuration.</summary>
    public ValidateSupplierCnpjPlugin(string? unsecureConfiguration, string? secureConfiguration)
        : base(unsecureConfiguration, secureConfiguration)
    {
    }

    protected override void ExecuteDataversePlugin(LocalPluginContext context)
    {
        context.EnsureRegistration(Schema.Account.EntityName, PluginStage.PreValidation, MessageNames.Create, MessageNames.Update);

        var target = context.GetTarget();
        if (!target.IsChanging(Schema.Account.Cnpj) && !target.IsChanging(Schema.Account.IsSupplier))
        {
            return;
        }

        var merged = context.MessageName == MessageNames.Update ? context.GetMergedTarget() : target;
        var isSupplier = merged.GetAttributeValue<bool?>(Schema.Account.IsSupplier) == true;
        var cnpj = Cnpj.Normalize(merged.GetAttributeValue<string>(Schema.Account.Cnpj));

        if (cnpj.Length == 0)
        {
            if (isSupplier)
            {
                throw new InvalidPluginExecutionException("O CNPJ é obrigatório para contas marcadas como fornecedor.");
            }

            return;
        }

        if (!Cnpj.IsValid(cnpj))
        {
            throw new InvalidPluginExecutionException(
                $"CNPJ inválido: {merged.GetAttributeValue<string>(Schema.Account.Cnpj)}. " +
                "Verifique os dígitos (o formato alfanumérico de 2026 também é aceito).");
        }

        // Persist the canonical (unmasked, upper-case) value so the alternate key and integrations match.
        if (target.IsChanging(Schema.Account.Cnpj))
        {
            target[Schema.Account.Cnpj] = cnpj;
        }

        var duplicate = FindDuplicate(context.SystemService, cnpj, target);
        if (duplicate is not null)
        {
            throw new InvalidPluginExecutionException(
                $"Já existe uma conta com o CNPJ {Cnpj.Format(cnpj)}: \"{duplicate.GetAttributeValue<string>(Schema.Account.Name)}\". " +
                "Utilize o cadastro existente.");
        }

        context.Trace($"CNPJ {Cnpj.Format(cnpj)} validated.");
    }

    private static Entity? FindDuplicate(IOrganizationService service, string cnpj, Entity target)
    {
        var query = new QueryExpression(Schema.Account.EntityName)
        {
            ColumnSet = new ColumnSet(Schema.Account.Name),
            TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression(Schema.Account.Cnpj, ConditionOperator.Equal, cnpj) } },
        };

        if (target.Id != System.Guid.Empty)
        {
            query.Criteria.AddCondition(Schema.Account.PrimaryId, ConditionOperator.NotEqual, target.Id);
        }

        return service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }
}
