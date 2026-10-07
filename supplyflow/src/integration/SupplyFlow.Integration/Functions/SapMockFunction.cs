using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using SupplyFlow.Integration.Sap;

namespace SupplyFlow.Integration.Functions;

/// <summary>
/// Stand-in for SAP S/4HANA so the end-to-end flow can be demonstrated without an SAP system.
/// POST /api/sap-mock/purchaseorders
///   - supplier "0000099999" → 422 (supplier blocked: business error, goes to dead-letter)
///   - header x-mock-fail: 503 → 503 (transient error, Service Bus retries)
/// Anonymous on purpose (it only returns fake numbers); disabled in prod via "AzureWebJobs.SapMock.Disabled" = true (see infra/main.bicep).
/// </summary>
public sealed class SapMockFunction
{
    private static int _sequence = Random.Shared.Next(10_000, 90_000);

    [Function("SapMock")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sap-mock/purchaseorders")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValue("x-mock-fail", out var fail) && fail.ToString() == "503")
        {
            return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
        }

        var order = await request.ReadFromJsonAsync<SapPurchaseOrder>(cancellationToken);
        if (order is null || order.Items.Count == 0)
        {
            return new BadRequestObjectResult(new { error = new { code = "06/001", message = "Documento sem itens." } });
        }

        if (order.Supplier == "0000099999")
        {
            return new UnprocessableEntityObjectResult(new { error = new { code = "M8/082", message = $"Fornecedor {order.Supplier} bloqueado para compras." } });
        }

        // SAP standard PO number range "45…" (10 digits).
        var purchaseOrder = $"45000{Interlocked.Increment(ref _sequence):D5}";
        return new ObjectResult(new SapPurchaseOrderResponse { PurchaseOrder = purchaseOrder }) { StatusCode = StatusCodes.Status201Created };
    }
}
