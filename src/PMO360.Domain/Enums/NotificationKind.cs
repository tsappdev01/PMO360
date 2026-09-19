namespace PMO360.Domain.Enums;

/// <summary>Section 5.3. The value is the BRD reference so the log reads against the document.</summary>
public enum NotificationKind
{
    StatusChangedToAtRisk = 1,       // WF-01
    StatusChangedToDelayed = 2,      // WF-02
    ManagementAttentionFlagged = 3,  // WF-03
    MilestoneDueOrOverdue = 4,       // WF-04
    UpdateOverdue = 5,               // WF-05
    UpdateOverdueEscalation = 6,     // WF-05 (second stage)
    HighSeverityRiskRaised = 7,      // WF-06
    PortfolioDigest = 8,             // WF-07
    ProjectCompleted = 9             // WF-08
}
