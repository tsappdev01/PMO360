using PMO360.Domain.Entities;
using PMO360.Domain.Enums;

namespace PMO360.Application.Services;

public sealed record MilestoneInput(
    int? Id,
    string Name,
    DateOnly PlannedDate,
    DateOnly? BaselineDate,
    DateOnly? ActualDate,
    MilestoneStatus Status,
    int CompletionPercent,
    PersonRef Owner);

public interface IMilestoneService
{
    Task<IReadOnlyList<Milestone>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-06. Adds or amends a milestone. The baseline date is captured on first save and is
    /// not moved afterwards, so slippage keeps something to measure against.
    /// </summary>
    Task<Milestone> SaveAsync(int projectId, MilestoneInput input, CancellationToken cancellationToken = default);

    /// <summary>BR-07. A milestone that is no longer wanted is cancelled, not deleted.</summary>
    Task CancelAsync(int milestoneId, CancellationToken cancellationToken = default);
}
