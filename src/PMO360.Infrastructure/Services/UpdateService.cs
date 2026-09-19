using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PMO360.Application.Abstractions;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Entities;
using PMO360.Domain.Enums;
using PMO360.Domain.Rules;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

/// <summary>
/// The single write path for status, progress, phase and the attention flag (FR-11).
///
/// The business rules of section 5.2 are checked twice on purpose: here, so the form can show
/// every problem at once against the field it belongs to, and again inside
/// usp_ProjectUpdate_Submit, which is what actually decides. The procedure's verdict wins.
/// </summary>
public sealed class UpdateService(
    ISqlConnectionFactory connections,
    IProjectAccessService access,
    INotificationService notifications,
    ICurrentUser user,
    IClock clock,
    ILogger<UpdateService> logger) : IUpdateService
{
    public async Task<UpdateSubmission> GetFormDefaultsAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_ProjectUpdate_GetFormDefaults")
            .With("ProjectId", projectId)
            .With("UserObjectId", user.ObjectId)
            .With("Today", clock.Today);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new UpdateSubmission { ProjectId = projectId, UpdateDate = clock.Today };
        }

        return new UpdateSubmission
        {
            ProjectId = projectId,
            UpdateDate = clock.Today,
            Status = reader.GetEnum<ProjectStatus>("StatusId"),
            ProgressPercent = reader.GetInt("ProgressPercent"),
            PhaseId = reader.GetIntOrNull("PhaseId"),
            MilestoneId = reader.GetIntOrNull("MilestoneId"),
            KeyUpdate = reader.GetStringOrNull("KeyUpdate"),
            Achievement = reader.GetStringOrNull("Achievement"),
            NextAction = reader.GetStringOrNull("NextAction"),
            NextActionOwner = reader.GetStringOrNull("NextActionOwnerDisplayName") is { } owner
                ? new PersonRef(owner,
                    reader.GetStringOrNull("NextActionOwnerObjectId"),
                    reader.GetStringOrNull("NextActionOwnerEmail"))
                : null,
            NextActionDueDate = reader.GetDateOrNull("NextActionDueDate"),
            AttentionRequired = reader.GetBool("AttentionRequired"),
            AttentionReason = reader.GetStringOrNull("AttentionReason")
        };
    }

    public async Task<SubmitUpdateResult> SubmitAsync(
        UpdateSubmission submission, CancellationToken cancellationToken = default)
    {
        await access.EnsureCanContributeAsync(submission.ProjectId, cancellationToken);

        int updateId;
        var failures = new List<ValidationFailure>();

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_ProjectUpdate_Submit")
                .With("ProjectId", submission.ProjectId)
                .With("UpdateDate", submission.UpdateDate)
                .With("StatusId", submission.Status)
                .With("ProgressPercent", submission.ProgressPercent)
                .With("PhaseId", submission.PhaseId)
                .With("MilestoneId", submission.MilestoneId)
                .With("KeyUpdate", Trim(submission.KeyUpdate))
                .With("Achievement", Trim(submission.Achievement))
                .With("NextAction", Trim(submission.NextAction))
                .WithPerson("NextActionOwner", submission.NextActionOwner)
                .With("NextActionDueDate", submission.NextActionDueDate)
                .With("AttentionRequired", submission.AttentionRequired)
                .With("AttentionReason", Trim(submission.AttentionReason))
                .With("Today", clock.Today)
                .With("UserObjectId", user.ObjectId)
                .With("UserName", user.DisplayName)
                .With("UserEmail", user.Email);

            var updateIdParameter = command.Output("UpdateId", SqlDbType.Int);

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                // Rows here are rejections (section 5.2). None means the update was accepted.
                failures = await reader.ReadAllAsync(r => new ValidationFailure(
                    r.GetString("Field"), r.GetString("Rule"), r.GetString("Message")), cancellationToken);
            }

            if (failures.Count > 0 || updateIdParameter.Value is not int id)
            {
                return SubmitUpdateResult.Rejected(
                    failures.Count > 0
                        ? failures
                        : [new ValidationFailure("ProjectId", "FR-12", "The update could not be saved.")]);
            }

            updateId = id;
        }

        // Section 5.3. Raised after the write and outside it: a mail server problem must not
        // undo an update the manager has been told was accepted.
        try
        {
            await notifications.OnUpdateSubmittedAsync(submission.ProjectId, updateId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Update {UpdateId} on project {ProjectId} was saved but its notifications failed.",
                updateId, submission.ProjectId);
        }

        return SubmitUpdateResult.Accepted(updateId);
    }

    public async Task SaveDraftAsync(UpdateSubmission submission, CancellationToken cancellationToken = default)
    {
        await access.EnsureCanContributeAsync(submission.ProjectId, cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_ProjectUpdate_SaveDraft")
            .With("ProjectId", submission.ProjectId)
            .With("UpdateDate", submission.UpdateDate)
            .With("StatusId", submission.Status)
            .With("ProgressPercent", submission.ProgressPercent)
            .With("PhaseId", submission.PhaseId)
            .With("MilestoneId", submission.MilestoneId)
            .With("KeyUpdate", Trim(submission.KeyUpdate))
            .With("Achievement", Trim(submission.Achievement))
            .With("NextAction", Trim(submission.NextAction))
            .WithPerson("NextActionOwner", submission.NextActionOwner)
            .With("NextActionDueDate", submission.NextActionDueDate)
            .With("AttentionRequired", submission.AttentionRequired)
            .With("AttentionReason", Trim(submission.AttentionReason))
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName)
            .With("UserEmail", user.Email);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectUpdate>> GetHistoryAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanViewAsync(projectId, cancellationToken))
        {
            return Array.Empty<ProjectUpdate>();
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_ProjectUpdate_GetHistory")
            .With("ProjectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(ProjectService.ReadUpdate, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectUpdate>> GetRecentAsync(
        int take = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_ProjectUpdate_GetRecent")
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("Take", take);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(r => new ProjectUpdate
        {
            Id = r.GetInt("UpdateId"),
            ProjectId = r.GetInt("ProjectId"),
            Project = new Project { Id = r.GetInt("ProjectId"), ProjectCode = r.GetString("ProjectCode"), Name = r.GetString("ProjectName") },
            UpdateDate = r.GetDate("UpdateDate"),
            Status = r.GetEnum<ProjectStatus>("StatusId"),
            ProgressPercent = r.GetInt("ProgressPercent"),
            KeyUpdate = r.GetStringOrNull("KeyUpdate"),
            AttentionRequired = r.GetBool("AttentionRequired"),
            SubmittedBy = new PersonRef(r.GetString("SubmittedByDisplayName")),
            SubmittedOn = r.GetTimestamp("SubmittedOn")
        }, cancellationToken);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
