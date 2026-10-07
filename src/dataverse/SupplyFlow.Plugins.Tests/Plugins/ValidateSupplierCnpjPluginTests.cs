using FakeXrmEasy.Abstractions.Plugins.Enums;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Plugins.Supplier;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Plugins;

public class ValidateSupplierCnpjPluginTests : TestBase
{
    [Fact]
    public void Masked_cnpj_is_stored_normalized()
    {
        var target = new Entity(Schema.Account.EntityName)
        {
            [Schema.Account.Name] = "Novo Fornecedor",
            [Schema.Account.IsSupplier] = true,
            [Schema.Account.Cnpj] = "12.abc.345/01de-35",
        };

        Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(PluginContext("Create", ProcessingStepStage.Prevalidation, target));

        Assert.Equal("12ABC34501DE35", target[Schema.Account.Cnpj]);
    }

    [Fact]
    public void Invalid_cnpj_is_rejected()
    {
        var target = new Entity(Schema.Account.EntityName) { [Schema.Account.Cnpj] = "11.222.333/0001-80" };

        var ex = Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(PluginContext("Create", ProcessingStepStage.Prevalidation, target)));

        Assert.StartsWith("CNPJ inválido", ex.Message);
    }

    [Fact]
    public void Duplicate_cnpj_names_the_existing_account()
    {
        var existing = Supplier("Metalúrgica Belém S.A.");
        Context.Initialize(existing);
        var target = new Entity(Schema.Account.EntityName) { [Schema.Account.Cnpj] = "11.222.333/0001-81" };

        var ex = Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(PluginContext("Create", ProcessingStepStage.Prevalidation, target)));

        Assert.Contains("Metalúrgica Belém S.A.", ex.Message);
        Assert.Contains("11.222.333/0001-81", ex.Message);
    }

    [Fact]
    public void Updating_the_same_account_is_not_a_duplicate()
    {
        var existing = Supplier();
        Context.Initialize(existing);
        var target = new Entity(Schema.Account.EntityName, existing.Id) { [Schema.Account.Cnpj] = "11.222.333/0001-81" };
        var preImage = new Entity(Schema.Account.EntityName, existing.Id) { [Schema.Account.IsSupplier] = true };

        Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(
            PluginContext("Update", ProcessingStepStage.Prevalidation, target, preImage: preImage));

        Assert.Equal("11222333000181", target[Schema.Account.Cnpj]);
    }

    [Fact]
    public void Supplier_requires_cnpj()
    {
        var target = new Entity(Schema.Account.EntityName) { [Schema.Account.IsSupplier] = true };

        var ex = Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(PluginContext("Create", ProcessingStepStage.Prevalidation, target)));

        Assert.Contains("obrigatório", ex.Message);
    }

    [Fact]
    public void Customer_without_cnpj_is_allowed()
    {
        var target = new Entity(Schema.Account.EntityName) { [Schema.Account.Name] = "Cliente", [Schema.Account.IsSupplier] = false };

        Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(PluginContext("Create", ProcessingStepStage.Prevalidation, target));
    }

    [Fact]
    public void Wrong_registration_fails_fast()
    {
        var target = new Entity(Schema.Account.EntityName) { [Schema.Account.Cnpj] = "11222333000181" };

        var ex = Throws<InvalidPluginExecutionException>(() =>
            Context.ExecutePluginWith<ValidateSupplierCnpjPlugin>(PluginContext("Create", ProcessingStepStage.Postoperation, target)));

        Assert.StartsWith("Invalid registration", ex.Message);
    }
}
