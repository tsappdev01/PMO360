using System.Security.Claims;
using PMO360.Application.Abstractions;
using PMO360.Domain.Entities;

namespace PMO360.Web.Identity;

/// <summary>
/// The signed-in user as the application needs them. Reads the Entra ID token: the object id
/// identifies the person for scoping and for stamping records, and the roles have already been
/// resolved from their security group membership by <see cref="RoleClaimsTransformation"/>.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <summary>
    /// The Entra ID object id. "oid" is the stable identifier for a person in a tenant — the
    /// name and the email can both change, this does not.
    /// </summary>
    public string? ObjectId =>
        Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? Principal?.FindFirst("oid")?.Value;

    public string DisplayName =>
        Principal?.FindFirst("name")?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? Email
        ?? "Unknown user";

    public string? Email =>
        Principal?.FindFirst("preferred_username")?.Value
        ?? Principal?.FindFirst(ClaimTypes.Email)?.Value
        ?? Principal?.FindFirst("upn")?.Value;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToArray() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public PersonRef ToPersonRef() => new(DisplayName, ObjectId, Email);
}
