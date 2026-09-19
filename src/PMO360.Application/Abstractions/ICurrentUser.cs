using PMO360.Domain.Entities;

namespace PMO360.Application.Abstractions;

/// <summary>
/// The signed-in user, as the application needs them: who they are for stamping records, and
/// which BRD roles their Entra ID group membership resolved to.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? ObjectId { get; }
    string DisplayName { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);

    /// <summary>The value stamped onto records this user writes.</summary>
    PersonRef ToPersonRef();
}
