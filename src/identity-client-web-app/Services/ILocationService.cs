namespace identity_client_web_app.Services;

public interface ILocationService
{
    /// <summary>
    /// Returns a tuple (location, channel) where location is ISO 3166-1 alpha-2 (upper-case)
    /// and channel is a two-digit string 00-99. Falls back to ("UNKNOWN", "00") on failure.
    /// </summary>
    Task<(string location, string channel)> GetLocationAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}
