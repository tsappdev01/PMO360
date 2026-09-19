using Microsoft.AspNetCore.Authorization;
using PMO360.Domain.Enums;

namespace PMO360.Web.Identity;

/// <summary>Section 6, as policies. The names are used on the pages with [Authorize].</summary>
public static class AuthorizationPolicies
{
    /// <summary>Anyone with a PMO360 role at all. Being in the tenant is not being in the portal.</summary>
    public const string PortalUser = "PortalUser";

    /// <summary>The whole portfolio and every report: Management, the PMO, IT Support.</summary>
    public const string ViewPortfolio = "ViewPortfolio";

    /// <summary>Submit updates and maintain milestones and risks.</summary>
    public const string Contribute = "Contribute";

    /// <summary>Create, close and reopen projects; maintain permissions. PMO only (FR-05).</summary>
    public const string Administer = "Administer";

    public static void AddPmoPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(PortalUser, policy => policy.RequireRole(PmoRole.All));

        options.AddPolicy(ViewPortfolio, policy => policy.RequireRole(
            PmoRole.Management, PmoRole.PmoAdministrator, PmoRole.ItSupport));

        options.AddPolicy(Contribute, policy => policy.RequireRole(
            PmoRole.ProjectManager, PmoRole.Consultant, PmoRole.PmoAdministrator));

        options.AddPolicy(Administer, policy => policy.RequireRole(PmoRole.PmoAdministrator));

        // Nothing in the portal is anonymous: an unauthenticated request is sent to Entra ID.
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
}
