using PMO360.Domain.Enums;

namespace PMO360.Application.Dtos;

/// <summary>FR-21. Filtering by entity, department, status, priority, owner and consultant.</summary>
public sealed record PortfolioFilter
{
    public int? ReportingEntityId { get; set; }
    public int? DepartmentId { get; set; }
    public ProjectStatus? Status { get; set; }
    public ProjectPriority? Priority { get; set; }

    /// <summary>Entra object id of the project owner.</summary>
    public string? OwnerObjectId { get; set; }

    public int? ConsultantId { get; set; }

    /// <summary>FR-25. Matches project name, project code, owner and consultant.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Which slice of the register to show — see <see cref="PortfolioView"/>.</summary>
    public PortfolioView View { get; set; } = PortfolioView.Active;

    /// <summary>FR-25. Restricts to projects the signed-in user is assigned to.</summary>
    public bool MyProjectsOnly { get; set; }

    public bool IsEmpty =>
        ReportingEntityId is null && DepartmentId is null && Status is null && Priority is null
        && OwnerObjectId is null && ConsultantId is null
        && string.IsNullOrWhiteSpace(SearchTerm) && !MyProjectsOnly
        && View == PortfolioView.Active;
}

/// <summary>FR-23. The standard views, as named in the BRD.</summary>
public enum PortfolioView
{
    Active = 0,
    AtRiskAndDelayed = 1,
    OverdueMilestones = 2,
    OpenHighRisks = 3,
    NotReported = 4,
    Completed = 5,
    All = 6
}
