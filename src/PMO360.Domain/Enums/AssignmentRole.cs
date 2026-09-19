namespace PMO360.Domain.Enums;

/// <summary>
/// How a named person is attached to one project. Drives row-level visibility:
/// project managers and consultants see only the projects they are assigned to (AC-10).
/// </summary>
public enum AssignmentRole
{
    ProjectManager = 1,
    ProjectOwner = 2,
    BusinessOwner = 3,
    Sponsor = 4,
    ConsultantStaff = 5
}
