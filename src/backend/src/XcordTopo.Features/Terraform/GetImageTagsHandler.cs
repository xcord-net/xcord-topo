using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record GetImageTagsRequest(string RepoName);

public sealed record ImageTagInfo(string Name, string Sha);

public sealed record GetImageTagsResponse(List<ImageTagInfo> Tags);

public sealed class GetImageTagsHandler
    : IRequestHandler<GetImageTagsRequest, Result<GetImageTagsResponse>>
{
    private static readonly HashSet<string> AllowedRepos = new(StringComparer.OrdinalIgnoreCase)
    {
        "xcord-hub",
        "xcord-fed"
    };

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "xcord-topo" },
            { "Accept", "application/vnd.github+json" }
        },
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<Result<GetImageTagsResponse>> Handle(GetImageTagsRequest request, CancellationToken ct)
    {
        if (!AllowedRepos.Contains(request.RepoName))
            return Error.Validation("INVALID_REPO", $"Unknown repository: {request.RepoName}");

        try
        {
            var url = $"https://api.github.com/repos/xcord-net/{request.RepoName}/tags?per_page=50";
            var ghTags = await HttpClient.GetFromJsonAsync<List<GitHubTag>>(url, ct) ?? [];

            var tags = ghTags.Select(t => new ImageTagInfo(t.Name, t.Commit.Sha)).ToList();
            return new GetImageTagsResponse(tags);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("GITHUB_ERROR", $"Failed to fetch tags: {ex.Message}");
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/images/{repoName}/tags", async (
            string repoName,
            GetImageTagsHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetImageTagsRequest(repoName), ct);
        })
        .WithName("GetImageTags")
        .WithTags("Images");
    }

    private sealed record GitHubTag(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("commit")] GitHubCommitRef Commit);

    private sealed record GitHubCommitRef(
        [property: JsonPropertyName("sha")] string Sha);
}
