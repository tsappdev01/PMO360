using Microsoft.Data.SqlClient;
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

        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken);
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
        catch (SqlException ex)
        {
            // A concise line at Error, because that is what someone watching the log wants; the
            // exception itself at Debug, because thirty frames of Microsoft.Data.SqlClient on the
            // console tell the person reading them nothing they can act on. Turn the PMO360
            // category up to Debug to get the lot.
            logger.LogError(
                "The PMO360 database could not be reached at startup: SQL error {Number} - {Message}",
                ex.Number, ex.Message);
            logger.LogDebug(ex, "Full detail of the startup connection failure.");

            throw new StartupFailureException(DescribeConnectionFailure(ex), ex);
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
            throw new StartupFailureException(
                "The PMO360 database does not match this version of the application."
                + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
                + Environment.NewLine + Environment.NewLine
                + "Apply the scripts in db/ to this database - see db/README.md - and start again.");
        }

        logger.LogInformation("Database controlled value lists verified against the application's enums.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Dictionary<int, string> Map<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(v => Convert.ToInt32(v), v => v.ToString()!);

    /// <summary>Says what went wrong reaching SQL Server in terms of what to go and check.</summary>
    private static string DescribeConnectionFailure(SqlException ex)
    {
        var reason = ex.Number switch
        {
            53 or 17142 or 10060 or 10061 or 40613 or -1 =>
                "The server did not answer. Check that the server name in the connection string is "
                + "right, that SQL Server is running, and that it accepts remote connections.",
            18456 =>
                "The login was rejected. Check the user name and password in the connection string, "
                + "and that the login has access to the PMO360 database.",
            4060 =>
                "The login was accepted but the database was not. Check the database name, and that "
                + "the login is a user in it - see db/030_permissions.sql.",
            -2 =>
                "The connection timed out. The server may be busy or unreachable from this machine.",

            // Everything else, at this point, is still a failure to connect: nothing else has
            // been attempted yet. So give the advice that fits rather than a shrug.
            _ =>
                "The connection was refused. Check that the server name in the connection string is "
                + "right, that SQL Server is running and reachable from this machine, and that the "
                + $"login has access to the database. (SQL error {ex.Number}.)"
        };

        return "PMO360 cannot start: the database could not be reached."
               + Environment.NewLine + Environment.NewLine
               + "  " + reason
               + Environment.NewLine + Environment.NewLine
               + "The connection string is ConnectionStrings:PmoDatabase. On a local machine set it "
               + "with user secrets; in Azure set ConnectionStrings__PmoDatabase as an application "
               + "setting. See docs/03-deployment.md.";
    }

}
