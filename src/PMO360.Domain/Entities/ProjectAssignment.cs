using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>
/// Row-level visibility (AC-10). A project manager or consultant reaches a project only through
/// an assignment; management and the PMO reach every project through their role instead.
/// </summary>
public class ProjectAssignment
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public PersonRef Person { get; set; } = new();
    public AssignmentRole Role { get; set; }

    public DateTimeOffset AssignedOn { get; set; }
    public PersonRef AssignedBy { get; set; } = new();

    /// <summary>
    /// Guest consultant access is reviewed at the end of an engagement (section 6). Ending an
    /// assignment removes the access without removing the record of who held it.
    /// </summary>
    public DateOnly? EndsOn { get; set; }

    public bool IsCurrentAt(DateOnly today) => EndsOn is null || EndsOn.Value >= today;
}
