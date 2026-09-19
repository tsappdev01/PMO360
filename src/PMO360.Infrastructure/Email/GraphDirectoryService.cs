using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using PMO360.Application.Abstractions;
using PMO360.Domain.Entities;

namespace PMO360.Infrastructure.Email;

/// <summary>
/// FR-04. People are picked from the corporate directory rather than typed, so the same person
/// is spelled the same way on every project and a notification has an address to go to.
/// </summary>
public sealed class GraphDirectoryService(
    GraphServiceClient graph,
    ILogger<GraphDirectoryService> logger) : IDirectoryService
{
    public async Task<IReadOnlyList<PersonRef>> SearchPeopleAsync(
        string term, int take = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return Array.Empty<PersonRef>();
        }

        var search = term.Trim().Replace("'", "''");

        try
        {
            var result = await graph.Users.GetAsync(request =>
            {
                request.QueryParameters.Filter =
                    $"startswith(displayName,'{search}') or startswith(mail,'{search}') "
                    + $"or startswith(userPrincipalName,'{search}')";
                request.QueryParameters.Select = ["id", "displayName", "mail", "userPrincipalName"];
                request.QueryParameters.Top = take;
                request.QueryParameters.Orderby = ["displayName"];
                // Guest consultants hold accounts in the tenant too, so they are searchable —
                // being findable is not being able to see anything.
                request.QueryParameters.Count = true;
                request.Headers.Add("ConsistencyLevel", "eventual");
            }, cancellationToken);

            return result?.Value?
                .Select(u => new PersonRef(
                    u.DisplayName ?? u.UserPrincipalName ?? "Unknown",
                    u.Id,
                    u.Mail ?? u.UserPrincipalName))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            // A directory that is briefly unreachable should not stop a form being filled in;
            // the caller falls back to whatever the user typed.
            logger.LogWarning(ex, "Directory search for '{Term}' failed.", term);
            return Array.Empty<PersonRef>();
        }
    }
}
