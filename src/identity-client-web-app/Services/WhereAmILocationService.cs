using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace identity_client_web_app.Services;

public class WhereAmILocationService : ILocationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WhereAmILocationService> _logger;
    private readonly IConfiguration _config;

    private sealed class WhereAmIResponse
    {
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("channel")] public string? Channel { get; set; }
    }

    public WhereAmILocationService(HttpClient httpClient, ILogger<WhereAmILocationService> logger, IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
        var baseUrl = _config["WhereAmI:BaseUrl"] ?? "http://localhost:5005"; // fictional API
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<(string location, string channel)> GetLocationAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        try
        {
            // Optionally pass along user or IP headers if the API expects them
            var resp = await _httpClient.GetAsync("whereAmI", cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("whereAmI API returned non-success status {Status}", resp.StatusCode);
                return ("UNKNOWN", "00");
            }
            var payload = await resp.Content.ReadFromJsonAsync<WhereAmIResponse>(cancellationToken: cancellationToken);
            var loc = (payload?.Location ?? "UNKNOWN").Trim().ToUpperInvariant();
            if (loc.Length != 2) loc = "UNKNOWN"; // enforce ISO2 format length (simplistic)
            var channel = payload?.Channel?.Trim();
            if (string.IsNullOrWhiteSpace(channel) || channel!.Length > 2) channel = "00";
            // zero-pad numeric channel if needed
            if (int.TryParse(channel, out var chInt)) channel = chInt.ToString("D2");
            return (loc, channel!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling whereAmI API");
            return ("UNKNOWN", "00");
        }
    }
}
