using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using identity_client_web_app.Models;
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

    public HomeController(ILogger<HomeController> logger, IEmbedService embedService, IConfiguration configuration)
    {
        _logger = logger;
        _embedService = embedService;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
