namespace PMO360.Application.Options;

/// <summary>Section 5.3. Recipients, intervals and thresholds "confirmed at design".</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Master switch. Off in non-production so a test run cannot mail the business.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The mailbox notifications are sent from, e.g. pmo@dubaiinvestments.com.</summary>
    public string SenderAddress { get; set; } = string.Empty;

    /// <summary>The PMO team address, on every notification in section 5.3.</summary>
    public string PmoAddress { get; set; } = string.Empty;

    /// <summary>WF-03 / WF-07. The management distribution list.</summary>
    public string ManagementDistributionList { get; set; } = string.Empty;

    /// <summary>WF-07. Day and time of the scheduled portfolio digest. BRD: [Monday 08:00].</summary>
    public DayOfWeek DigestDay { get; set; } = DayOfWeek.Monday;

    public TimeOnly DigestTime { get; set; } = new(8, 0);

    /// <summary>Time of day the reminder sweep runs, before the working day starts.</summary>
    public TimeOnly ReminderSweepTime { get; set; } = new(7, 0);

    /// <summary>
    /// Absolute base URL of the portal, used to build the deep link carried by every
    /// notification ("Every notification links directly to the relevant record").
    /// </summary>
    public string PortalBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// When set, every notification goes here instead of the real recipients. For UAT: the flows
    /// can be exercised end to end without reaching the business.
    /// </summary>
    public string? RedirectAllTo { get; set; }
}
