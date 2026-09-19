using PMO360.Domain.Enums;

namespace PMO360.Domain.Rules;

/// <summary>BR-01. The fixed definitions, in one place, so the portal and the reports say the same thing.</summary>
public static class ProjectStatusRules
{
    public static string DisplayName(ProjectStatus status) => status switch
    {
        ProjectStatus.OnTrack => "On Track",
        ProjectStatus.AtRisk => "At Risk",
        ProjectStatus.Delayed => "Delayed",
        ProjectStatus.OnHold => "On Hold",
        ProjectStatus.Completed => "Completed",
        _ => status.ToString()
    };

    public static string Definition(ProjectStatus status) => status switch
    {
        ProjectStatus.OnTrack => "Delivering to plan.",
        ProjectStatus.AtRisk => "An identified threat could cause a milestone or the completion date to be missed.",
        ProjectStatus.Delayed => "A milestone or the completion date has already been missed.",
        ProjectStatus.OnHold => "Formally paused.",
        ProjectStatus.Completed => "Delivered and accepted.",
        _ => string.Empty
    };

    /// <summary>BR-02. These two require a key update stating the cause and the recovery action.</summary>
    public static bool RequiresKeyUpdate(ProjectStatus status) =>
        status is ProjectStatus.AtRisk or ProjectStatus.Delayed;

    /// <summary>Statuses that count as an exception on the dashboard and in the standard views (FR-23).</summary>
    public static bool IsException(ProjectStatus status) =>
        status is ProjectStatus.AtRisk or ProjectStatus.Delayed;

    /// <summary>
    /// A status change that management is told about immediately (WF-01, WF-02). Moving from
    /// At Risk to Delayed escalates again; moving back to On Track does not raise an alert.
    /// </summary>
    public static bool HasWorsened(ProjectStatus? previous, ProjectStatus current) =>
        IsException(current) && previous != current;

    public static readonly ProjectStatus[] All =
    [
        ProjectStatus.OnTrack, ProjectStatus.AtRisk, ProjectStatus.Delayed,
        ProjectStatus.OnHold, ProjectStatus.Completed
    ];
}
