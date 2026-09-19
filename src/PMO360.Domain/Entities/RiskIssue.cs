using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>FR-09. Risks and issues share one register, separated by <see cref="Type"/>.</summary>
public class RiskIssue
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public RiskType Type { get; set; } = RiskType.Risk;
    public string Description { get; set; } = string.Empty;
    public RiskSeverity Severity { get; set; } = RiskSeverity.Medium;
    public PersonRef Owner { get; set; } = new();
    public string? Mitigation { get; set; }
    public DateOnly? DueDate { get; set; }

    public RiskStatus Status { get; set; } = RiskStatus.Open;

    /// <summary>FR-10. Required when the item is closed — a closed item keeps its record, with the reason.</summary>
    public string? ClosureNote { get; set; }
    public DateTimeOffset? ClosedOn { get; set; }
    public PersonRef? ClosedBy { get; set; }

    public DateTimeOffset RaisedOn { get; set; }
    public PersonRef RaisedBy { get; set; } = new();
    public DateTimeOffset? ModifiedOn { get; set; }
    public PersonRef? ModifiedBy { get; set; }

    public bool IsOpen => Status == RiskStatus.Open;
    public bool IsOpenHigh => IsOpen && Severity == RiskSeverity.High;
    public bool IsOverdueAt(DateOnly today) => IsOpen && DueDate is { } due && due < today;
}
