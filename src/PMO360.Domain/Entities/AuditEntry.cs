namespace PMO360.Domain.Entities;

/// <summary>
/// Non-functional requirement "Auditability": every change attributable to a named user with
/// date and time. Written by the DbContext from its change tracker, so a write cannot bypass it.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public string EntityName { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;
    public string? UserObjectId { get; set; }
    public DateTimeOffset OccurredOn { get; set; }

    /// <summary>Changed columns only, as JSON: {"Column":{"old":…,"new":…}}.</summary>
    public string? Changes { get; set; }
}
