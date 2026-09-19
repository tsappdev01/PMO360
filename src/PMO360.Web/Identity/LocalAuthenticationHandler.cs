using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PMO360.Application.Options;

namespace PMO360.Web.Identity;

/// <summary>
/// Stands in for Entra ID when <c>Authentication:EnableSso</c> is false.
///
/// It authenticates every request as the configured local user, so the portal opens on the
/// dashboard with no sign-in at all. That is the point — and it is also why this handler exists
/// only for development and demonstration, and why the portal says so on every page.
/// </summary>
public sealed class LocalAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<SsoOptions> ssoOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PMO360.LocalDevelopment";

    private readonly LocalUserOptions _localUser = ssoOptions.Value.LocalUser;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new("name", _localUser.DisplayName),
            new(ClaimTypes.Name, _localUser.DisplayName),
            new("preferred_username", _localUser.Email),
            new(ClaimTypes.Email, _localUser.Email),
            // The same claim type Entra ID uses, so nothing downstream needs to know the
            // difference between this and a real sign-in.
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", _localUser.ObjectId)
        };

        claims.AddRange(_localUser.EffectiveRoles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName, "name", ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
