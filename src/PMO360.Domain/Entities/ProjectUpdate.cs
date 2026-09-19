using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>
/// FR-12, FR-13, FR-14. One dated entry per submitted update, written by the system at the same
/// time as the project record. Once submitted it is read-only: a correction is a later update,
/// never an edit of this row. Nothing in the application updates or deletes a submitted entry.
/// </summary>
public class ProjectUpdate
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateOnly UpdateDate { get; set; }

    public ProjectStatus Status { get; set; }
    public int ProgressPercent { get; set; }

    public int? PhaseId { get; set; }
    public Phase? Phase { get; set; }

    /// <summary>The milestone the manager reported against, so history shows what was in flight.</summary>
    public int? MilestoneId { get; set; }
    public Milestone? Milestone { get; set; }

    /// <summary>BR-02. Required when the status is At Risk or Delayed: cause and recovery action.</summary>
    public string? KeyUpdate { get; set; }
    public string? Achievement { get; set; }

    public string? NextAction { get; set; }
    public PersonRef? NextActionOwner { get; set; }
    public DateOnly? NextActionDueDate { get; set; }

    /// <summary>BR-03. Yes requires <see cref="AttentionReason"/>.</summary>
    public bool AttentionRequired { get; set; }
    public string? AttentionReason { get; set; }

    /// <summary>
    /// A draft is the manager's own working copy ("Save as draft" on the update form).
    /// It is not history, is not visible to anyone else, and never reaches the dashboard.
    /// </summary>
    public bool IsDraft { get; set; }

    public PersonRef SubmittedBy { get; set; } = new();
    public DateTimeOffset SubmittedOn { get; set; }

    /// <summary>Status the project held immediately before this update, for trend and alerting.</summary>
    public ProjectStatus? PreviousStatus { get; set; }
    public int? PreviousProgressPercent { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
