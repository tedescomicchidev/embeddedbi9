namespace identity_client_web_app.Models;

public record EmbedTokenRequest(
    string WorkspaceId,
    string ReportId,
    string Username,
    IEnumerable<string> Groups,
    string UserLocation
);
