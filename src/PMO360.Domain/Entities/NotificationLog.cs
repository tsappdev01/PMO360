using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>
/// One row per notification the system tried to send (section 5.3). Kept so that "the alert fired"
/// can be evidenced at acceptance (AC-07, AC-08, AC-09), and so a repeating reminder knows
/// whether it already went out today.
/// </summary>
public class NotificationLog
{
    public int Id { get; set; }

    public NotificationKind Kind { get; set; }

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? MilestoneId { get; set; }
    public int? RiskIssueId { get; set; }

    /// <summary>Semicolon-separated addresses, as sent.</summary>
    public string Recipients { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    public DateTimeOffset SentOn { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// Identifies the occasion the notification was for — project, kind and the day or milestone
    /// it relates to. A repeating reminder checks this before sending so a restart or a second
    /// scheduler pass cannot send the same notice twice.
    /// </summary>
    public string DedupeKey { get; set; } = string.Empty;
}
