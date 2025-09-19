#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using Microsoft.Rest;
using identity_client_api.Models;

namespace identity_client_api;

public static class GetEmbedToken
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [FunctionName("GetEmbedToken")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generateEmbedToken")] HttpRequest req,
        ILogger log)
    {
        try
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest("Empty body");
            }
            var request = JsonSerializer.Deserialize<EmbedTokenRequest>(body, JsonOptions);
            if (request is null)
            {
                return BadRequest("Invalid JSON");
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(request.WorkspaceId)) missing.Add(nameof(request.WorkspaceId));
            if (string.IsNullOrWhiteSpace(request.ReportId)) missing.Add(nameof(request.ReportId));
            if (string.IsNullOrWhiteSpace(request.Username)) missing.Add(nameof(request.Username));
            if (string.IsNullOrWhiteSpace(request.UserLocation)) missing.Add(nameof(request.UserLocation));
            if (missing.Any())
            {
                return BadRequest($"Missing required fields: {string.Join(", ", missing)}");
            }

            // Validate user + location via CSV stored in blob (Azurite/local or real storage)
            bool authorized = await IsAuthorizedAsync(request.Username, request.UserLocation, log);
            if (!authorized)
            {
                return new ObjectResult(new EmbedTokenResponse(null, null, "User and location not authorized")) { StatusCode = (int)HttpStatusCode.Forbidden };
            }

            // Acquire Power BI access token using service principal
            var pbiToken = await AcquirePowerBiAccessTokenAsync(log);
            if (string.IsNullOrEmpty(pbiToken))
            {
                return new ObjectResult(new EmbedTokenResponse(null, null, "Failed to acquire Power BI access token")) { StatusCode = 500 };
            }

            // Generate embed token
            var embedResponse = await GenerateEmbedTokenAsync(pbiToken, request, log);
            if (embedResponse.EmbedToken == null)
            {
                return new ObjectResult(embedResponse) { StatusCode = 500 };
            }
            return new OkObjectResult(embedResponse);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error generating embed token");
            return new ObjectResult(new EmbedTokenResponse(null, null, ex.Message)) { StatusCode = 500 };
        }
    }

    private static IActionResult BadRequest(string message) => new BadRequestObjectResult(new EmbedTokenResponse(null, null, message));

    private static async Task<bool> IsAuthorizedAsync(string username, string location, ILogger log)
    {
        try
        {
            // Support both classic connection string and managed identity based configuration.
            // 1. If AzureWebJobsStorage is a full connection string (contains AccountName=) use it directly.
            // 2. Else look for AzureWebJobsStorage__blobServiceUri (Functions v4 MSI pattern) and build client with DefaultAzureCredential.
            // 3. Back-compat: if AzureWebJobsStorage is just a blob service URI (starts with https://) also treat as endpoint.
            var raw = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            var containerName = Environment.GetEnvironmentVariable("USER_CSV_CONTAINER") ?? "data";
            var blobName = Environment.GetEnvironmentVariable("USER_CSV_FILENAME") ?? "user_locations.csv";

            BlobClient blobClient;
            if (!string.IsNullOrWhiteSpace(raw) && raw.Contains("AccountName=", StringComparison.OrdinalIgnoreCase))
            {
                // Classic connection string
                blobClient = new BlobContainerClient(raw, containerName).GetBlobClient(blobName);
            }
            else
            {
                // Try endpoint style
                var endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri") ?? raw; // raw may itself be an endpoint URL
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    log.LogWarning("AzureWebJobsStorage not configured (neither connection string nor endpoint)");
                    return false;
                }
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var serviceUri))
                {
                    log.LogWarning("AzureWebJobsStorage endpoint value invalid: {value}", endpoint);
                    return false;
                }
                // Build container client using service URI + container name with managed identity credential
                // serviceUri could be e.g. https://<account>.blob.core.windows.net
                var containerUri = new Uri(serviceUri, containerName);
                var cred = new DefaultAzureCredential();
                var containerClient = new BlobContainerClient(containerUri, cred);
                blobClient = containerClient.GetBlobClient(blobName);
            }

            log.LogInformation("Checking authorization for user {user} at location {loc} using blob {blob}", username, location, blobClient.Uri);

            if (!await blobClient.ExistsAsync())
            {
                log.LogWarning("User CSV blob not found: {blob}", blobName);
                return false;
            }
            using var stream = await blobClient.OpenReadAsync();
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (string.Equals(parts[0], username, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(parts[1], location, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error validating user authorization. Msg: {msg}", ex.Message );
            return false;
        }
    }

    private static async Task<string?> AcquirePowerBiAccessTokenAsync(ILogger log)
    {
        try
        {
            var tenantId = Environment.GetEnvironmentVariable("PBI_TENANT_ID");
            var clientId = Environment.GetEnvironmentVariable("PBI_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("PBI_CLIENT_SECRET");
            if (new[] { tenantId, clientId, clientSecret }.Any(string.IsNullOrWhiteSpace))
            {
                log.LogWarning("Power BI service principal credentials not fully configured");
                return null;
            }
            var scopes = new[] { "https://analysis.windows.net/powerbi/api/.default" };
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(scopes));
            return token.Token;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to acquire Power BI access token");
            return null;
        }
    }

    private static async Task<EmbedTokenResponse> GenerateEmbedTokenAsync(string powerBiAccessToken, EmbedTokenRequest request, ILogger log)
    {
        try
        {
            using var client = new PowerBIClient(new Uri("https://api.powerbi.com/"), new TokenCredentials(powerBiAccessToken, "Bearer"));
            // Get report to ensure it exists
            var report = await client.Reports.GetReportInGroupAsync(Guid.Parse(request.WorkspaceId), Guid.Parse(request.ReportId));
            var datasetId = report.DatasetId;

            var generateTokenRequestParameters = new GenerateTokenRequestV2(
                reports: new List<GenerateTokenRequestV2Report>()
                {
                    new GenerateTokenRequestV2Report(report.Id, allowEdit: false)
                },
                datasets: new List<GenerateTokenRequestV2Dataset>() { new(datasetId) },
                targetWorkspaces: new List<GenerateTokenRequestV2TargetWorkspace>() { new(Guid.Parse(request.WorkspaceId)) }
            );

            var tokenResponse = await client.EmbedToken.GenerateTokenAsync(generateTokenRequestParameters);
            DateTime? expOffset = null;
            try
            {
                var exp = tokenResponse.Expiration;
                if (exp != default)
                {
                    // tokenResponse.Expiration is DateTime? already or DateTime; ensure UTC
                    expOffset = DateTime.SpecifyKind(exp, DateTimeKind.Utc);
                }
            }
            catch { }
            return new EmbedTokenResponse(tokenResponse.Token, expOffset, null, request.ReportId, request.WorkspaceId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to generate embed token");
            return new EmbedTokenResponse(null, null, ex.Message, request.ReportId, request.WorkspaceId);
        }
    }
}
