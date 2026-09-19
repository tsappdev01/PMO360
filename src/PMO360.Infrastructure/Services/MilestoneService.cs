using System.Data;
using PMO360.Application.Abstractions;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Entities;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

public sealed class MilestoneService(
    ISqlConnectionFactory connections,
    IProjectAccessService access,
    ICurrentUser user,
    IClock clock) : IMilestoneService
{
    public async Task<IReadOnlyList<Milestone>> GetForProjectAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanViewAsync(projectId, cancellationToken))
        {
            return Array.Empty<Milestone>();
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Milestone_GetForProject")
            .With("ProjectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(ProjectService.ReadMilestone, cancellationToken);
    }

    public async Task<Milestone> SaveAsync(
        int projectId, MilestoneInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureCanContributeAsync(projectId, cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Milestone_Save")
            .With("MilestoneId", input.Id)
            .With("ProjectId", projectId)
            .With("Name", input.Name)
            .With("PlannedDate", input.PlannedDate)
            .With("BaselineDate", input.BaselineDate)
            .With("ActualDate", input.ActualDate)
            .With("StatusId", input.Status)
            .With("CompletionPercent", input.CompletionPercent)
            .WithPerson("Owner", input.Owner)
            .With("Today", clock.Today)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        var savedId = command.Output("SavedMilestoneId", SqlDbType.Int);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new Milestone
        {
            Id = savedId.Value is int id ? id : 0,
            ProjectId = projectId,
            Name = input.Name,
            PlannedDate = input.PlannedDate,
            BaselineDate = input.BaselineDate ?? input.PlannedDate,
            ActualDate = input.ActualDate,
            Status = input.Status,
            CompletionPercent = input.CompletionPercent,
            Owner = input.Owner,
            CreatedOn = clock.Now,
            CreatedBy = user.ToPersonRef()
        };
    }

    public async Task CancelAsync(int milestoneId, CancellationToken cancellationToken = default)
    {
        // The procedure works on one milestone, so the project it belongs to decides the check.
        var projectId = await GetProjectIdAsync(milestoneId, cancellationToken);
        await access.EnsureCanContributeAsync(projectId, cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Milestone_Cancel")
            .With("MilestoneId", milestoneId)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetProjectIdAsync(int milestoneId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Milestone_GetProjectId")
            .With("MilestoneId", milestoneId);

        var projectId = command.Output("ProjectId", SqlDbType.Int);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return projectId.Value is int id
            ? id
            : throw new InvalidOperationException($"Milestone {milestoneId} was not found.");
    }
}
