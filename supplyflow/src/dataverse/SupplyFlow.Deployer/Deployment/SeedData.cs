using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SupplyFlow.Deployer.Deployment;

/// <summary>
/// Loads demo reference data using <see cref="UpsertRequest"/> with alternate keys – the same pattern an
/// SAP master-data integration would use (idempotent, no GUID bookkeeping).
/// </summary>
public sealed class SeedData
{
    private readonly IOrganizationService _service;
    private readonly ILogger _logger;

    public SeedData(IOrganizationService service, ILogger logger)
    {
        _service = service;
        _logger = logger;
    }

    public void Load()
    {
        var me = ((WhoAmIResponse)_service.Execute(new WhoAmIRequest())).UserId;
        var fiscalYear = DateTime.UtcNow.Year;

        var maintenance = UpsertCostCenter("CC1001", fiscalYear, "Manutenção Industrial", 250_000m, me);
        UpsertCostCenter("CC2001", fiscalYear, "Tecnologia da Informação", 120_000m, me);
        UpsertCostCenter("CC3001", fiscalYear, "Logística e Armazenagem", 80_000m, me);

        var bearing = UpsertMaterial("000000000010004711", "Rolamento rígido de esferas 6205-2RS", 100_000_000, "UN", 48.90m);
        var belt = UpsertMaterial("000000000010004712", "Correia transportadora 800mm", 100_000_000, "M", 315.00m);
        var pallet = UpsertMaterial("000000000020000105", "Palete PBR 1,00 x 1,20", 100_000_002, "UN", 62.50m);
        var stretch = UpsertMaterial("000000000020000231", "Filme stretch 500mm x 25µm", 100_000_002, "RL", 89.90m);
        UpsertMaterial("000000000030000017", "Notebook corporativo 16GB/512GB", 100_000_004, "UN", 6_890.00m);

        var supplierA = UpsertSupplier("11222333000181", "Rolamentos Amazônia Ltda", "0000100231", approved: true);
        var supplierB = UpsertSupplier("12ABC34501DE35", "EmbalaNorte Indústria S.A.", "0000100452", approved: true); // CNPJ alfanumérico (2026)
        UpsertSupplier("45723174000110", "Tech Pará Distribuidora", null, approved: false);

        Associate(supplierA, bearing, belt);
        Associate(supplierB, pallet, stretch);

        UpsertPolicy("Gestor – qualquer valor", 100_000_001, 0m, null);
        UpsertPolicy("Diretor – a partir de R$ 25 mil", 100_000_002, 25_000m, null);
        UpsertPolicy("CFO – a partir de R$ 150 mil", 100_000_003, 150_000m, null);
        UpsertPolicy("Diretor – TI a partir de R$ 5 mil", 100_000_002, 5_000m, 100_000_004);

        _logger.LogInformation("Seed data loaded. Cost center used in demos: {CostCenter}", maintenance);
    }

    private Guid UpsertCostCenter(string code, int fiscalYear, string name, decimal budget, Guid managerId)
    {
        var record = new Entity("fno_costcenter", new KeyAttributeCollection { { "fno_code", code }, { "fno_fiscalyear", fiscalYear } })
        {
            ["fno_name"] = $"{code} – {name}",
            ["fno_annualbudget"] = new Money(budget),
            ["fno_managerid"] = new EntityReference("systemuser", managerId),
        };
        return Upsert(record, $"cost center {code}");
    }

    private Guid UpsertMaterial(string code, string name, int group, string unit, decimal price)
    {
        var record = new Entity("fno_material", "fno_materialcode", code)
        {
            ["fno_name"] = name,
            ["fno_materialgroup"] = new OptionSetValue(group),
            ["fno_unitofmeasure"] = unit,
            ["fno_standardprice"] = new Money(price),
        };
        return Upsert(record, $"material {code}");
    }

    private Guid UpsertSupplier(string cnpj, string name, string? sapCode, bool approved)
    {
        var record = new Entity("account", "fno_cnpj", cnpj)
        {
            ["name"] = name,
            ["fno_issupplier"] = true,
            ["fno_supplierstatus"] = new OptionSetValue(approved ? 100_000_001 : 100_000_000),
            ["fno_sapvendorcode"] = sapCode,
            ["address1_city"] = "Belém",
            ["address1_stateorprovince"] = "PA",
            ["address1_country"] = "Brasil",
        };
        return Upsert(record, $"supplier {name}");
    }

    private void UpsertPolicy(string name, int level, decimal minAmount, int? group)
    {
        var existing = _service.FindFirst("fno_approvalpolicy", "fno_name", name, "fno_approvalpolicyid");
        var record = new Entity("fno_approvalpolicy")
        {
            ["fno_name"] = name,
            ["fno_level"] = new OptionSetValue(level),
            ["fno_minamount"] = new Money(minAmount),
            ["fno_materialgroup"] = group is null ? null : new OptionSetValue(group.Value),
        };

        if (existing is null)
        {
            _service.Create(record);
        }
        else
        {
            record.Id = existing.Id;
            _service.Update(record);
        }
    }

    private Guid Upsert(Entity record, string description)
    {
        var response = (UpsertResponse)_service.Execute(new UpsertRequest { Target = record });
        _logger.LogInformation("{Action} {Description}", response.RecordCreated ? "Created" : "Updated", description);
        return response.Target.Id;
    }

    private void Associate(Guid supplierId, params Guid[] materialIds)
    {
        var related = new EntityReferenceCollection();
        foreach (var materialId in materialIds)
        {
            var query = new QueryExpression("fno_account_fno_material") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("accountid", ConditionOperator.Equal, supplierId);
            query.Criteria.AddCondition("fno_materialid", ConditionOperator.Equal, materialId);
            if (_service.RetrieveMultiple(query).Entities.Count == 0)
            {
                related.Add(new EntityReference("fno_material", materialId));
            }
        }

        if (related.Count > 0)
        {
            _service.Associate("account", supplierId, new Relationship("fno_account_fno_material"), related);
        }
    }
}
