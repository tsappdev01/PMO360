using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PMO360.Application.Options;
using PMO360.Domain.Enums;

namespace PMO360.Web.Identity;

/// <summary>
/// Section 6: "Access is granted through security groups, not to named individuals."
///
/// Entra ID puts group membership in the token as object ids. This turns those ids into the
/// BRD's role names, using the mapping in configuration, so nothing downstream has to know a
/// group id. App roles, where the tenant prefers them, are taken as they are.
/// </summary>
public sealed class RoleClaimsTransformation(
    IOptionsMonitor<RoleGroupOptions> options,
    ILogger<RoleClaimsTransformation> logger) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        // Transformation runs on every request in ASP.NET Core, so this must be idempotent:
        // adding the same role twice would be harmless but wasteful, and a second pass must not
        // see its own output as new input.
        if (identity.HasClaim(c => c.Type == RoleMarker))
        {
            return Task.FromResult(principal);
        }

        var configured = options.CurrentValue;
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // App roles, when the tenant assigns them directly.
        if (configured.TrustAppRoleClaims)
        {
            foreach (var claim in principal.FindAll("roles").Concat(principal.FindAll(ClaimTypes.Role)))
            {
                if (PmoRole.All.Contains(claim.Value, StringComparer.OrdinalIgnoreCase))
                {
                    granted.Add(claim.Value);
                }
            }
        }

        // Security groups, as object ids in the "groups" claim.
        var groups = principal.FindAll("groups").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (groups.Count > 0)
        {
            foreach (var (role, groupIds) in configured.RoleGroups)
            {
                if (groupIds.Any(groups.Contains))
                {
                    granted.Add(role);
                }
            }
        }

        if (granted.Count == 0)
        {
            // Authenticated but in none of the groups. They reach the portal and are told they
            // have no access, which is a better answer than an empty dashboard.
            logger.LogInformation(
                "{User} signed in with no PMO360 role. Groups in token: {GroupCount}.",
                principal.Identity.Name, groups.Count);
        }

        foreach (var role in granted)
        {
            if (!identity.HasClaim(ClaimTypes.Role, role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        identity.AddClaim(new Claim(RoleMarker, "1"));

        return Task.FromResult(principal);
    }

    /// <summary>Marks a principal this transformation has already processed.</summary>
    private const string RoleMarker = "pmo360:roles-resolved";
}
