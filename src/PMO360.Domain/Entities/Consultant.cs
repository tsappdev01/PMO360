namespace PMO360.Domain.Entities;

/// <summary>Consultant or vendor, from a controlled list (FR-04).</summary>
public class Consultant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
