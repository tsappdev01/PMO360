using PMO360.Domain.Enums;

namespace PMO360.Application.Dtos;

/// <summary>FR-16. The six summary indicators on the management dashboard.</summary>
public sealed record DashboardSummary(
    int ActiveProjects,
    int CompletedThisFinancialYear,
    int OnTrack,
    int AtRisk,
    int AtRiskRaisedThisWeek,
    int Delayed,
    int AverageProgress,
    int NeedsAttention)
{
    /// <summary>Share of the active portfolio that is On Track, as shown under the indicator.</summary>
    public int OnTrackPercent =>
        ActiveProjects == 0 ? 0 : (int)Math.Round(OnTrack * 100.0 / ActiveProjects, MidpointRounding.AwayFromZero);
}

/// <summary>FR-17. One row of the portfolio table.</summary>
public sealed record PortfolioRow(
    int ProjectId,
    string ProjectCode,
    string Name,
    string OwnerName,
    string? ConsultantName,
    ProjectStatus Status,
    int ProgressPercent,
    string? PhaseName,
    string? NextMilestoneName,
    DateOnly? NextMilestoneDue,
    bool NextMilestoneOverdue,
    DateOnly? LastUpdateDate,
    bool IsNotReported,
    bool AttentionRequired,
    int OpenHighRisks,
    ProjectPriority Priority,
    string ReportingEntityName);

/// <summary>Left-hand card on the dashboard: counts by status, drawn as proportional bars.</summary>
public sealed record StatusBar(ProjectStatus Status, string Label, int Count, int PercentOfActive);

/// <summary>FR-20. Milestones due in the rolling window.</summary>
public sealed record UpcomingMilestone(
    int MilestoneId,
    int ProjectId,
    string ProjectName,
    string MilestoneName,
    DateOnly PlannedDate,
    string OwnerName,
    bool IsOverdue);

/// <summary>FR-20. The items flagged for management attention, with the reason given by the manager.</summary>
public sealed record AttentionItem(
    int ProjectId,
    string ProjectCode,
    string ProjectName,
    ProjectStatus Status,
    string Reason,
    DateOnly? RaisedOn);

public sealed record DashboardModel(
    DashboardSummary Summary,
    IReadOnlyList<PortfolioRow> Portfolio,
    int PortfolioTotal,
    IReadOnlyList<StatusBar> StatusBars,
    IReadOnlyList<UpcomingMilestone> UpcomingMilestones,
    IReadOnlyList<AttentionItem> AttentionItems,
    DateOnly AsAt,
    int UpcomingWindowDays);
