namespace identity_client_web_app.Models;

public record EmbedTokenResponse(
    string? EmbedToken,
    DateTime? Expiration,
    string? Error
);
