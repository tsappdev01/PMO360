using System.Data;
using Microsoft.Data.SqlClient;

namespace PMO360.Infrastructure.Data;

/// <summary>
/// The whole data access surface: open a connection, call a procedure, read the rows.
/// There is no ORM and no ad-hoc SQL anywhere in the application — every statement the
/// database runs is a procedure in <c>db/</c>, and the application holds EXECUTE rights only.
/// </summary>
public static class Db
{
    public static SqlCommand Proc(SqlConnection connection, string name)
    {
        var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = name;
        return command;
    }

    /// <summary>
    /// Adds a parameter, mapping null to <see cref="DBNull"/>. Procedures declare their own
    /// types, so the value's CLR type is enough here — except for the cases below, which SQL
    /// Server would otherwise infer unhelpfully.
    /// </summary>
    public static SqlCommand With(this SqlCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name.StartsWith('@') ? name : "@" + name;

        switch (value)
        {
            case null:
                parameter.Value = DBNull.Value;
                break;
            case DateOnly date:
                // DateOnly has no SqlDbType of its own; date is the column type in every case.
                parameter.SqlDbType = SqlDbType.Date;
                parameter.Value = date.ToDateTime(TimeOnly.MinValue);
                break;
            case bool flag:
                parameter.SqlDbType = SqlDbType.Bit;
                parameter.Value = flag;
                break;
            case Enum enumeration:
                // Every enum in the domain is stored as its tinyint id.
                parameter.SqlDbType = SqlDbType.TinyInt;
                parameter.Value = Convert.ToByte(enumeration);
                break;
            default:
                parameter.Value = value;
                break;
        }

        command.Parameters.Add(parameter);
        return command;
    }

    /// <summary>
    /// Passes a <c>PersonRef</c> as the three parameters the procedures declare for it:
    /// @&lt;prefix&gt;ObjectId, @&lt;prefix&gt;DisplayName and @&lt;prefix&gt;Email.
    /// </summary>
    public static SqlCommand WithPerson(this SqlCommand command, string prefix, Domain.Entities.PersonRef? person)
    {
        command.With(prefix + "ObjectId", person?.ObjectId);
        command.With(prefix + "DisplayName", person?.DisplayName);
        command.With(prefix + "Email", person?.Email);
        return command;
    }

    public static SqlParameter Output(this SqlCommand command, string name, SqlDbType type, int size = 0)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name.StartsWith('@') ? name : "@" + name;
        parameter.SqlDbType = type;
        parameter.Direction = ParameterDirection.Output;
        if (size > 0)
        {
            parameter.Size = size;
        }

        command.Parameters.Add(parameter);
        return parameter;
    }

    public static async Task<List<T>> ReadAllAsync<T>(
        this SqlDataReader reader, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public static string GetString(this SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? string.Empty : reader.GetString(reader.GetOrdinal(column));

    public static string? GetStringOrNull(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int GetInt(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    public static int? GetIntOrNull(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static long GetLong(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0L : reader.GetInt64(ordinal);
    }

    public static bool GetBool(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
    }

    public static DateOnly GetDate(this SqlDataReader reader, string column) =>
        DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal(column)));

    public static DateOnly? GetDateOrNull(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));
    }

    public static DateTimeOffset GetTimestamp(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? default : reader.GetDateTimeOffset(ordinal);
    }

    public static DateTimeOffset? GetTimestampOrNull(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTimeOffset(ordinal);
    }

    public static TEnum GetEnum<TEnum>(this SqlDataReader reader, string column) where TEnum : struct, Enum
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return default;
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToInt32(reader.GetValue(ordinal)));
    }

    public static TEnum? GetEnumOrNull<TEnum>(this SqlDataReader reader, string column) where TEnum : struct, Enum
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToInt32(reader.GetValue(ordinal)));
    }

    /// <summary>Reads the three columns a <c>PersonRef</c> is stored in, by their shared prefix.</summary>
    public static Domain.Entities.PersonRef GetPerson(this SqlDataReader reader, string prefix) =>
        new(reader.GetString(prefix + "DisplayName"),
            reader.GetStringOrNull(prefix + "ObjectId"),
            reader.HasColumn(prefix + "Email") ? reader.GetStringOrNull(prefix + "Email") : null);

    public static bool HasColumn(this SqlDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
