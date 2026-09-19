namespace PMO360.Domain.Entities;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
