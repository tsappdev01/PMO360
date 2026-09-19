using PMO360.Application.Dtos;
using PMO360.Domain.Entities;
using PMO360.Domain.Enums;

namespace PMO360.Application.Services;

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    int ReportingEntityId,
    int DepartmentId,
    int? ConsultantId,
    PersonRef ProjectOwner,
    PersonRef BusinessOwner,
    PersonRef ProjectManager,
    PersonRef Sponsor,
    ProjectPriority Priority,
    DateOnly StartDate,
    DateOnly TargetCompletionDate,
    int? CurrentPhaseId);

public sealed record PortfolioPage(IReadOnlyList<PortfolioRow> Rows, int Total);

public interface IProjectService
{
    /// <summary>
    /// FR-17, FR-21, FR-23, FR-25. Scoped to what the signed-in user may see. Rows and the
    /// total come back together: the procedure carries the count on every row, so paging does
    /// not cost a second pass over the register.
    /// </summary>
    Task<PortfolioPage> GetPortfolioAsync(
        PortfolioFilter filter, int skip = 0, int? take = null, CancellationToken cancellationToken = default);

    /// <summary>FR-19. Returns null when the project does not exist or is out of the user's scope.</summary>
    Task<ProjectDetailModel?> GetDetailAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>FR-05, AC-01. PMO only. Allocates the next unique project code.</summary>
    Task<Project> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>FR-05, BR-07. PMO only. The project leaves the default views and is retained.</summary>
    Task CloseAsync(int projectId, string closureNote, CancellationToken cancellationToken = default);

    Task ReopenAsync(int projectId, string reason, CancellationToken cancellationToken = default);

    /// <summary>PMO maintenance of the attributes the update form does not carry (owners, dates, consultant).</summary>
    Task UpdateParticularsAsync(int projectId, CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverdueMilestoneRow>> GetOverdueMilestonesAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskRow>> GetRisksAsync(
        PortfolioFilter filter, bool openHighOnly = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotReportedRow>> GetNotReportedAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default);

    Task<ReportingCompliance> GetReportingComplianceAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default);
}
