using System.Diagnostics;
// removed duplicate usings
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using identity_client_web_app.Services;
using identity_client_web_app.Models;

namespace identity_client_web_app.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IEmbedService _embedService;
    private readonly ILocationService _locationService;

    public HomeController(ILogger<HomeController> logger, IEmbedService embedService, ILocationService locationService, IConfiguration configuration)
    {
        _logger = logger;
        _embedService = embedService;
        _locationService = locationService;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [HttpPost("/EmbedToken")]
    public async Task<IActionResult> GetEmbedToken([FromBody] EmbedTokenClientRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest();

        // Derive username from claims (prefer UPN, then email, then name identifier)
        var principal = User;
        string username = principal.FindFirst("upn")?.Value
            ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value
            ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? principal.Identity?.Name
            ?? "unknown";

        // Collect group claims (Azure AD may emit 'groups' claim or roles) – standard OID: http://schemas.microsoft.com/ws/2008/06/identity/claims/groups
        var groupClaims = principal.Claims
            .Where(c => c.Type == "groups" || c.Type == System.Security.Claims.ClaimTypes.GroupSid || c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        // Retrieve location & channel from whereAmI API (ignoring any client-provided location for integrity)
        var (resolvedLocation, channel) = await _locationService.GetLocationAsync(HttpContext, cancellationToken);

        var resp = await _embedService.GetEmbedTokenAsync(request.WorkspaceId, request.ReportId, username, groupClaims, resolvedLocation, cancellationToken);
        // Attach extra metadata for front-end display (channel & location) by shaping an anonymous object if success or failure
        if (!string.IsNullOrEmpty(resp.Error))
        {
            return StatusCode(500, new { resp.EmbedToken, resp.Expiration, resp.Error, ReportId = request.ReportId, WorkspaceId = request.WorkspaceId, userLocation = resolvedLocation, channel });
        }
        return Ok(new { resp.EmbedToken, resp.Expiration, resp.Error, ReportId = request.ReportId, WorkspaceId = request.WorkspaceId, userLocation = resolvedLocation, channel });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
