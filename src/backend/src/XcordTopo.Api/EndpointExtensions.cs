using System.Reflection;
using XcordTopo;

namespace XcordTopo.Api;

public static class EndpointExtensions
{
    public static void MapHandlerEndpoints(this IEndpointRouteBuilder app, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethod("Map", BindingFlags.Public | BindingFlags.Static,
                null, [typeof(IEndpointRouteBuilder)], null) is not null);

        foreach (var type in handlerTypes)
        {
            type.GetMethod("Map", BindingFlags.Public | BindingFlags.Static,
                null, [typeof(IEndpointRouteBuilder)], null)!
                .Invoke(null, [app]);
        }
    }

    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        t.GetInterfaces().Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));

        foreach (var type in handlerTypes)
        {
            services.AddScoped(type);
        }

        return services;
    }
}
