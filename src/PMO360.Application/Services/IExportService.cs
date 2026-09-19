using PMO360.Application.Dtos;

namespace PMO360.Application.Services;

/// <summary>FR-24. Portfolio to Excel for meeting papers; the one-page status report is printed from the portal.</summary>
public interface IExportService
{
    Task<byte[]> ExportPortfolioToExcelAsync(PortfolioFilter filter, CancellationToken cancellationToken = default);
}
