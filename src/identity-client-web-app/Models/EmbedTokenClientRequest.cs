namespace identity_client_web_app.Models;

// Request payload sent from browser – username and groups are derived server-side from claims.
public record EmbedTokenClientRequest(
    string WorkspaceId,
    string ReportId,
    string UserLocation
);
