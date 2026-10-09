using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi;
using SarawakBizOps.Api.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// src/SarawakBizOps.Api/openapi.json is the contract the web app's types are generated from (npm run gen:api), so
/// it must match the API as built. After changing a DTO or an endpoint, regenerate it:
/// <c>UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests</c>, then <c>npm run gen:api</c> in the web app.
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenApiDocumentTests
{
    private readonly ApiFactory _factory;

    public OpenApiDocumentTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void The_checked_in_OpenAPI_document_matches_the_API()
    {
        var document = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var current = document.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(RepositoryRoot(), "src", "SarawakBizOps.Api", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            File.WriteAllText(path, current);
            return;
        }

        var checkedIn = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : string.Empty;
        Assert.True(checkedIn == current,
            "openapi.json is out of date. Run: UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests, " +
            "then npm run gen:api in src/SarawakBizOps.Web, and commit both files.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SarawakBizOps.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("SarawakBizOps.sln not found above the test output.");
    }
}
