using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>FR-06. Any number per project.</summary>
public class Milestone
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The date currently planned. Moving it is what milestone slippage measures against the baseline.</summary>
    public DateOnly PlannedDate { get; set; }

    /// <summary>Set once, at the first approved plan, and not moved again — the slippage reference.</summary>
    public DateOnly? BaselineDate { get; set; }

    /// <summary>Actual completion, or the current forecast when the milestone is still open.</summary>
    public DateOnly? ActualDate { get; set; }

    public MilestoneStatus Status { get; set; } = MilestoneStatus.NotStarted;
    public int CompletionPercent { get; set; }

    public PersonRef Owner { get; set; } = new();

    public DateTimeOffset CreatedOn { get; set; }
    public PersonRef CreatedBy { get; set; } = new();
    public DateTimeOffset? ModifiedOn { get; set; }
    public PersonRef? ModifiedBy { get; set; }

    /// <summary>A milestone that still counts towards "next" and "overdue" (FR-07, FR-08).</summary>
    public bool IsIncomplete =>
        Status is MilestoneStatus.NotStarted or MilestoneStatus.InProgress;

    /// <summary>FR-08. Past its planned date and not complete.</summary>
    public bool IsOverdueAt(DateOnly today) => IsIncomplete && PlannedDate < today;

    /// <summary>Days between the baseline and where the milestone actually landed, for slippage reporting.</summary>
    public int? SlippageDays =>
        BaselineDate is null ? null : (ActualDate ?? PlannedDate).DayNumber - BaselineDate.Value.DayNumber;
}
