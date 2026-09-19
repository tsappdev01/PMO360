using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>
/// FR-01. One master record per project across every reporting entity.
/// The fields below carry the current position; how the project reached it lives in
/// <see cref="Updates"/>. Nothing here is edited directly by a reader of the dashboard —
/// the only write path for status, progress, phase and the attention flag is
/// submitting an update (FR-11).
/// </summary>
public class Project
{
    public int Id { get; set; }

    /// <summary>Unique business key, e.g. PRJ-0142 (FR-01). Issued by the PMO on creation.</summary>
    public string ProjectCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int ReportingEntityId { get; set; }
    public ReportingEntity? ReportingEntity { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? ConsultantId { get; set; }
    public Consultant? Consultant { get; set; }

    public PersonRef ProjectOwner { get; set; } = new();
    public PersonRef BusinessOwner { get; set; } = new();
    public PersonRef ProjectManager { get; set; } = new();

    /// <summary>Receives the WF-02 escalation when a project goes Delayed.</summary>
    public PersonRef Sponsor { get; set; } = new();

    public ProjectStatus Status { get; set; } = ProjectStatus.OnTrack;

    /// <summary>FR-03. Whole number, 0-100. Enforced by check constraint as well as by the form.</summary>
    public int ProgressPercent { get; set; }

    public int? CurrentPhaseId { get; set; }
    public Phase? CurrentPhase { get; set; }

    public ProjectPriority Priority { get; set; } = ProjectPriority.Medium;

    public DateOnly StartDate { get; set; }
    public DateOnly TargetCompletionDate { get; set; }

    /// <summary>Carried from the most recent update, so the dashboard never re-reads history.</summary>
    public string? KeyUpdate { get; set; }
    public string? NextAction { get; set; }
    public PersonRef? NextActionOwner { get; set; }
    public DateOnly? NextActionDueDate { get; set; }

    /// <summary>FR-16, BR-03. Set by the update form; a reason is required alongside it.</summary>
    public bool AttentionRequired { get; set; }
    public string? AttentionReason { get; set; }

    /// <summary>FR-12. Stamped by the system on submission, never typed.</summary>
    public DateOnly? LastUpdateDate { get; set; }
    public PersonRef? LastUpdatedBy { get; set; }

    /// <summary>FR-05, BR-07. Closed projects leave the default views but stay in reporting.</summary>
    public RecordState State { get; set; } = RecordState.Active;
    public DateTimeOffset? ClosedOn { get; set; }
    public PersonRef? ClosedBy { get; set; }
    public string? ClosureNote { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public PersonRef CreatedBy { get; set; } = new();

    public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
    public ICollection<RiskIssue> RisksAndIssues { get; set; } = new List<RiskIssue>();
    public ICollection<ProjectUpdate> Updates { get; set; } = new List<ProjectUpdate>();
    public ICollection<ProjectAssignment> Assignments { get; set; } = new List<ProjectAssignment>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public bool IsActive => State == RecordState.Active;
}
