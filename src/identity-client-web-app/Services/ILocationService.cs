namespace identity_client_web_app.Services;

public interface ILocationService
{
    Task<(string location, string channel)> GetUserLocationAsync(CancellationToken cancellationToken = default);
}
