using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SupplyFlow.Integration.Sap;

public interface ISapClient
{
    Task<SapPurchaseOrderResponse> CreatePurchaseOrderAsync(SapPurchaseOrder order, string correlationId, CancellationToken cancellationToken);
}

/// <summary>
/// Typed HttpClient. Retries, timeout and circuit breaker are configured in Program.cs
/// (Microsoft.Extensions.Http.Resilience standard pipeline) – this class only maps HTTP to domain outcomes.
/// </summary>
public sealed class SapClient : ISapClient
{
    private readonly HttpClient _http;

    public SapClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<SapPurchaseOrderResponse> CreatePurchaseOrderAsync(SapPurchaseOrder order, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "purchaseorders")
        {
            Content = JsonContent.Create(order),
        };
        request.Headers.Add("x-correlation-id", correlationId);
        // Same requisition number => same key: SAP (or the API gateway in front of it) can deduplicate retries.
        request.Headers.Add("Idempotency-Key", order.CorrespncExternalReference);

        using var response = await _http.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests && response.StatusCode != HttpStatusCode.RequestTimeout)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new SapBusinessException($"SAP rejeitou o pedido ({(int)response.StatusCode}): {body}");
        }

        response.EnsureSuccessStatusCode(); // 5xx/429 -> HttpRequestException -> transient
        return await response.Content.ReadFromJsonAsync<SapPurchaseOrderResponse>(cancellationToken)
            ?? throw new HttpRequestException("Resposta vazia do SAP.");
    }
}
