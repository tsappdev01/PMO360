using PMO360.Domain.Enums;

namespace PMO360.Application.Dtos;

public sealed record OverdueMilestoneRow(
    int MilestoneId,
    int ProjectId,
    string ProjectCode,
    string ProjectName,
    string MilestoneName,
    DateOnly PlannedDate,
    DateOnly? BaselineDate,
    int DaysOverdue,
    int CompletionPercent,
    string OwnerName);

public sealed record RiskRow(
    int RiskId,
    int ProjectId,
    string ProjectCode,
    string ProjectName,
    RiskType Type,
    string Description,
    RiskSeverity Severity,
    string OwnerName,
    string? Mitigation,
    DateOnly? DueDate,
    RiskStatus Status,
    bool IsOverdue);

public sealed record NotReportedRow(
    int ProjectId,
    string ProjectCode,
    string ProjectName,
    string OwnerName,
    ProjectStatus Status,
    DateOnly? LastUpdateDate,
    int DaysSinceUpdate);

/// <summary>Reporting compliance (section 5.4): how much of the portfolio reported inside the cycle.</summary>
public sealed record ReportingCompliance(int ActiveProjects, int ReportedInCycle, int NotReported)
{
    public int CompliancePercent =>
        ActiveProjects == 0 ? 100 : (int)Math.Round(ReportedInCycle * 100.0 / ActiveProjects, MidpointRounding.AwayFromZero);
}
