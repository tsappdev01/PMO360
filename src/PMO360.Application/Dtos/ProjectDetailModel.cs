using PMO360.Domain.Entities;

namespace PMO360.Application.Dtos;

/// <summary>FR-19 / AC-05. Everything the project detail page shows, in one round trip.</summary>
public sealed record ProjectDetailModel(
    Project Project,
    IReadOnlyList<Milestone> Milestones,
    IReadOnlyList<RiskIssue> RisksAndIssues,
    IReadOnlyList<ProjectUpdate> History,
    IReadOnlyList<Attachment> Attachments,
    Milestone? NextMilestone,
    int MilestonesComplete,
    bool IsNotReported,
    DateOnly AsAt);
