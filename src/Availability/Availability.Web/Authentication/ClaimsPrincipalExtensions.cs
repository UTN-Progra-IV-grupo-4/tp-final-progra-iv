using System.Security.Claims;

namespace Availability.Web.Authentication;

public static class ClaimsPrincipalExtensions
{
    // Same lookup order as POST /api/reservations and the status page, so the user id stored with
    // a request is the one the ownership checks compare against.
    public static string? GetUserId(this ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.Identity?.Name;
}
