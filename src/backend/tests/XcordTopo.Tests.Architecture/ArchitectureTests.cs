using NetArchTest.Rules;

namespace XcordTopo.Tests.Architecture;

public class ArchitectureTests
{
    [Fact]
    public void Shared_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Result<>).Assembly)
            .ShouldNot()
            .HaveDependencyOn("XcordTopo.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Shared should not depend on Infrastructure. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Shared_ShouldNotDependOn_Features()
    {
        var result = Types.InAssembly(typeof(Result<>).Assembly)
            .ShouldNot()
            .HaveDependencyOn("XcordTopo.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Shared should not depend on Features. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Shared_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(typeof(Result<>).Assembly)
            .ShouldNot()
            .HaveDependencyOn("XcordTopo.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Shared should not depend on Api. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Features()
    {
        var result = Types.InAssembly(typeof(XcordTopo.Infrastructure.Storage.FileTopologyStore).Assembly)
            .ShouldNot()
            .HaveDependencyOn("XcordTopo.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure should not depend on Features. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(typeof(XcordTopo.Infrastructure.Storage.FileTopologyStore).Assembly)
            .ShouldNot()
            .HaveDependencyOn("XcordTopo.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure should not depend on Api. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Features_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(typeof(XcordTopo.Features.Topologies.ListTopologiesHandler).Assembly)
            .ShouldNot()
            .HaveDependencyOn("XcordTopo.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Features should not depend on Api. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Handlers_ShouldHaveMapMethod()
    {
        var handlerTypes = Types.InAssembly(typeof(XcordTopo.Features.Topologies.ListTopologiesHandler).Assembly)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .GetTypes();

        foreach (var type in handlerTypes)
        {
            var mapMethod = type.GetMethod("Map", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.True(mapMethod is not null, $"Handler {type.Name} must have a static Map method");
        }
    }
}
