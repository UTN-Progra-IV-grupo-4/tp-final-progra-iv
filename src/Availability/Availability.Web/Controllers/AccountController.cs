using Availability.Web.Authentication;
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
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthenticationSchemeProvider schemeProvider, ILogger<AccountController> logger)
    {
        _schemeProvider = schemeProvider;
        _logger = logger;
    }

    [HttpGet("Login")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = SafeReturnUrl(returnUrl);
        ViewData["IsDevelopmentLogin"] = await IsDevelopmentLoginAsync();
        return View();
    }

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(string? returnUrl = null)
    {
        var safeReturnUrl = SafeReturnUrl(returnUrl);

        if (await IsDevelopmentLoginAsync())
        {
            // Development mode: the fixed user comes from configuration, never from the request.
            var result = await HttpContext.AuthenticateAsync(DevelopmentAuthenticationHandler.SchemeName);
            if (!result.Succeeded || result.Principal is null)
            {
                return Forbid();
            }

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal);
            _logger.LogInformation("User logged in with development authentication");
            return LocalRedirect(safeReturnUrl);
        }

        // Production mode: initiate OpenID Connect flow.
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(LoginCallback), new { returnUrl = safeReturnUrl })
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("LoginCallback")]
    public async Task<IActionResult> LoginCallback(string? returnUrl = null)
    {
        var safeReturnUrl = SafeReturnUrl(returnUrl);

        // OpenID Connect callback: the OIDC handler already signed the user into the cookie scheme.
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result.Succeeded)
        {
            _logger.LogInformation("User logged in with OpenID Connect");
            return LocalRedirect(safeReturnUrl);
        }

        return RedirectToAction(nameof(Login), new { returnUrl = safeReturnUrl });
    }

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        if (await IsDevelopmentLoginAsync())
        {
            // Development mode: just sign out the cookie.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out (development mode)");
            return RedirectToAction("Index", "Home");
        }

        // Production mode: returning SignOut (instead of calling SignOutAsync and then redirecting)
        // lets the OpenID Connect handler redirect to Keycloak's end-session endpoint, which ends
        // the Keycloak session too. Keycloak then returns to /signout-callback-oidc -> RedirectUri.
        _logger.LogInformation("User logged out (OpenID Connect)");
        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") ?? "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("AccessDenied")]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // Development login is available only when Program.cs registered the scheme, which requires
    // the Development environment and DevelopmentAuthentication:Enabled.
    private async Task<bool> IsDevelopmentLoginAsync() =>
        await _schemeProvider.GetSchemeAsync(DevelopmentAuthenticationHandler.SchemeName) is not null;

    // Only local URLs are accepted, so the login cannot be used as an open redirect.
    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
