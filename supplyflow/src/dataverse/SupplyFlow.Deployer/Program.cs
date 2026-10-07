using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using SupplyFlow.Deployer.Definitions;
using SupplyFlow.Deployer.Deployment;

// SupplyFlow.Deployer – schema, registration and seed data as code.
//
//   dotnet run -- validate
//   dotnet run -- schema
//   dotnet run -- plugins --assembly ../SupplyFlow.Plugins/bin/Release/net462/SupplyFlow.Plugins.dll
//   dotnet run -- webresources --folder ../../webresources/dist
//   dotnet run -- seed
//   dotnet run -- all --assembly <path> --folder <dist>
//
// Connection: DATAVERSE_CONNECTION environment variable (any Dataverse connection string), e.g.
//   AuthType=ClientSecret;Url=https://<org>.crm2.dynamics.com;ClientId=<app-id>;ClientSecret=<secret>
// or --url https://<org>.crm2.dynamics.com for interactive login.

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("SupplyFlow.Deployer");

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
string? Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

var definitionsFolder = Option("--definitions") ?? Path.Combine(AppContext.BaseDirectory, "definitions");
var schema = DefinitionLoader.LoadSchema(Path.Combine(definitionsFolder, "schema.json"));
var registration = DefinitionLoader.LoadRegistration(Path.Combine(definitionsFolder, "plugin-registration.json"));

var errors = DefinitionValidator.Validate(schema).Concat(DefinitionValidator.Validate(registration, schema)).ToList();
if (errors.Count > 0)
{
    errors.ForEach(e => logger.LogError("{Error}", e));
    return 1;
}

if (command == "validate")
{
    logger.LogInformation("Definitions are valid: {Tables} tables, {Steps} steps, {Apis} custom APIs.",
        schema.Tables.Count, registration.Plugins.Sum(p => p.Steps.Count), registration.CustomApis.Count);
    return 0;
}

if (command is not ("schema" or "plugins" or "webresources" or "seed" or "all"))
{
    Console.WriteLine("Usage: SupplyFlow.Deployer <validate|schema|plugins|webresources|seed|all> [--assembly <dll>] [--folder <dist>] [--url <env-url>] [--definitions <folder>]");
    return command == "help" ? 0 : 1;
}

var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION");
var url = Option("--url");
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (url is null)
    {
        logger.LogError("Set DATAVERSE_CONNECTION or pass --url for interactive login.");
        return 1;
    }

    // Public sample app registration documented by Microsoft for development tools.
    connectionString = $"AuthType=OAuth;Url={url};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto";
}

using var client = new ServiceClient(connectionString);
if (!client.IsReady)
{
    logger.LogError("Connection failed: {Error}", client.LastError);
    return 1;
}

logger.LogInformation("Connected to {Org} ({Url})", client.ConnectedOrgFriendlyName, client.ConnectedOrgUriActual);

if (command is "schema" or "all")
{
    new SchemaDeployer(client, logger).Deploy(schema);
}

if (command is "plugins" or "all")
{
    var assembly = Option("--assembly")
        ?? throw new ArgumentException("--assembly <path to net462 SupplyFlow.Plugins.dll> is required.");
    new PluginRegistrar(client, logger, schema.Solution.UniqueName).Register(registration, assembly);
}

if (command is "webresources" or "all")
{
    var folder = Option("--folder")
        ?? throw new ArgumentException("--folder <src/webresources/dist> is required.");
    new WebResourceDeployer(client, logger, schema.Solution.UniqueName).Deploy(folder);
}

if (command is "seed" or "all")
{
    new SeedData(client, logger).Load();
}

logger.LogInformation("Done.");
return 0;
