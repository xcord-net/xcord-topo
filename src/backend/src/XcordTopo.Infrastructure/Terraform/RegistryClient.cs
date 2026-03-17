namespace XcordTopo.Infrastructure.Terraform;

/// <summary>
/// Verifies images exist on a Docker registry via the HTTP API v2.
/// </summary>
public sealed class RegistryClient
{
    private static readonly HttpClient Client;

    static RegistryClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        Client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Checks which images are missing from the registry.
    /// Returns the list of ImageBuildSpec entries that are NOT present.
    /// </summary>
    public async Task<List<ImageBuildSpec>> FindMissingImagesAsync(
        string registryUrl,
        IReadOnlyList<ImageBuildSpec> images,
        CancellationToken ct)
    {
        var baseUrl = registryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? registryUrl
            : $"https://{registryUrl}";

        var missing = new List<ImageBuildSpec>();
        foreach (var image in images)
        {
            try
            {
                var url = $"{baseUrl}/v2/{image.RegistryName}/manifests/{image.GitRef}";
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                request.Headers.TryAddWithoutValidation("Accept",
                    "application/vnd.docker.distribution.manifest.v2+json");

                var response = await Client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    missing.Add(image);
            }
            catch
            {
                missing.Add(image);
            }
        }
        return missing;
    }
}
