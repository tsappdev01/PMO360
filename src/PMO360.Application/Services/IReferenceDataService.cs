using PMO360.Domain.Entities;

namespace PMO360.Application.Services;

/// <summary>
/// The controlled lists behind every choice field (section 4.3). Reference data is maintained by
/// script in <c>db/</c>, so this service only reads.
/// </summary>
public interface IReferenceDataService
{
    Task<IReadOnlyList<ReportingEntity>> GetEntitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Phase>> GetPhasesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Consultant>> GetConsultantsAsync(CancellationToken cancellationToken = default);
}
