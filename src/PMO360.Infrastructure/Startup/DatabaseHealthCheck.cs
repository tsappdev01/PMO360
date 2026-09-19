using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Startup;

/// <summary>
/// The controlled value lists live in the database (FR-02) and are mirrored by enums in the
/// domain. If the two ever drift — a script not run, an id changed — every status on the
/// dashboard would be wrong in a way nobody would notice for weeks.
///
/// So the portal checks at startup and refuses to run on a mismatch, naming what is wrong.
/// The failure is a clear message on the first request rather than a wrong number on a board pack.
/// </summary>
public sealed class DatabaseHealthCheck(
    ISqlConnectionFactory connections,
    ILogger<DatabaseHealthCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var expected = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectStatus"] = Map<ProjectStatus>(),
            ["ProjectPriority"] = Map<ProjectPriority>(),
            ["MilestoneStatus"] = Map<MilestoneStatus>(),
            ["RiskType"] = Map<RiskType>(),
            ["RiskSeverity"] = Map<RiskSeverity>(),
            ["RiskStatus"] = Map<RiskStatus>(),
            ["RecordState"] = Map<RecordState>(),
            ["AssignmentRole"] = Map<AssignmentRole>()
        };

        var actual = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_System_GetEnumerations");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var list = reader.GetString("ListName");
                if (!actual.TryGetValue(list, out var values))
                {
                    values = new Dictionary<int, string>();
                    actual[list] = values;
                }

                values[Convert.ToInt32(reader.GetValue(reader.GetOrdinal("Id")))] = reader.GetString("Code");
            }
        }

        var problems = new List<string>();

        foreach (var (listName, expectedValues) in expected)
        {
            if (!actual.TryGetValue(listName, out var actualValues))
            {
                problems.Add($"{listName}: the list is missing from the database — run db/002_controlled_values.sql.");
                continue;
            }

            foreach (var (id, code) in expectedValues)
            {
                if (!actualValues.TryGetValue(id, out var actualCode))
                {
                    problems.Add($"{listName}: id {id} ({code}) is missing from the database.");
                }
                else if (!string.Equals(actualCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{listName}: id {id} is '{actualCode}' in the database but '{code}' in the application.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The database's controlled value lists do not match the application:"
                + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        logger.LogInformation("Database controlled value lists verified against the application's enums.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Dictionary<int, string> Map<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(v => Convert.ToInt32(v), v => v.ToString()!);
}
