namespace PMO360.Domain.Enums;

/// <summary>
/// BR-07. Nothing is deleted. A record is closed or cancelled and stays available to reporting.
/// There is deliberately no delete path in the data access layer.
/// </summary>
public enum RecordState
{
    Active = 1,
    Closed = 2,
    Cancelled = 3
}
