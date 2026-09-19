using System.Data;
using Microsoft.Extensions.Logging;
using PMO360.Application.Abstractions;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Entities;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

public sealed class RiskService(
    ISqlConnectionFactory connections,
    IProjectAccessService access,
    INotificationService notifications,
    ICurrentUser user,
    IClock clock,
    ILogger<RiskService> logger) : IRiskService
{
    public async Task<IReadOnlyList<RiskIssue>> GetForProjectAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanViewAsync(projectId, cancellationToken))
        {
            return Array.Empty<RiskIssue>();
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Risk_GetForProject")
            .With("ProjectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(ProjectService.ReadRisk, cancellationToken);
    }

    public async Task<RiskIssue> SaveAsync(
        int projectId, RiskInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureCanContributeAsync(projectId, cancellationToken);

        int savedId;
        bool becameHigh;

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Risk_Save")
                .With("RiskId", input.Id)
                .With("ProjectId", projectId)
                .With("TypeId", input.Type)
                .With("Description", input.Description)
                .With("SeverityId", input.Severity)
                .WithPerson("Owner", input.Owner)
                .With("Mitigation", input.Mitigation)
                .With("DueDate", input.DueDate)
                .With("UserObjectId", user.ObjectId)
                .With("UserName", user.DisplayName);

            var savedIdParameter = command.Output("SavedRiskId", SqlDbType.Int);
            var becameHighParameter = command.Output("BecameHigh", SqlDbType.Bit);

            await command.ExecuteNonQueryAsync(cancellationToken);

            savedId = savedIdParameter.Value is int id ? id : 0;
            becameHigh = becameHighParameter.Value is bool high && high;
        }

        // WF-06. The procedure decides whether this counts as newly High, so the rule lives
        // with the data rather than being re-derived here.
        if (becameHigh)
        {
            try
            {
                await notifications.OnHighSeverityRiskRaisedAsync(projectId, savedId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Risk {RiskId} was saved but its notification failed.", savedId);
            }
        }

        return new RiskIssue
        {
            Id = savedId,
            ProjectId = projectId,
            Type = input.Type,
            Description = input.Description,
            Severity = input.Severity,
            Owner = input.Owner,
            Mitigation = input.Mitigation,
            DueDate = input.DueDate,
            Status = RiskStatus.Open,
            RaisedOn = clock.Now,
            RaisedBy = user.ToPersonRef()
        };
    }

    public async Task CloseAsync(int riskId, string closureNote, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(closureNote))
        {
            // FR-10: a closed item is retained with a closure note, so the note is not optional.
            throw new InvalidOperationException("A closure note is required when a risk or issue is closed.");
        }

        var projectId = await GetProjectIdAsync(riskId, cancellationToken);
        await access.EnsureCanContributeAsync(projectId, cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Risk_Close")
            .With("RiskId", riskId)
            .With("ClosureNote", closureNote.Trim())
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetProjectIdAsync(int riskId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Risk_GetProjectId")
            .With("RiskId", riskId);

        var projectId = command.Output("ProjectId", SqlDbType.Int);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return projectId.Value is int id
            ? id
            : throw new InvalidOperationException($"Risk or issue {riskId} was not found.");
    }
}
