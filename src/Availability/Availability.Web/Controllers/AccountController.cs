using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Web.Controllers;

[AllowAnonymous]
[Route("Account")]
public class AccountController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IConfiguration configuration, ILogger<AccountController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(string? returnUrl = null)
    {
        // Check if using development authentication
        var useDevelopmentAuth = _configuration.GetValue<bool>("DevelopmentAuthentication:Enabled", false)
            && HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

        if (useDevelopmentAuth)
        {
            // Development mode: authenticate with the development handler
            var result = await HttpContext.AuthenticateAsync("Development");
            if (result.Succeeded)
            {
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal);
                _logger.LogInformation("User logged in with development authentication");
                return LocalRedirect(returnUrl ?? "/");
            }
        }

        // Production mode: initiate OpenID Connect flow
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(LoginCallback), new { returnUrl })
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("LoginCallback")]
    public async Task<IActionResult> LoginCallback(string? returnUrl = null)
    {
        // OpenID Connect callback
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result?.Succeeded == true)
        {
            _logger.LogInformation("User logged in with OpenID Connect");
            return LocalRedirect(returnUrl ?? "/");
        }

        return RedirectToAction(nameof(Login), new { returnUrl });
    }

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var useDevelopmentAuth = _configuration.GetValue<bool>("DevelopmentAuthentication:Enabled", false)
            && HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

        if (useDevelopmentAuth)
        {
            // Development mode: just sign out the cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out (development mode)");
        }
        else
        {
            // Production mode: sign out of OpenID Connect
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out (OpenID Connect)");
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpGet("AccessDenied")]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
