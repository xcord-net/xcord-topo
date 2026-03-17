using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record GetImageTagsRequest(string RepoName);

public sealed record ImageTagInfo(string Name, string Sha);

public sealed record GetImageTagsResponse(List<ImageTagInfo> Tags);

public sealed class GetImageTagsHandler(ImagePluginRegistry pluginRegistry)
    : IRequestHandler<GetImageTagsRequest, Result<GetImageTagsResponse>>
{
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
        // Find the plugin whose GitRepoUrl ends with the requested repo name
        var gitRepoUrl = ResolveGitRepoUrl(request.RepoName);
        if (gitRepoUrl is null)
            return Error.Validation("INVALID_REPO", $"Unknown repository: {request.RepoName}");

        // Extract owner/repo from the git URL (e.g. "https://github.com/xcord-net/xcord-hub.git" -> "xcord-net/xcord-hub")
        var uri = new Uri(gitRepoUrl);
        var ownerRepo = uri.AbsolutePath.TrimStart('/').TrimEnd('/');
        if (ownerRepo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            ownerRepo = ownerRepo[..^4];

        try
        {
            var url = $"https://api.github.com/repos/{ownerRepo}/tags?per_page=50";
            var ghTags = await HttpClient.GetFromJsonAsync<List<GitHubTag>>(url, ct) ?? [];

            var tags = ghTags.Select(t => new ImageTagInfo(t.Name, t.Commit.Sha)).ToList();
            return new GetImageTagsResponse(tags);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("GITHUB_ERROR", $"Failed to fetch tags: {ex.Message}");
        }
    }

    private string? ResolveGitRepoUrl(string repoName)
    {
        foreach (var plugin in pluginRegistry.GetAll())
        {
            var docker = plugin.GetDockerBehavior();
            if (docker.GitRepoUrl is null) continue;

            // Match by repo name: "xcord-hub" matches "https://github.com/xcord-net/xcord-hub.git"
            var uri = new Uri(docker.GitRepoUrl);
            var lastSegment = uri.Segments[^1].TrimEnd('/');
            if (lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                lastSegment = lastSegment[..^4];

            if (string.Equals(lastSegment, repoName, StringComparison.OrdinalIgnoreCase))
                return docker.GitRepoUrl;
        }

        return null;
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
