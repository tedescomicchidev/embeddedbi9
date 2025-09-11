using identity_client_web_app.Models;

namespace identity_client_web_app.Services;

public interface IEmbedService
{
    Task<EmbedTokenResponse> GetEmbedTokenAsync(string workspaceId, string reportId, string username, IEnumerable<string> groups, string userLocation, CancellationToken cancellationToken = default);
}
