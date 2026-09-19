using System.Data;
using PMO360.Application.Abstractions;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

public sealed class ProjectAccessService(
    ISqlConnectionFactory connections,
    ICurrentUser user,
    IClock clock) : IProjectAccessService
{
    public bool CanSeeWholePortfolio =>
        user.IsInRole(PmoRole.Management)
        || user.IsInRole(PmoRole.PmoAdministrator)
        || user.IsInRole(PmoRole.ItSupport);

    public bool CanAdministerProjects => user.IsInRole(PmoRole.PmoAdministrator);

    public bool CanSubmitUpdates =>
        user.IsInRole(PmoRole.ProjectManager)
        || user.IsInRole(PmoRole.Consultant)
        || user.IsInRole(PmoRole.PmoAdministrator);

    public string? UserObjectId => user.ObjectId;

    public async Task<bool> CanViewAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (CanSeeWholePortfolio)
        {
            return true;
        }

        // usp_Project_GetDetail returns nothing at all for a project outside the caller's scope,
        // so asking it is the same check the page itself will make.
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_GetDetail")
            .With("ProjectId", projectId)
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", CanSeeWholePortfolio)
            .With("Today", clock.Today);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    public async Task<bool> CanContributeAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (!CanSubmitUpdates)
        {
            return false;
        }

        if (CanAdministerProjects)
        {
            return true;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_CanContribute")
            .With("ProjectId", projectId)
            .With("UserObjectId", user.ObjectId)
            .With("IsPmoAdmin", CanAdministerProjects)
            .With("Today", clock.Today);

        var result = command.Output("CanContribute", SqlDbType.Bit);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return result.Value is bool allowed && allowed;
    }

    public async Task EnsureCanContributeAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (!await CanContributeAsync(projectId, cancellationToken))
        {
            throw new UnauthorizedAccessException(
                $"{user.DisplayName} may not submit updates or maintain records for project {projectId}.");
        }
    }

    public void EnsureCanAdminister()
    {
        if (!CanAdministerProjects)
        {
            throw new UnauthorizedAccessException(
                "Only the PMO may create, close or reopen a project (FR-05).");
        }
    }
}
