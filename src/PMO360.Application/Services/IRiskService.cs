using PMO360.Domain.Entities;
using PMO360.Domain.Enums;

namespace PMO360.Application.Services;

public sealed record RiskInput(
    int? Id,
    RiskType Type,
    string Description,
    RiskSeverity Severity,
    PersonRef Owner,
    string? Mitigation,
    DateOnly? DueDate);

public interface IRiskService
{
    Task<IReadOnlyList<RiskIssue>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>FR-09, WF-06. Raising a High severity item notifies the PMO and the Project Owner.</summary>
    Task<RiskIssue> SaveAsync(int projectId, RiskInput input, CancellationToken cancellationToken = default);

    /// <summary>FR-10. Closing keeps the record and requires the closure note.</summary>
    Task CloseAsync(int riskId, string closureNote, CancellationToken cancellationToken = default);
}
