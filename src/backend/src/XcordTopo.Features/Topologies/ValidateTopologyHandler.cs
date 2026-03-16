using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Validation;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record ValidateTopologyRequest(Guid Id);

public sealed record ValidateTopologyResponse(
    List<string> Errors,
    bool CanDeploy,
    List<TopologyValidationError> Items);

public sealed class ValidateTopologyHandler(ITopologyStore store, ITopologyValidator validator)
    : IRequestHandler<ValidateTopologyRequest, Result<ValidateTopologyResponse>>
{
    public async Task<Result<ValidateTopologyResponse>> Handle(ValidateTopologyRequest request, CancellationToken ct)
    {
        var topology = await store.GetAsync(request.Id, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.Id} not found");

        var result = validator.ValidateFull(topology);
        return new ValidateTopologyResponse(
            Errors: result.Errors.Select(e => e.Message).ToList(),
            CanDeploy: result.CanDeploy,
            Items: result.Items);
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
