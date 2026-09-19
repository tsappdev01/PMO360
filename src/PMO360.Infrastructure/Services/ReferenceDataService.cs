using Microsoft.Extensions.Caching.Memory;
using PMO360.Application.Services;
using PMO360.Domain.Entities;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

/// <summary>
/// The controlled lists behind every choice field. usp_Reference_GetAll returns all four in one
/// round trip, and they are cached briefly — long enough that the choice fields do not cost a
/// call each, short enough that running a script in <c>db/</c> is picked up without a restart.
/// </summary>
public sealed class ReferenceDataService(ISqlConnectionFactory connections, IMemoryCache cache) : IReferenceDataService
{
    private const string CacheKey = "pmo:reference";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private sealed record ReferenceData(
        IReadOnlyList<ReportingEntity> Entities,
        IReadOnlyList<Department> Departments,
        IReadOnlyList<Phase> Phases,
        IReadOnlyList<Consultant> Consultants);

    public async Task<IReadOnlyList<ReportingEntity>> GetEntitiesAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Entities;

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Departments;

    public async Task<IReadOnlyList<Phase>> GetPhasesAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Phases;

    public async Task<IReadOnlyList<Consultant>> GetConsultantsAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Consultants;

    private async Task<ReferenceData> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out ReferenceData? cached) && cached is not null)
        {
            return cached;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Reference_GetAll");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var entities = await reader.ReadAllAsync(r => new ReportingEntity
        {
            Id = r.GetInt("Id"),
            Code = r.GetString("Code"),
            Name = r.GetString("Name"),
            SortOrder = r.GetInt("SortOrder"),
            IsActive = r.GetBool("IsActive")
        }, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        var departments = await reader.ReadAllAsync(r => new Department
        {
            Id = r.GetInt("Id"),
            Name = r.GetString("Name"),
            SortOrder = r.GetInt("SortOrder"),
            IsActive = r.GetBool("IsActive")
        }, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        var phases = await reader.ReadAllAsync(r => new Phase
        {
            Id = r.GetInt("Id"),
            Name = r.GetString("Name"),
            SortOrder = r.GetInt("SortOrder"),
            IsActive = r.GetBool("IsActive")
        }, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        var consultants = await reader.ReadAllAsync(r => new Consultant
        {
            Id = r.GetInt("Id"),
            Name = r.GetString("Name"),
            IsActive = r.GetBool("IsActive")
        }, cancellationToken);

        var data = new ReferenceData(entities, departments, phases, consultants);
        cache.Set(CacheKey, data, CacheLifetime);
        return data;
    }
}
