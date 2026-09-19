namespace PMO360.Application.Services;

/// <summary>
/// Section 5.3. Replaces the Power Automate flows of the BRD's low-code design: the same
/// triggers, recipients and escalations, raised by the portal and by a scheduled sweep.
///
/// Every send is claimed in pmo.NotificationLog against a key that identifies the occasion, so
/// a restart, a retry or a second App Service instance cannot mail the business twice.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// WF-01, WF-02, WF-03, WF-08 — decided from what the submission changed. The update has
    /// already been written, so this reads the record rather than being told about it.
    /// </summary>
    Task OnUpdateSubmittedAsync(int projectId, int updateId, CancellationToken cancellationToken = default);

    /// <summary>WF-06. Raised at High severity, or raised to High from something lower.</summary>
    Task OnHighSeverityRiskRaisedAsync(int projectId, int riskId, CancellationToken cancellationToken = default);

    /// <summary>WF-04 and WF-05. Idempotent for a given day — safe to run more than once.</summary>
    Task RunReminderSweepAsync(CancellationToken cancellationToken = default);

    /// <summary>WF-07. The scheduled portfolio digest to management.</summary>
    Task SendPortfolioDigestAsync(CancellationToken cancellationToken = default);
}
