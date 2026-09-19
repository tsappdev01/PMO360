namespace PMO360.Domain.Entities;

/// <summary>
/// A person taken from the corporate directory (FR-04). Stored on the record rather than
/// looked up at read time, so a historical update still shows who held the role at the time.
/// <see cref="ObjectId"/> is the Entra ID object id and is null for a label-only owner
/// such as "Test Lead" or a vendor team.
/// </summary>
public sealed class PersonRef
{
    public string? ObjectId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }

    public PersonRef() { }

    public PersonRef(string displayName, string? objectId = null, string? email = null)
    {
        DisplayName = displayName;
        ObjectId = objectId;
        Email = email;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(DisplayName);

    public override string ToString() => DisplayName;
}
