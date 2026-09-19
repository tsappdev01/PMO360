namespace PMO360.Domain.Enums;

/// <summary>
/// BRD section 6. Granted through Entra ID security groups, never to named individuals —
/// see <c>Authorization:RoleGroups</c> in configuration for the group-to-role mapping.
/// </summary>
public static class PmoRole
{
    public const string Management = "Management";
    public const string PmoAdministrator = "PmoAdministrator";
    public const string ProjectManager = "ProjectManager";
    public const string ProjectOwner = "ProjectOwner";
    public const string Consultant = "Consultant";
    public const string ItSupport = "ItSupport";

    public static readonly string[] All =
    [
        Management, PmoAdministrator, ProjectManager, ProjectOwner, Consultant, ItSupport
    ];
}
