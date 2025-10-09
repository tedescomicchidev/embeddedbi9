using System.Net.Http.Json;

namespace identity_client_web_app.Services;

internal sealed class LocationService : ILocationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocationService> _logger;
    private readonly Uri _endpoint = new("http://localhost:7071/api/whereami");

    public LocationService(HttpClient httpClient, ILogger<LocationService> logger, IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        var endpoint = config["WhereAmI:Endpoint"];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            _endpoint = uri;
        }
    }

    public async Task<(string location, string channel)> GetUserLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("whereami endpoint returned status {code}", resp.StatusCode);
                return ("unknown", "00");
            }
            var json = await resp.Content.ReadFromJsonAsync<WhereAmIResponse>(cancellationToken: cancellationToken);
            if (json is null) return ("unknown", "00");
            return (json.Location ?? "unknown", json.Channel ?? "00");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve user location");
            return ("unknown", "00");
        }
    }

    private sealed record WhereAmIResponse(string? Location, string? Channel)
    {
        public string? location { init => Location = value; } // to accept lower-case JSON
        public string? channel { init => Channel = value; }
    }
}
