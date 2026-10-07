using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SupplyFlow.Integration.Dataverse;
using SupplyFlow.Integration.Processing;
using SupplyFlow.Integration.Sap;
using Xunit;

namespace SupplyFlow.Integration.Tests;

public class RequisitionIntegrationProcessorTests
{
    private static readonly string ApprovedEvent =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "requisition-approved.json"));

    private readonly FakeGateway _gateway = new();
    private readonly FakeSap _sap = new();

    [Fact]
    public async Task Creates_purchase_order_and_completes()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition();

        var result = await Process();

        Assert.Equal(MessageDisposition.Complete, result.Disposition);
        Assert.Equal(new[] { "SentToSap", "PurchaseOrderCreated:4500012345" }, _gateway.Calls);
        Assert.Equal(IntegrationLogStatus.Success, Assert.Single(_gateway.Logs).Status);
        Assert.Equal("9b8f6c1e-2d3a-4b5c-9d8e-7f6a5b4c3d2e", _sap.LastCorrelationId);
    }

    [Fact]
    public async Task Ignores_events_that_are_not_approvals()
    {
        var result = await Process(ApprovedEvent.Replace("100000002", "100000004")); // SentToSap echo from our own update

        Assert.Equal(MessageDisposition.Complete, result.Disposition);
        Assert.Empty(_gateway.Calls);
        Assert.Equal(0, _sap.Calls);
    }

    [Fact]
    public async Task Is_idempotent_when_purchase_order_already_exists()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition() with { SapPurchaseOrder = "4500000001" };

        var result = await Process();

        Assert.Equal(MessageDisposition.Complete, result.Disposition);
        Assert.Equal(0, _sap.Calls);
        Assert.Equal(IntegrationLogStatus.Skipped, Assert.Single(_gateway.Logs).Status);
    }

    [Fact]
    public async Task Ignores_stale_events_for_cancelled_requisitions()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition() with { Stage = 100_000_006 };

        var result = await Process();

        Assert.Equal(MessageDisposition.Complete, result.Disposition);
        Assert.Equal(0, _sap.Calls);
    }

    [Fact]
    public async Task Retries_redelivered_message_while_requisition_is_already_sent()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition() with { Stage = RequisitionIntegrationProcessor.StageSentToSap };

        var result = await Process(deliveryCount: 2);

        Assert.Equal(MessageDisposition.Complete, result.Disposition);
        Assert.Equal(1, _sap.Calls);
    }

    [Fact]
    public async Task Business_rejection_goes_to_dead_letter_and_flags_requisition()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition();
        _sap.Failure = new SapBusinessException("SAP rejeitou o pedido (422): Fornecedor bloqueado");

        var result = await Process();

        Assert.Equal(MessageDisposition.DeadLetter, result.Disposition);
        Assert.Contains("Failed:SAP rejeitou", _gateway.Calls.Last());
    }

    [Fact]
    public async Task Transient_failure_is_retried_until_the_last_delivery()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition();
        _sap.Failure = new HttpRequestException("503 Service Unavailable");

        var first = await Process(deliveryCount: 1);
        Assert.Equal(MessageDisposition.Retry, first.Disposition);
        Assert.DoesNotContain(_gateway.Calls, c => c.StartsWith("Failed"));

        var last = await Process(deliveryCount: 5);
        Assert.Equal(MessageDisposition.DeadLetter, last.Disposition);
        Assert.StartsWith("Failed:Falha temporária", _gateway.Calls.Last());
    }

    [Fact]
    public async Task Incomplete_master_data_is_dead_lettered_without_calling_sap()
    {
        _gateway.Requisition = SapPurchaseOrderMapperTests.Requisition(vendor: null);

        var result = await Process();

        Assert.Equal(MessageDisposition.DeadLetter, result.Disposition);
        Assert.Equal(0, _sap.Calls);
        Assert.StartsWith("Failed:Dados incompletos", Assert.Single(_gateway.Calls));
    }

    [Fact]
    public async Task Invalid_payload_is_dead_lettered()
    {
        var result = await Process("not json");

        Assert.Equal(MessageDisposition.DeadLetter, result.Disposition);
    }

    private Task<ProcessingResult> Process(string? body = null, int deliveryCount = 1)
    {
        var processor = new RequisitionIntegrationProcessor(
            _gateway, _sap, Options.Create(new SapOptions { MaxDeliveryCount = 5 }), NullLogger<RequisitionIntegrationProcessor>.Instance);
        return processor.ProcessAsync(body ?? ApprovedEvent, "msg-1", deliveryCount, CancellationToken.None);
    }

    private sealed class FakeGateway : IRequisitionGateway
    {
        public RequisitionSnapshot? Requisition { get; set; }

        public List<string> Calls { get; } = new();

        public List<IntegrationLogEntry> Logs { get; } = new();

        public Task<RequisitionSnapshot?> GetAsync(Guid requisitionId, CancellationToken cancellationToken) => Task.FromResult(Requisition);

        public Task MarkSentToSapAsync(Guid requisitionId, CancellationToken cancellationToken)
        {
            Calls.Add("SentToSap");
            return Task.CompletedTask;
        }

        public Task MarkPurchaseOrderCreatedAsync(Guid requisitionId, string purchaseOrder, CancellationToken cancellationToken)
        {
            Calls.Add($"PurchaseOrderCreated:{purchaseOrder}");
            return Task.CompletedTask;
        }

        public Task MarkIntegrationFailedAsync(Guid requisitionId, string message, CancellationToken cancellationToken)
        {
            Calls.Add($"Failed:{message}");
            return Task.CompletedTask;
        }

        public Task WriteLogAsync(IntegrationLogEntry entry, CancellationToken cancellationToken)
        {
            Logs.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSap : ISapClient
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public string? LastCorrelationId { get; private set; }

        public Task<SapPurchaseOrderResponse> CreatePurchaseOrderAsync(SapPurchaseOrder order, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            LastCorrelationId = correlationId;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new SapPurchaseOrderResponse { PurchaseOrder = "4500012345" });
        }
    }
}
