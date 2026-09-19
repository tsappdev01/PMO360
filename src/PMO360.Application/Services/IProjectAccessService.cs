namespace PMO360.Application.Services;

/// <summary>
/// Section 6 and AC-10, in one place. Management and the PMO see the whole portfolio; project
/// managers and consultants see only the projects they are assigned to; nobody but the PMO
/// creates or closes a project; management edits nothing.
///
/// The scope itself is applied in the database — every read procedure takes the caller's object
/// id and <see cref="CanSeeWholePortfolio"/> and filters through pmo.fn_VisibleProjects — so a
/// query cannot forget to apply it.
/// </summary>
public interface IProjectAccessService
{
    bool CanSeeWholePortfolio { get; }
    bool CanAdministerProjects { get; }
    bool CanSubmitUpdates { get; }

    /// <summary>The signed-in user's Entra object id, as passed to the procedures.</summary>
    string? UserObjectId { get; }

    Task<bool> CanViewAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>May submit an update, and maintain milestones and risks, for this project.</summary>
    Task<bool> CanContributeAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when the user may not contribute.</summary>
    Task EnsureCanContributeAsync(int projectId, CancellationToken cancellationToken = default);

    void EnsureCanAdminister();
}
