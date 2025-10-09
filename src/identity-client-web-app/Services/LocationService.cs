using System.Net.Http.Json;

namespace identity_client_web_app.Services;

internal sealed class LocationService : ILocationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocationService> _logger;
    private readonly Uri _endpoint = new("http://localhost:7072/api/whereami");
    private readonly string? _apiKey;

    public LocationService(HttpClient httpClient, ILogger<LocationService> logger, IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        var endpoint = config["WhereAmI:Endpoint"];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            _endpoint = uri;
            _logger.LogInformation("Using WhereAmI endpoint: {url}", _endpoint);
        }
        else
        {
            _logger.LogWarning("Invalid or missing WhereAmI endpoint in configuration, using default: {url}", _endpoint);
        }
        _apiKey = config["WhereAmI:ApiKey"]; // optional
    }

    public async Task<(string location, string channel)> GetUserLocationAsync(string username, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            // Send username header and optional API key.
            req.Headers.Add("X-WhereAmI-User", username);
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                req.Headers.Add("X-WhereAmI-Key", _apiKey);
            }
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
            _logger.LogError(ex, "Failed to retrieve user location, {err}", ex.Message);
            return ("unknown", "00");
        }
    }

    private sealed class WhereAmIResponse
    {
        // Map lowercase JSON names explicitly; avoids duplicate logical names.
        [System.Text.Json.Serialization.JsonPropertyName("location")]
        public string? Location { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("channel")]
        public string? Channel { get; init; }
    }
}
