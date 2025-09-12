using System.Security.Claims;
using identity_client_web_app.Models;

namespace identity_client_web_app.Services;

public class EmbedService : IEmbedService
{
    private readonly ILogger<EmbedService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public EmbedService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<EmbedService> logger)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(nameof(EmbedService));
        _config = config;
        var baseUrl = _config["FunctionApi:BaseUrl"] ?? "https://localhost:7071";
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
    }

    public Task<EmbedTokenResponse> GetEmbedTokenAsync(string workspaceId, string reportId, string username, IEnumerable<string> groups, string userLocation, CancellationToken cancellationToken = default)
    {
        var req = new EmbedTokenRequest(workspaceId, reportId, username, groups, userLocation);
        return RequestTokenAsync(req, cancellationToken);
    }

    public async Task<EmbedTokenResponse> RequestTokenAsync(EmbedTokenRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/generateEmbedToken", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                return new EmbedTokenResponse(null, null, $"Function error: {response.StatusCode} {err}");
            }
            var result = await response.Content.ReadFromJsonAsync<EmbedTokenResponse>(cancellationToken: cancellationToken);
            return result ?? new EmbedTokenResponse(null, null, "Empty response");
        }
        catch (Exception ex)
        {
            // If SSL handshake fails for local dev when using https, retry with http automatically
            if (_httpClient.BaseAddress is not null && _httpClient.BaseAddress.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && _httpClient.BaseAddress.Scheme == Uri.UriSchemeHttps && ex is HttpRequestException)
            {
                try
                {
                    var downgraded = new UriBuilder(_httpClient.BaseAddress) { Scheme = Uri.UriSchemeHttp, Port = _httpClient.BaseAddress.Port }.Uri;
                    _logger.LogWarning(ex, "HTTPS call to local function failed, retrying over HTTP: {url}", downgraded);
                    _httpClient.BaseAddress = downgraded;
                    var retry = await _httpClient.PostAsJsonAsync("/api/generateEmbedToken", request, cancellationToken);
                    if (!retry.IsSuccessStatusCode)
                    {
                        var err = await retry.Content.ReadAsStringAsync(cancellationToken);
                        return new EmbedTokenResponse(null, null, $"Function error after downgrade: {retry.StatusCode} {err}");
                    }
                    var result = await retry.Content.ReadFromJsonAsync<EmbedTokenResponse>(cancellationToken: cancellationToken);
                    return result ?? new EmbedTokenResponse(null, null, "Empty response after downgrade");
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Retry over HTTP failed");
                }
            }
            _logger.LogError(ex, "Error requesting embed token");
            return new EmbedTokenResponse(null, null, ex.Message);
        }
    }
}
