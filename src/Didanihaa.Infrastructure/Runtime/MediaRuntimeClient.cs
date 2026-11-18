using System.Net.Http.Json;
using System.Text.Json;
using Didanihaa.Core;

namespace Didanihaa.Infrastructure;

public sealed class MediaRuntimeClient(RuntimeLayout layout, HttpClient client)
{
    public async Task<RuntimeStatus?> ReadReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await client.GetFromJsonAsync<RuntimeStatus>(
                $"http://127.0.0.1:{layout.Configuration.MediaPort}/_internal/runtime", cancellationToken);
            return status is { CatalogReady: true, Identity: not null }
                && status.Identity.IsCurrent(layout.Root, RuntimeRole.Media, layout.MediaExecutableCandidates) ? status : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { return null; }
    }
}
