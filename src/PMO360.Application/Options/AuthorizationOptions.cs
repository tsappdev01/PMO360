namespace PMO360.Application.Options;

/// <summary>
/// Section 6: "Access is granted through security groups, not to named individuals."
/// Each role lists the Entra ID group object ids whose members hold it.
/// </summary>
public sealed class RoleGroupOptions
{
    public const string SectionName = "Authorization";

    public Dictionary<string, string[]> RoleGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Entra app roles can be used instead of, or alongside, group ids. When the token carries
    /// roles claims the mapping is the identity, so nothing needs configuring.
    /// </summary>
    public bool TrustAppRoleClaims { get; set; } = true;
}
