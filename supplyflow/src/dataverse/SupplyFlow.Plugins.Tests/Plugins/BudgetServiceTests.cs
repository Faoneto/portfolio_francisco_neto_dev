using System;
using System.ServiceModel;
using FakeItEasy;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SupplyFlow.Plugins.Core;
using SupplyFlow.Plugins.Model;
using SupplyFlow.Plugins.Services;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Plugins;

public class BudgetServiceTests
{
    private readonly Guid _costCenterId = Guid.NewGuid();

    [Fact]
    public void Retries_when_row_version_changed_and_reapplies_delta_on_fresh_value()
    {
        var service = A.Fake<IOrganizationService>();
        A.CallTo(() => service.Retrieve(Schema.CostCenter.EntityName, _costCenterId, A<ColumnSet>._))
            .ReturnsNextFromSequence(CostCenter(committed: 100m, rowVersion: "1"), CostCenter(committed: 400m, rowVersion: "2"));

        var calls = 0;
        UpdateRequest? lastRequest = null;
        A.CallTo(() => service.Execute(A<OrganizationRequest>._)).Invokes((OrganizationRequest request) =>
        {
            calls++;
            lastRequest = (UpdateRequest)request;
            if (calls == 1)
            {
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = DataverseErrorCodes.ConcurrencyVersionMismatch }, "version mismatch");
            }
        });

        var result = new BudgetService(service).AdjustCommitted(_costCenterId, 50m);

        Assert.Equal(2, calls);
        Assert.Equal(450m, result.CommittedAmount);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, lastRequest!.ConcurrencyBehavior);
        Assert.Equal("2", lastRequest.Target.RowVersion);
    }

    [Fact]
    public void Gives_up_after_max_attempts()
    {
        var service = A.Fake<IOrganizationService>();
        A.CallTo(() => service.Retrieve(A<string>._, A<Guid>._, A<ColumnSet>._)).ReturnsLazily(() => CostCenter(0m, "1"));
        A.CallTo(() => service.Execute(A<OrganizationRequest>._)).Throws(
            new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = DataverseErrorCodes.ConcurrencyVersionMismatch }, "version mismatch"));

        Assert.Throws<FaultException<OrganizationServiceFault>>(() => new BudgetService(service).AdjustCommitted(_costCenterId, 10m));
        A.CallTo(() => service.Execute(A<OrganizationRequest>._)).MustHaveHappened(BudgetService.MaxAttempts, Times.Exactly);
    }

    private Entity CostCenter(decimal committed, string rowVersion)
    {
        return new Entity(Schema.CostCenter.EntityName, _costCenterId)
        {
            RowVersion = rowVersion,
            [Schema.CostCenter.AnnualBudget] = new Money(1_000m),
            [Schema.CostCenter.CommittedAmount] = new Money(committed),
        };
    }
}
