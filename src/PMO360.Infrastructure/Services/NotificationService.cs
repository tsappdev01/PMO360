using System.Data;
using System.Net;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

/// <summary>
/// Section 5.3, WF-01 to WF-08. Every send is claimed in pmo.NotificationLog against a key that
/// identifies the occasion — the update, the risk, or the project and the day — so a retry, a
/// restart or a second App Service instance running the sweep cannot mail the business twice.
/// </summary>
public sealed class NotificationService(
    ISqlConnectionFactory connections,
    IEmailSender email,
    IClock clock,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<PortalOptions> portalOptions,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly NotificationOptions _options = notificationOptions.Value;
    private readonly PortalOptions _portal = portalOptions.Value;

    public async Task OnUpdateSubmittedAsync(
        int projectId, int updateId, CancellationToken cancellationToken = default)
    {
        UpdateContext? context;

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Notification_GetUpdateContext")
                .With("UpdateId", updateId);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            context = await reader.ReadAsync(cancellationToken) ? ReadUpdateContext(reader) : null;
        }

        if (context is null)
        {
            logger.LogWarning("Update {UpdateId} was not found when raising its notifications.", updateId);
            return;
        }

        var link = ProjectLink(context.ProjectId);

        // WF-01 / WF-02. Only a change into the status counts: a project that was already
        // At Risk last week does not re-alert every time it is updated.
        if (context.Status != context.PreviousStatus)
        {
            if (context.Status == ProjectStatus.AtRisk)
            {
                await SendAsync(
                    NotificationKind.StatusChangedToAtRisk,
                    $"WF01:{updateId}",
                    [_options.PmoAddress, context.ProjectOwnerEmail],
                    null,
                    $"At Risk — {context.ProjectCode} {context.ProjectName}",
                    StatusChangeBody(context, link, "is now At Risk"),
                    context.ProjectId,
                    cancellationToken: cancellationToken);
            }
            else if (context.Status == ProjectStatus.Delayed)
            {
                // Escalation: the sponsor is added, because a delay has already happened.
                await SendAsync(
                    NotificationKind.StatusChangedToDelayed,
                    $"WF02:{updateId}",
                    [_options.PmoAddress, context.ProjectOwnerEmail, context.SponsorEmail],
                    null,
                    $"Delayed — {context.ProjectCode} {context.ProjectName}",
                    StatusChangeBody(context, link, "is now Delayed"),
                    context.ProjectId,
                    cancellationToken: cancellationToken);
            }
            else if (context.Status == ProjectStatus.Completed)
            {
                // WF-08. The PMO begins the closure checklist.
                await SendAsync(
                    NotificationKind.ProjectCompleted,
                    $"WF08:{updateId}",
                    [_options.PmoAddress],
                    [context.ProjectOwnerEmail],
                    $"Completed — {context.ProjectCode} {context.ProjectName}",
                    StatusChangeBody(context, link, "has been reported as Completed"),
                    context.ProjectId,
                    cancellationToken: cancellationToken);
            }
        }

        // WF-03. Management attention, with the stated reason.
        if (context.AttentionRequired)
        {
            await SendAsync(
                NotificationKind.ManagementAttentionFlagged,
                $"WF03:{updateId}",
                [_options.PmoAddress, _options.ManagementDistributionList],
                [context.ProjectOwnerEmail],
                $"Management attention — {context.ProjectCode} {context.ProjectName}",
                AttentionBody(context, link),
                context.ProjectId,
                cancellationToken: cancellationToken);
        }
    }

    public async Task OnHighSeverityRiskRaisedAsync(
        int projectId, int riskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Notification_GetRiskContext")
            .With("RiskId", riskId);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        var projectCode = reader.GetString("ProjectCode");
        var projectName = reader.GetString("ProjectName");
        var typeName = reader.GetString("TypeName");
        var description = reader.GetString("Description");
        var owner = reader.GetString("OwnerDisplayName");
        var mitigation = reader.GetStringOrNull("Mitigation");
        var due = reader.GetDateOrNull("DueDate");
        var raisedBy = reader.GetString("RaisedByDisplayName");
        var pmoOwnerEmail = reader.GetStringOrNull("ProjectOwnerEmail");

        var body = new StringBuilder()
            .Append(Paragraph($"A <strong>High severity {typeName.ToLowerInvariant()}</strong> has been raised on "
                              + $"{Encode(projectCode)} {Encode(projectName)} by {Encode(raisedBy)}."))
            .Append(Field("Description", description))
            .Append(Field("Owner", owner))
            .Append(mitigation is null ? string.Empty : Field("Mitigation", mitigation))
            .Append(due is null ? string.Empty : Field("Due", due.Value.ToString("dd-MMM-yyyy")))
            .Append(LinkParagraph(ProjectLink(projectId), "Open the project"))
            .ToString();

        await SendAsync(
            NotificationKind.HighSeverityRiskRaised,
            $"WF06:{riskId}",
            [_options.PmoAddress, pmoOwnerEmail],
            null,
            $"High severity {typeName.ToLowerInvariant()} — {projectCode} {projectName}",
            body,
            projectId,
            riskIssueId: riskId,
            cancellationToken: cancellationToken);
    }

    public async Task RunReminderSweepAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        // WF-04. Milestones due inside the window, and anything already overdue — overdue
        // notices repeat daily, which the dedupe key allows by carrying the date.
        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Notification_GetMilestoneReminders")
                .With("Today", today)
                .With("ReminderDays", _portal.MilestoneReminderDays);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var milestones = await reader.ReadAllAsync(r => new
            {
                MilestoneId = r.GetInt("MilestoneId"),
                MilestoneName = r.GetString("MilestoneName"),
                PlannedDate = r.GetDate("PlannedDate"),
                OwnerEmail = r.GetStringOrNull("OwnerEmail"),
                ProjectId = r.GetInt("ProjectId"),
                ProjectCode = r.GetString("ProjectCode"),
                ProjectName = r.GetString("ProjectName"),
                ManagerEmail = r.GetStringOrNull("ProjectManagerEmail"),
                OwnerName = r.GetString("OwnerDisplayName"),
                IsOverdue = r.GetBool("IsOverdue")
            }, cancellationToken);

            foreach (var milestone in milestones)
            {
                var days = milestone.PlannedDate.DayNumber - today.DayNumber;
                var when = milestone.IsOverdue
                    ? $"was due on {milestone.PlannedDate:dd-MMM-yyyy} and is overdue by {-days} day{(days == -1 ? "" : "s")}"
                    : days == 0
                        ? "is due today"
                        : $"is due on {milestone.PlannedDate:dd-MMM-yyyy}, in {days} day{(days == 1 ? "" : "s")}";

                var body = new StringBuilder()
                    .Append(Paragraph($"Milestone <strong>{Encode(milestone.MilestoneName)}</strong> on "
                                      + $"{Encode(milestone.ProjectCode)} {Encode(milestone.ProjectName)} {when}."))
                    .Append(Field("Owner", milestone.OwnerName))
                    .Append(LinkParagraph(ProjectLink(milestone.ProjectId), "Open the project"))
                    .ToString();

                await SendAsync(
                    NotificationKind.MilestoneDueOrOverdue,
                    // Overdue repeats daily; a reminder ahead of the date goes once, because the
                    // key is the same on each day until the milestone is passed.
                    milestone.IsOverdue
                        ? $"WF04:{milestone.MilestoneId}:{today:yyyyMMdd}"
                        : $"WF04:{milestone.MilestoneId}:due",
                    [milestone.OwnerEmail],
                    [milestone.ManagerEmail],
                    (milestone.IsOverdue ? "Overdue milestone — " : "Milestone due — ")
                        + $"{milestone.ProjectCode} {milestone.ProjectName}",
                    body,
                    milestone.ProjectId,
                    milestoneId: milestone.MilestoneId,
                    cancellationToken: cancellationToken);
            }
        }

        // WF-05. Reminder to the project manager, then escalation to the Project Owner.
        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Notification_GetUpdateReminders")
                .With("Today", today)
                .With("ReminderDays", _portal.UpdateCycleDays)
                .With("EscalationDays", _portal.UpdateEscalationDays);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var overdue = await reader.ReadAllAsync(r => new
            {
                ProjectId = r.GetInt("ProjectId"),
                ProjectCode = r.GetString("ProjectCode"),
                ProjectName = r.GetString("ProjectName"),
                ManagerName = r.GetString("ProjectManagerDisplayName"),
                ManagerEmail = r.GetStringOrNull("ProjectManagerEmail"),
                OwnerEmail = r.GetStringOrNull("ProjectOwnerEmail"),
                LastUpdate = r.GetDateOrNull("LastUpdateDate"),
                Days = r.GetInt("DaysSinceUpdate"),
                Stage = r.GetInt("Stage")
            }, cancellationToken);

            foreach (var project in overdue)
            {
                var last = project.LastUpdate is { } date
                    ? $"The last update was on {date:dd-MMM-yyyy}, {project.Days} days ago."
                    : $"The project has not been updated since it was created, {project.Days} days ago.";

                var body = new StringBuilder()
                    .Append(Paragraph($"{Encode(project.ProjectCode)} {Encode(project.ProjectName)} is reported as "
                                      + "<strong>Not Reported</strong> because it has no update inside the agreed cycle."))
                    .Append(Paragraph(last))
                    .Append(LinkParagraph(UpdateLink(project.ProjectId), "Submit the update"))
                    .ToString();

                var escalating = project.Stage == 2;

                await SendAsync(
                    escalating ? NotificationKind.UpdateOverdueEscalation : NotificationKind.UpdateOverdue,
                    $"WF05:{project.Stage}:{project.ProjectId}:{today:yyyyMMdd}",
                    escalating
                        ? [project.OwnerEmail, _options.PmoAddress]
                        : [project.ManagerEmail],
                    escalating ? [project.ManagerEmail] : null,
                    (escalating ? "Escalation: no project update — " : "Reminder: project update due — ")
                        + $"{project.ProjectCode} {project.ProjectName}",
                    body,
                    project.ProjectId,
                    cancellationToken: cancellationToken);
            }
        }
    }

    public async Task SendPortfolioDigestAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var body = new StringBuilder();

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Notification_GetDigest")
                .With("Today", today)
                .With("UpdateCycleDays", _portal.UpdateCycleDays)
                .With("UpcomingWindowDays", _portal.MilestoneReminderDays);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            body.Append($"<h2 style=\"font-family:Segoe UI,Arial,sans-serif\">Portfolio position as at {today:dd-MMM-yyyy}</h2>");

            // 1 — counts by status.
            body.Append("<h3 style=\"font-family:Segoe UI,Arial,sans-serif\">By status</h3><ul>");
            while (await reader.ReadAsync(cancellationToken))
            {
                body.Append($"<li>{Encode(reader.GetString("StatusName"))}: <strong>{reader.GetInt("ProjectCount")}</strong></li>");
            }

            body.Append("</ul>");

            // 2 — the exceptions.
            await reader.NextResultAsync(cancellationToken);
            var exceptions = await reader.ReadAllAsync(r => new
            {
                Code = r.GetString("ProjectCode"),
                Name = r.GetString("ProjectName"),
                Status = r.GetString("StatusName"),
                Progress = r.GetInt("ProgressPercent"),
                Owner = r.GetString("ProjectOwnerDisplayName"),
                Note = r.GetStringOrNull("Note"),
                ProjectId = r.GetInt("ProjectId")
            }, cancellationToken);

            body.Append("<h3 style=\"font-family:Segoe UI,Arial,sans-serif\">Exceptions</h3>");
            if (exceptions.Count == 0)
            {
                body.Append(Paragraph("No project is at risk, delayed or flagged for attention."));
            }
            else
            {
                body.Append("<ul>");
                foreach (var item in exceptions)
                {
                    body.Append($"<li><a href=\"{ProjectLink(item.ProjectId)}\">{Encode(item.Code)} {Encode(item.Name)}</a> — "
                                + $"{Encode(item.Status)}, {item.Progress}%, owner {Encode(item.Owner)}"
                                + (string.IsNullOrWhiteSpace(item.Note) ? "" : $"<br/><em>{Encode(item.Note)}</em>")
                                + "</li>");
                }

                body.Append("</ul>");
            }

            // 3 — upcoming and overdue milestones.
            await reader.NextResultAsync(cancellationToken);
            var milestones = await reader.ReadAllAsync(r => new
            {
                Project = r.GetString("ProjectName"),
                Milestone = r.GetString("MilestoneName"),
                Planned = r.GetDate("PlannedDate"),
                Owner = r.GetString("OwnerDisplayName"),
                Overdue = r.GetBool("IsOverdue")
            }, cancellationToken);

            body.Append("<h3 style=\"font-family:Segoe UI,Arial,sans-serif\">Milestones</h3>");
            if (milestones.Count == 0)
            {
                body.Append(Paragraph("No milestone is due in the coming week."));
            }
            else
            {
                body.Append("<ul>");
                foreach (var m in milestones)
                {
                    body.Append($"<li>{m.Planned:dd-MMM} — {Encode(m.Milestone)} ({Encode(m.Project)}, {Encode(m.Owner)})"
                                + (m.Overdue ? " <strong>overdue</strong>" : "") + "</li>");
                }

                body.Append("</ul>");
            }

            // 4 — reporting compliance.
            await reader.NextResultAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var active = reader.GetInt("ActiveProjects");
                var reported = reader.GetInt("ReportedInCycle");
                var notReported = reader.GetInt("NotReported");
                var percent = active == 0 ? 100 : (int)Math.Round(reported * 100.0 / active);

                body.Append(Paragraph($"Reporting compliance: <strong>{percent}%</strong> "
                                      + $"({reported} of {active} updated within {_portal.UpdateCycleDays} days; "
                                      + $"{notReported} not reported)."));
            }
        }

        body.Append(LinkParagraph(DashboardLink(), "Open the dashboard"));

        await SendAsync(
            NotificationKind.PortfolioDigest,
            $"WF07:{today:yyyyMMdd}",
            [_options.ManagementDistributionList],
            [_options.PmoAddress],
            $"Portfolio digest — {today:dd-MMM-yyyy}",
            body.ToString(),
            projectId: null,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Claims the occasion, sends, then records how it went. A claim that is refused means
    /// somebody has already sent this one — an ordinary outcome, not an error.
    /// </summary>
    private async Task SendAsync(
        NotificationKind kind,
        string dedupeKey,
        IEnumerable<string?> to,
        IEnumerable<string?>? cc,
        string subject,
        string htmlBody,
        int? projectId,
        int? milestoneId = null,
        int? riskIssueId = null,
        CancellationToken cancellationToken = default)
    {
        var recipients = Clean(to);
        var copies = cc is null ? [] : Clean(cc);

        if (recipients.Count == 0)
        {
            // No address for the person the BRD names — worth knowing about, because it means a
            // record is missing an email, not that the rule did not fire.
            logger.LogWarning(
                "{Kind} for '{Subject}' had no recipient address; check the project's people records.",
                kind, subject);
            return;
        }

        long notificationId;

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Notification_Claim")
                .With("DedupeKey", dedupeKey)
                .With("Kind", kind)
                .With("ProjectId", projectId)
                .With("MilestoneId", milestoneId)
                .With("RiskIssueId", riskIssueId)
                .With("Recipients", string.Join("; ", recipients))
                .With("Subject", subject);

            var idParameter = command.Output("NotificationId", SqlDbType.BigInt);
            var claimedParameter = command.Output("Claimed", SqlDbType.Bit);

            await command.ExecuteNonQueryAsync(cancellationToken);

            if (claimedParameter.Value is not bool claimed || !claimed)
            {
                logger.LogDebug("{Kind} '{DedupeKey}' has already been sent.", kind, dedupeKey);
                return;
            }

            notificationId = (long)idParameter.Value;
        }

        string? error = null;

        try
        {
            await email.SendAsync(new EmailMessage(recipients, subject, Wrap(htmlBody), copies), cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "{Kind} '{Subject}' could not be sent.", kind, subject);
        }

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Notification_Complete")
                .With("NotificationId", notificationId)
                .With("Succeeded", error is null)
                .With("Error", error);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static List<string> Clean(IEnumerable<string?> addresses) =>
        addresses.Where(a => !string.IsNullOrWhiteSpace(a))
                 .Select(a => a!.Trim())
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();

    private string ProjectLink(int projectId) => $"{BaseUrl()}/projects/{projectId}";

    private string UpdateLink(int projectId) => $"{BaseUrl()}/updates/submit/{projectId}";

    private string DashboardLink() => BaseUrl();

    private string BaseUrl() => _options.PortalBaseUrl.TrimEnd('/');

    private static string StatusChangeBody(UpdateContext context, string link, string what)
    {
        var body = new StringBuilder()
            .Append(Paragraph($"{Encode(context.ProjectCode)} {Encode(context.ProjectName)} ({Encode(context.ReportingEntityName)}) "
                              + $"{what}, reported by {Encode(context.SubmittedBy)} on {context.UpdateDate:dd-MMM-yyyy}."));

        if (context.PreviousStatusName is { } previous)
        {
            body.Append(Field("Previous status", previous));
        }

        body.Append(Field("Progress", $"{context.ProgressPercent}%"));

        if (!string.IsNullOrWhiteSpace(context.KeyUpdate))
        {
            // BR-02: the cause and the recovery action. It is the point of the alert, so it goes first.
            body.Append(Field("Cause and recovery action", context.KeyUpdate));
        }

        if (!string.IsNullOrWhiteSpace(context.NextAction))
        {
            var due = context.NextActionDueDate is { } d ? $" (due {d:dd-MMM-yyyy})" : string.Empty;
            body.Append(Field("Next action", context.NextAction + due));
        }

        return body.Append(LinkParagraph(link, "Open the project")).ToString();
    }

    private static string AttentionBody(UpdateContext context, string link) =>
        new StringBuilder()
            .Append(Paragraph($"{Encode(context.ProjectCode)} {Encode(context.ProjectName)} has been flagged for "
                              + $"<strong>management attention</strong> by {Encode(context.SubmittedBy)}."))
            .Append(Field("Reason", context.AttentionReason ?? string.Empty))
            .Append(Field("Status", $"{context.StatusName}, {context.ProgressPercent}%"))
            .Append(LinkParagraph(link, "Open the project"))
            .ToString();

    private static string Paragraph(string html) =>
        $"<p style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1b2b3a\">{html}</p>";

    private static string Field(string label, string value) =>
        $"<p style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1b2b3a;margin:4px 0\">"
        + $"<strong>{Encode(label)}:</strong> {Encode(value)}</p>";

    private static string LinkParagraph(string url, string text) =>
        $"<p style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;margin-top:16px\">"
        + $"<a href=\"{url}\" style=\"color:#1b3b6f;font-weight:600\">{Encode(text)}</a></p>";

    /// <summary>
    /// Everything that reaches the body is encoded — a key update is free text typed by a
    /// project manager, and it must not be able to carry markup into a mail sent to the board.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Wrap(string body) =>
        "<html><body style=\"margin:0;padding:24px;background:#f4f6f8\">"
        + "<div style=\"max-width:640px;margin:0 auto;background:#ffffff;border-radius:8px;padding:24px;"
        + "border:1px solid #e3e8ee\">"
        + body
        + "<hr style=\"border:none;border-top:1px solid #e3e8ee;margin:24px 0\"/>"
        + "<p style=\"font-family:Segoe UI,Arial,sans-serif;font-size:12px;color:#64748b\">"
        + "Sent by PMO360, the Project Management Office Portal. Do not reply to this message.</p>"
        + "</div></body></html>";

    private static UpdateContext ReadUpdateContext(SqlDataReader r) => new(
        r.GetInt("UpdateId"),
        r.GetInt("ProjectId"),
        r.GetString("ProjectCode"),
        r.GetString("ProjectName"),
        r.GetString("ReportingEntityName"),
        r.GetDate("UpdateDate"),
        r.GetEnum<ProjectStatus>("StatusId"),
        r.GetString("StatusName"),
        r.GetEnumOrNull<ProjectStatus>("PreviousStatusId"),
        r.GetStringOrNull("PreviousStatusName"),
        r.GetInt("ProgressPercent"),
        r.GetStringOrNull("KeyUpdate"),
        r.GetStringOrNull("NextAction"),
        r.GetDateOrNull("NextActionDueDate"),
        r.GetBool("AttentionRequired"),
        r.GetStringOrNull("AttentionReason"),
        r.GetString("SubmittedByDisplayName"),
        r.GetStringOrNull("ProjectOwnerEmail"),
        r.GetStringOrNull("SponsorEmail"),
        r.GetStringOrNull("ProjectManagerEmail"));

    private sealed record UpdateContext(
        int UpdateId,
        int ProjectId,
        string ProjectCode,
        string ProjectName,
        string ReportingEntityName,
        DateOnly UpdateDate,
        ProjectStatus Status,
        string StatusName,
        ProjectStatus? PreviousStatus,
        string? PreviousStatusName,
        int ProgressPercent,
        string? KeyUpdate,
        string? NextAction,
        DateOnly? NextActionDueDate,
        bool AttentionRequired,
        string? AttentionReason,
        string SubmittedBy,
        string? ProjectOwnerEmail,
        string? SponsorEmail,
        string? ProjectManagerEmail);
}
