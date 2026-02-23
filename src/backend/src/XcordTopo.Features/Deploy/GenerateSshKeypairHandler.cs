using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace XcordTopo.Features.Deploy;

public sealed record GenerateSshKeypairRequest;

public sealed record GenerateSshKeypairResponse(string PublicKey, string PrivateKey);

public sealed class GenerateSshKeypairHandler
    : IRequestHandler<GenerateSshKeypairRequest, Result<GenerateSshKeypairResponse>>
{
    public async Task<Result<GenerateSshKeypairResponse>> Handle(GenerateSshKeypairRequest request, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xcord-topo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var keyPath = Path.Combine(tempDir, "id_ed25519");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh-keygen",
                ArgumentList = { "-t", "ed25519", "-f", keyPath, "-N", "", "-C", "xcord-topo" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return Error.Failure("SSH_KEYGEN_FAILED", "Failed to start ssh-keygen process");

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                return Error.Failure("SSH_KEYGEN_FAILED", $"ssh-keygen exited with code {process.ExitCode}: {stderr}");
            }

            var publicKey = (await File.ReadAllTextAsync($"{keyPath}.pub", ct)).Trim();
            var privateKey = (await File.ReadAllTextAsync(keyPath, ct)).Trim();

            return new GenerateSshKeypairResponse(publicKey, privateKey);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/ssh/generate-keypair", async (
            GenerateSshKeypairHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GenerateSshKeypairRequest(), ct);
        })
        .WithName("GenerateSshKeypair")
        .WithTags("Deploy");
    }
}
