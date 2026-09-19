namespace PMO360.Domain.Entities;

/// <summary>
/// Section 1: Dubai Investments PJSC and its reporting entities. Reference data — maintained by
/// script in <c>db/</c>, never seeded from code, so the list changes without a redeploy.
/// </summary>
public class ReportingEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
