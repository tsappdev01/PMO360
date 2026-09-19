namespace PMO360.Application.Options;

/// <summary>
/// The values the BRD leaves in square brackets, to be confirmed before sign-off. They are
/// configuration, not constants, so confirming one is an App Service setting rather than a build.
/// </summary>
public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Displayed name of the application.</summary>
    public string ApplicationName { get; set; } = "PMO360";

    public string Tagline { get; set; } = "Project Management Office Portal";

    /// <summary>Business time zone; "today" is evaluated in it. The Gulf has no daylight saving.</summary>
    public string TimeZone { get; set; } = "Asia/Dubai";

    /// <summary>BR-06 / FR-23. The agreed reporting cycle, in days. BRD: [7].</summary>
    public int UpdateCycleDays { get; set; } = 7;

    /// <summary>WF-05 second stage: escalation to the Project Owner. BRD: [14].</summary>
    public int UpdateEscalationDays { get; set; } = 14;

    /// <summary>FR-20 / WF-04. Rolling window for upcoming milestones, in days. BRD: [7] for alerts, 14 on the dashboard.</summary>
    public int MilestoneReminderDays { get; set; } = 7;

    public int UpcomingMilestoneWindowDays { get; set; } = 14;

    /// <summary>Non-functional "Capacity": page size used by the list views, which are never unbounded.</summary>
    public int ListPageSize { get; set; } = 50;

    /// <summary>Number of portfolio rows on the dashboard before "View all projects".</summary>
    public int DashboardProjectRows { get; set; } = 8;

    /// <summary>
    /// First month of the financial year, for the "completed this FY" indicator. Dubai Investments
    /// reports on the calendar year, so January; change it here rather than in the query.
    /// </summary>
    public int FinancialYearStartMonth { get; set; } = 1;
}
