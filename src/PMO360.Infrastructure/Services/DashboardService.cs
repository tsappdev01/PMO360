using Microsoft.Extensions.Options;
using PMO360.Application.Abstractions;
using PMO360.Application.Dtos;
using PMO360.Application.Options;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

/// <summary>
/// AC-04: "every summary figure reconciles to the underlying list data". usp_Dashboard_Get
/// computes the indicators, the status bars, the upcoming milestones and the attention list
/// from one scoped set of projects, and the portfolio table beneath them comes from the same
/// procedure the Projects page uses — so there is no second place for a number to come from.
/// </summary>
public sealed class DashboardService(
    ISqlConnectionFactory connections,
    IProjectService projects,
    IProjectAccessService access,
    ICurrentUser user,
    IClock clock,
    IOptions<PortalOptions> portalOptions) : IDashboardService
{
    private readonly PortalOptions _portal = portalOptions.Value;

    public async Task<DashboardModel> GetDashboardAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        DashboardSummary summary;
        List<StatusBar> bars;
        List<UpcomingMilestone> upcoming;
        List<AttentionItem> attention;

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Dashboard_Get")
                .With("UserObjectId", user.ObjectId)
                .With("CanSeeAll", access.CanSeeWholePortfolio)
                .With("Today", today)
                .With("UpdateCycleDays", _portal.UpdateCycleDays)
                .With("UpcomingWindowDays", _portal.UpcomingMilestoneWindowDays)
                .With("FinancialYearStartMonth", (byte)_portal.FinancialYearStartMonth)
                .With("ReportingEntityId", filter.ReportingEntityId)
                .With("DepartmentId", filter.DepartmentId)
                .With("ConsultantId", filter.ConsultantId)
                .With("OwnerObjectId", filter.OwnerObjectId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // 1 — the six indicators (FR-16).
            summary = await reader.ReadAsync(cancellationToken)
                ? new DashboardSummary(
                    ActiveProjects: reader.GetInt("ActiveProjects"),
                    CompletedThisFinancialYear: reader.GetInt("CompletedThisFinancialYear"),
                    OnTrack: reader.GetInt("OnTrack"),
                    AtRisk: reader.GetInt("AtRisk"),
                    AtRiskRaisedThisWeek: reader.GetInt("AtRiskRaisedThisWeek"),
                    Delayed: reader.GetInt("Delayed"),
                    AverageProgress: reader.GetInt("AverageProgress"),
                    NeedsAttention: reader.GetInt("NeedsAttention"))
                : new DashboardSummary(0, 0, 0, 0, 0, 0, 0, 0);

            // 2 — portfolio by status.
            await reader.NextResultAsync(cancellationToken);
            bars = await reader.ReadAllAsync(r =>
            {
                var status = r.GetEnum<ProjectStatus>("StatusId");
                var label = status == ProjectStatus.Completed
                    ? $"Completed {FinancialYearLabel(today)}"
                    : r.GetString("StatusName");

                return new StatusBar(status, label, r.GetInt("ProjectCount"), r.GetInt("PercentOfActive"));
            }, cancellationToken);

            // 3 — milestones due in the rolling window (FR-20).
            await reader.NextResultAsync(cancellationToken);
            upcoming = await reader.ReadAllAsync(r => new UpcomingMilestone(
                r.GetInt("MilestoneId"),
                r.GetInt("ProjectId"),
                r.GetString("ProjectName"),
                r.GetString("MilestoneName"),
                r.GetDate("PlannedDate"),
                r.GetString("OwnerName"),
                r.GetBool("IsOverdue")), cancellationToken);

            // 4 — items needing a decision (FR-20).
            await reader.NextResultAsync(cancellationToken);
            attention = await reader.ReadAllAsync(r => new AttentionItem(
                r.GetInt("ProjectId"),
                r.GetString("ProjectCode"),
                r.GetString("ProjectName"),
                r.GetEnum<ProjectStatus>("StatusId"),
                r.GetString("Reason"),
                r.GetDateOrNull("RaisedOn")), cancellationToken);
        }

        var page = await projects.GetPortfolioAsync(
            filter, take: _portal.DashboardProjectRows, cancellationToken: cancellationToken);

        return new DashboardModel(
            summary, page.Rows, page.Total, bars, upcoming, attention, today, _portal.UpcomingMilestoneWindowDays);
    }

    private string FinancialYearLabel(DateOnly today)
    {
        var start = new DateOnly(today.Year, _portal.FinancialYearStartMonth, 1);
        if (start > today)
        {
            start = start.AddYears(-1);
        }

        var end = start.AddYears(1).AddDays(-1);
        return $"FY{end:yy}";
    }
}
