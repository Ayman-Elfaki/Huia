using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Huia.E2ETests;

public sealed class AspireCustomCommandTests
{
    [Fact]
    public async Task AppHost_registers_custom_commands_for_e2e_artifacts()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Huia_AppHost>(
            ["--Huia:EnableE2E=true", "--Huia:UsePostgresVolume=false"]);

        var resources = builder.Resources.ToList();

        // 1. IdentityServer should have both build-e2e-artifact and build-all-e2e-artifacts commands
        var idp = resources.FirstOrDefault(r => r.Name == "huia-identityserver");
        Assert.NotNull(idp);

        var idpCommands = idp.Annotations.OfType<ResourceCommandAnnotation>().ToList();
        Assert.Contains(idpCommands, c => c.Name == "build-e2e-artifact" && c.DisplayName == "Build E2E Artifact");
        Assert.Contains(idpCommands, c => c.Name == "build-all-e2e-artifacts" && c.DisplayName == "Build All E2E Artifacts");

        // 2. Check individual .NET and frontend apps have build-e2e-artifact command
        var targetResourceNames = new[]
        {
            "huia-external",
            "shop-api",
            "todo-api",
            "todo-app",
            "admin-app",
            "shop-app",
            "todo-next",
            "shop-next"
        };

        foreach (var resourceName in targetResourceNames)
        {
            var res = resources.FirstOrDefault(r => r.Name == resourceName);
            Assert.NotNull(res);

            var commands = res.Annotations.OfType<ResourceCommandAnnotation>().ToList();
            Assert.Contains(commands, c => c.Name == "build-e2e-artifact" && c.DisplayName == "Build E2E Artifact");
            var buildCommand = commands.First(c => c.Name == "build-e2e-artifact");
            Assert.NotNull(buildCommand.ExecuteCommand);
        }
    }
}
