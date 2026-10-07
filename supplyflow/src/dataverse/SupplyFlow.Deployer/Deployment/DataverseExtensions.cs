using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SupplyFlow.Deployer.Deployment;

internal static class DataverseExtensions
{
    public static Entity? FindFirst(this IOrganizationService service, string entity, string attribute, object value, params string[] columns)
    {
        var query = new QueryExpression(entity)
        {
            ColumnSet = columns.Length == 0 ? new ColumnSet(false) : new ColumnSet(columns),
            TopCount = 1,
        };
        query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
        return service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    /// <summary>Creates the record directly inside the target solution.</summary>
    public static Guid CreateInSolution(this IOrganizationService service, Entity entity, string solutionUniqueName)
    {
        var request = new OrganizationRequest("Create") { ["Target"] = entity };
        request.Parameters["SolutionUniqueName"] = solutionUniqueName;
        var response = service.Execute(request);
        return (Guid)response["id"];
    }

    /// <summary>Executes a Retrieve*Request and returns null when the component does not exist.</summary>
    public static TResponse? TryExecute<TResponse>(this IOrganizationService service, OrganizationRequest request)
        where TResponse : OrganizationResponse
    {
        try
        {
            return (TResponse)service.Execute(request);
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            return null;
        }
    }
}
