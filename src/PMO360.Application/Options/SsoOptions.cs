namespace PMO360.Application.Options;

/// <summary>
/// Whether the portal signs people in through Microsoft Entra ID (BRD section 6), and what to do
/// when it does not.
///
/// Turning SSO off is for development and for demonstrating the portal before the app
/// registration exists. It is not a way to run the portal: with it off, everyone who can reach
/// the site is signed in automatically as <see cref="LocalUser"/>, with whatever roles that user
/// is given. There is no password and no identity — which is exactly why it refuses to start in
/// Production unless somebody has deliberately said otherwise.
/// </summary>
public sealed class SsoOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// True (the default) requires a real Entra ID sign-in. False signs everyone in as
    /// <see cref="LocalUser"/> and takes them straight to the dashboard.
    /// </summary>
    public bool EnableSso { get; set; } = true;

    /// <summary>
    /// Acknowledges that SSO is deliberately off outside Development. Without it the portal
    /// refuses to start, so an unauthenticated portal cannot be published by forgetting a setting.
    /// </summary>
    public bool AllowSsoDisabledOutsideDevelopment { get; set; }

    public LocalUserOptions LocalUser { get; set; } = new();
}

/// <summary>Who everybody is when SSO is off.</summary>
public sealed class LocalUserOptions
{
    public string DisplayName { get; set; } = "Local Developer";

    public string Email { get; set; } = "local.developer@localhost";

    /// <summary>
    /// A stable id, so records written while SSO is off still have something to attribute them
    /// to, and so "My projects" has something to match on.
    /// </summary>
    public string ObjectId { get; set; } = "00000000-0000-0000-0000-00000000dev1";

    /// <summary>
    /// The roles the local user holds. Left empty — the default — they hold every role, so the
    /// whole portal is reachable. Set it to see the portal as one role sees it: a consultant, say.
    ///
    /// It starts empty rather than pre-populated because the configuration binder *appends* to an
    /// array that already has items. A default of all six roles plus six in appsettings.json gave
    /// the local user twelve, and would have made a narrowed list impossible to express: the six
    /// defaults would still have been there.
    /// </summary>
    public string[] Roles { get; set; } = [];

    /// <summary>The roles to actually grant: what was configured, or all of them when nothing was.</summary>
    public string[] EffectiveRoles =>
        Roles.Length > 0
            ? Roles
            : ["Management", "PmoAdministrator", "ProjectManager", "ProjectOwner", "Consultant", "ItSupport"];
}
