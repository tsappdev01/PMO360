namespace PMO360.Domain.Enums;

/// <summary>
/// BR-01. The five agreed status values. Free text status is not possible (FR-02),
/// so this enum is the only place a status can come from anywhere in the solution.
/// </summary>
public enum ProjectStatus
{
    OnTrack = 1,
    AtRisk = 2,
    Delayed = 3,
    OnHold = 4,
    Completed = 5
}
