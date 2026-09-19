using PMO360.Domain.Entities;
using PMO360.Domain.Enums;

namespace PMO360.Domain.Rules;

/// <summary>
/// What the update form submits (screen 3). Kept in the domain because the business rules in
/// section 5.2 are checked against it, and those rules have to hold wherever the submission
/// comes from — the Blazor form today, a bulk import at migration, or anything added later.
/// </summary>
public sealed class UpdateSubmission
{
    public int ProjectId { get; set; }
    public DateOnly UpdateDate { get; set; }
    public ProjectStatus Status { get; set; }
    public int ProgressPercent { get; set; }
    public int? PhaseId { get; set; }
    public int? MilestoneId { get; set; }
    public string? KeyUpdate { get; set; }
    public string? Achievement { get; set; }
    public string? NextAction { get; set; }
    public PersonRef? NextActionOwner { get; set; }
    public DateOnly? NextActionDueDate { get; set; }
    public bool AttentionRequired { get; set; }
    public string? AttentionReason { get; set; }
}

public sealed record ValidationFailure(string Field, string Rule, string Message);
