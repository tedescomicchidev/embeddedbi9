using System.Text.Json;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace whereami_function;

public static class WhereAmIFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [FunctionName("WhereAmI")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whereami")] HttpRequest req,
        ILogger log)
    {
        const string userHeader = "X-WhereAmI-User";
        const string keyHeader = "X-WhereAmI-Key";
        string user = req.Headers[userHeader];
        if (string.IsNullOrWhiteSpace(user)) user = "unknown";
        log.LogInformation("whereAmI request for user {user}", user);
        
        var providedKey = req.Headers[keyHeader];
        var expectedKey = Environment.GetEnvironmentVariable("WHEREAMI_API_KEY");
        if (!string.IsNullOrWhiteSpace(expectedKey) && !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            log.LogWarning("Invalid or missing API key for whereAmI request (user: {user})", user);
            return new UnauthorizedResult();
        }

        var payload = new { location = "CH", channel = "05" };
        log.LogInformation("whereAmI request for user {user} returning {loc}/{channel}", user, payload.location, payload.channel);
        return new OkObjectResult(payload) { ContentTypes = { "application/json" } };
    }
}
