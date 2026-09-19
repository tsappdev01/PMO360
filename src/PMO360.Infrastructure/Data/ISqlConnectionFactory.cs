using Microsoft.Data.SqlClient;

namespace PMO360.Infrastructure.Data;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens connections to the PMO database. In Azure the connection string carries
/// <c>Authentication=Active Directory Default</c> and no password: the App Service's managed
/// identity is the SQL principal, so there is no secret to rotate or to leak.
/// </summary>
public sealed class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
