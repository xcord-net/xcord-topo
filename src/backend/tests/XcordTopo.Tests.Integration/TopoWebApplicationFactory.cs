using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace XcordTopo.Tests.Integration;

public sealed class TopoWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } = Path.Combine(
        Path.GetTempPath(), $"xcord-topo-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Data:BasePath", DataPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(DataPath))
            Directory.Delete(DataPath, true);
    }
}
