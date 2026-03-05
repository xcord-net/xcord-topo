using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace XcordTopo.Tests.Integration;

public sealed class TopoWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"xcord-topo-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Data:BasePath", _tempDir);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
