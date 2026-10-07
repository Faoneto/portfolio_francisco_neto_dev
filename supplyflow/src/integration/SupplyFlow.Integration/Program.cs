using System;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using SupplyFlow.Integration.Dataverse;
using SupplyFlow.Integration.Processing;
using SupplyFlow.Integration.Sap;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddOptions<SapOptions>().Bind(builder.Configuration.GetSection(SapOptions.Section));

// SAP client with the standard resilience pipeline: rate limiter, total timeout, retry (exponential
// back-off + jitter, only for transient errors), circuit breaker and per-attempt timeout.
builder.Services
    .AddHttpClient<ISapClient, SapClient>((sp, http) =>
    {
        var options = sp.GetRequiredService<IOptions<SapOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler();

// One ServiceClient per process (thread-safe, keeps the token cache and connection pool).
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<ServiceClient>>();
    var url = new Uri(configuration["Dataverse:Url"] ?? throw new InvalidOperationException("Dataverse:Url is not configured."));
    var clientSecret = configuration["Dataverse:ClientSecret"];

    if (!string.IsNullOrEmpty(clientSecret))
    {
        // Local development with an app registration.
        return new ServiceClient(url, configuration["Dataverse:ClientId"], clientSecret, useUniqueInstance: false, logger);
    }

    // Azure: managed identity (registered as an Application User in Dataverse) – no secret to rotate.
    var credential = new DefaultAzureCredential();
    return new ServiceClient(url, async resource =>
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { $"{new Uri(resource).GetLeftPart(UriPartial.Authority)}/.default" }), default);
        return token.Token;
    }, useUniqueInstance: false, logger);
});

builder.Services.AddSingleton<IRequisitionGateway, DataverseRequisitionGateway>();
builder.Services.AddScoped<RequisitionIntegrationProcessor>();

builder.Build().Run();
