using System.IO;
using SupplyFlow.Deployer.Deployment;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Definitions;

public class WebResourceDeployerTests
{
    [Fact]
    public void Relative_path_becomes_the_web_resource_name()
    {
        var root = Path.Combine("src", "webresources", "dist");
        var file = Path.Combine(root, "fno_", "js", "requisition.form.js");

        Assert.Equal("fno_/js/requisition.form.js", WebResourceDeployer.ToWebResourceName(root, file));
    }
}
