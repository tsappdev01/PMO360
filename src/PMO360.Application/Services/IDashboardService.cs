using PMO360.Application.Dtos;

namespace PMO360.Application.Services;

public interface IDashboardService
{
    /// <summary>
    /// FR-16, FR-17, FR-20, AC-04. Every figure is computed from the register in the same
    /// query pass, so the indicators always reconcile to the table beneath them.
    /// </summary>
    Task<DashboardModel> GetDashboardAsync(PortfolioFilter filter, CancellationToken cancellationToken = default);
}
