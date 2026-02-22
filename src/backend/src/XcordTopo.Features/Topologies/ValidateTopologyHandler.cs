using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Validation;

namespace XcordTopo.Features.Topologies;

public sealed record ValidateTopologyRequest(Guid Id);

public sealed record ValidateTopologyResponse(bool IsValid, List<string> Errors);

public sealed class ValidateTopologyHandler(ITopologyStore store, ITopologyValidator validator)
    : IRequestHandler<ValidateTopologyRequest, Result<ValidateTopologyResponse>>
{
    public async Task<Result<ValidateTopologyResponse>> Handle(ValidateTopologyRequest request, CancellationToken ct)
    {
        var topology = await store.GetAsync(request.Id, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.Id} not found");

        var errors = validator.Validate(topology);
        return new ValidateTopologyResponse(errors.Count == 0, errors);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{id:guid}/validate", async (
            Guid id, ValidateTopologyHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ValidateTopologyRequest(id), ct);
        })
        .WithName("ValidateTopology")
        .WithTags("Topologies");
    }
}
