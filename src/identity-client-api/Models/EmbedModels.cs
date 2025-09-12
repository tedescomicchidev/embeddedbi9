#nullable enable
using System;
using System.Collections.Generic;

namespace identity_client_api.Models;

public record EmbedTokenRequest(
    string WorkspaceId,
    string ReportId,
    string Username,
    IEnumerable<string> Groups,
    string UserLocation
);

public record EmbedTokenResponse(
    string? EmbedToken,
    DateTime? Expiration,
    string? Error,
    string? ReportId = null,
    string? WorkspaceId = null
);
