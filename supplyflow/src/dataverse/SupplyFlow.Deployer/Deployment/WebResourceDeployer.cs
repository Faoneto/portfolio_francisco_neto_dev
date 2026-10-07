using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace SupplyFlow.Deployer.Deployment;

/// <summary>
/// Uploads the bundled client scripts (src/webresources/dist) as web resources and publishes only them.
/// The relative path becomes the web resource name: dist/fno_/js/requisition.form.js → fno_/js/requisition.form.js
/// </summary>
public sealed class WebResourceDeployer
{
    private static readonly IReadOnlyDictionary<string, int> Types = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        [".htm"] = 1,
        [".html"] = 1,
        [".css"] = 2,
        [".js"] = 3,
        [".xml"] = 4,
        [".png"] = 5,
        [".jpg"] = 6,
        [".gif"] = 7,
        [".svg"] = 11,
        [".resx"] = 12,
    };

    private readonly IOrganizationService _service;
    private readonly ILogger _logger;
    private readonly string _solution;

    public WebResourceDeployer(IOrganizationService service, ILogger logger, string solutionUniqueName)
    {
        _service = service;
        _logger = logger;
        _solution = solutionUniqueName;
    }

    public static string ToWebResourceName(string root, string file)
    {
        return Path.GetRelativePath(root, file).Replace('\\', '/');
    }

    public void Deploy(string folder)
    {
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => Types.ContainsKey(Path.GetExtension(f)))
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No web resources found in {folder}. Run 'npm run build' in src/webresources first.");
        }

        var published = new List<Guid>();
        foreach (var file in files)
        {
            var name = ToWebResourceName(folder, file);
            var record = new Entity("webresource")
            {
                ["content"] = Convert.ToBase64String(File.ReadAllBytes(file)),
                ["displayname"] = name,
            };

            var existing = _service.FindFirst("webresource", "name", name, "webresourceid");
            if (existing is null)
            {
                record["name"] = name;
                record["webresourcetype"] = new OptionSetValue(Types[Path.GetExtension(file)]);
                published.Add(_service.CreateInSolution(record, _solution));
                _logger.LogInformation("Created web resource {Name}", name);
            }
            else
            {
                record.Id = existing.Id;
                _service.Update(record);
                published.Add(existing.Id);
                _logger.LogInformation("Updated web resource {Name}", name);
            }
        }

        var ids = string.Concat(published.Select(id => $"<webresource>{{{id}}}</webresource>"));
        _service.Execute(new PublishXmlRequest { ParameterXml = $"<importexportxml><webresources>{ids}</webresources></importexportxml>" });
        _logger.LogInformation("Published {Count} web resources", published.Count);
    }
}
