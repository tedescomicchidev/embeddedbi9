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
        // Simulated user location service: always returns Switzerland (CH) and channel 05
        var payload = new { location = "CH", channel = "05" };
        log.LogInformation("Returning fixed location for whereami request");
        return new OkObjectResult(payload)
        {
            ContentTypes = { "application/json" }
        };
    }
}
