using System.Net;
using System.Text.Json;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace whereami_function;

public static class WhereAmIFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [FunctionName("WhereAmI")]  // function name
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whereAmI")] HttpRequest req,
        ILogger log)
    {
        // For now always return Switzerland (CH) and channel 05
        var result = new { location = "CH", channel = "05" };
        return new OkObjectResult(result);
    }
}
