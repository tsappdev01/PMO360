using PMO360.Domain.Entities;

namespace PMO360.Application.Abstractions;

/// <summary>
/// FR-04. People are picked from the corporate directory rather than typed, so the same person
/// is spelled the same way on every project. Backed by Microsoft Graph user search.
/// </summary>
public interface IDirectoryService
{
    Task<IReadOnlyList<PersonRef>> SearchPeopleAsync(
        string term,
        int take = 10,
        CancellationToken cancellationToken = default);
}
