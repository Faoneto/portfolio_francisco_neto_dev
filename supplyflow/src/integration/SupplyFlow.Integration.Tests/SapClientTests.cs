using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SupplyFlow.Integration.Sap;
using Xunit;

namespace SupplyFlow.Integration.Tests;

public class SapClientTests
{
    private static readonly SapPurchaseOrder Order = new() { Supplier = "0000100231", CorrespncExternalReference = "RC-202610-00042" };

    [Fact]
    public async Task Sends_correlation_and_idempotency_headers()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """{"PurchaseOrder":"4500012345"}""");
        var client = new SapClient(new HttpClient(handler) { BaseAddress = new Uri("https://sap.example/api/") });

        var response = await client.CreatePurchaseOrderAsync(Order, "corr-1", CancellationToken.None);

        Assert.Equal("4500012345", response.PurchaseOrder);
        Assert.Equal("https://sap.example/api/purchaseorders", handler.Request!.RequestUri!.ToString());
        Assert.Equal("corr-1", handler.Request.Headers.GetValues("x-correlation-id").Single());
        Assert.Equal("RC-202610-00042", handler.Request.Headers.GetValues("Idempotency-Key").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Client_errors_are_business_failures(HttpStatusCode status)
    {
        var client = new SapClient(new HttpClient(new StubHandler(status, "Fornecedor bloqueado")) { BaseAddress = new Uri("https://sap.example/") });

        var ex = await Assert.ThrowsAsync<SapBusinessException>(() => client.CreatePurchaseOrderAsync(Order, "c", CancellationToken.None));
        Assert.Contains("Fornecedor bloqueado", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Server_errors_and_throttling_are_transient(HttpStatusCode status)
    {
        var client = new SapClient(new HttpClient(new StubHandler(status, string.Empty)) { BaseAddress = new Uri("https://sap.example/") });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreatePurchaseOrderAsync(Order, "c", CancellationToken.None));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
