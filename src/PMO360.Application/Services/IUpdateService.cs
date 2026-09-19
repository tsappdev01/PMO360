using PMO360.Domain.Entities;
using PMO360.Domain.Rules;

namespace PMO360.Application.Services;

public sealed record SubmitUpdateResult(
    bool Succeeded,
    int? UpdateId,
    IReadOnlyList<ValidationFailure> Failures)
{
    public static SubmitUpdateResult Rejected(IReadOnlyList<ValidationFailure> failures) =>
        new(false, null, failures);

    public static SubmitUpdateResult Accepted(int updateId) =>
        new(true, updateId, Array.Empty<ValidationFailure>());
}

public interface IUpdateService
{
    /// <summary>
    /// FR-15. The form opens on the values of the previous update, so the manager edits rather
    /// than retypes. Returns the manager's own draft first if one exists, otherwise the last
    /// submitted update, otherwise the project's current position.
    /// </summary>
    Task<UpdateSubmission> GetFormDefaultsAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-12, FR-22, AC-03, AC-04. Validates against section 5.2, then in one transaction:
    /// writes the dated history entry, updates the project record, stamps the last update date
    /// and the submitting user, and queues the notifications the change earns.
    /// </summary>
    Task<SubmitUpdateResult> SubmitAsync(UpdateSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>"Save as draft" on the update form. A draft is private to its author and is not history.</summary>
    Task SaveDraftAsync(UpdateSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>FR-14. Submitted updates in date order, newest first. Drafts are never included.</summary>
    Task<IReadOnlyList<ProjectUpdate>> GetHistoryAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>The recent submissions across the portfolio, for the Updates page.</summary>
    Task<IReadOnlyList<ProjectUpdate>> GetRecentAsync(int take = 50, CancellationToken cancellationToken = default);
}
