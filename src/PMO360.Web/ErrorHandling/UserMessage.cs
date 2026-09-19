using Microsoft.Data.SqlClient;

namespace PMO360.Web.ErrorHandling;

/// <summary>
/// Turns an exception into something a project manager can read and act on.
///
/// Nothing technical reaches the screen: no stack trace, no SQL error number, no procedure name.
/// The detail goes to the log with a reference the user can quote, which is what IT Support
/// needs — and what the user cannot do anything with.
///
/// The one thing that does pass through unchanged is a message the application wrote itself.
/// "A closure note is required when a risk or issue is closed" is already the right sentence,
/// and replacing it with something generic would be a step backwards.
/// </summary>
public static class UserMessage
{
    public static string For(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            "You do not have permission to do that. Access to PMO360 is granted through security "
            + "groups — ask the PMO if you need it.",

        // Our own services throw these with a sentence already written for the person reading it.
        InvalidOperationException invalid when IsOurs(invalid) => invalid.Message,

        SqlException sql => ForSql(sql),

        TimeoutException or TaskCanceledException or OperationCanceledException =>
            "That took longer than expected and was stopped. Try again in a moment.",

        _ => "Something went wrong and the portal could not complete that. Nothing has been "
             + "changed. If it keeps happening, tell IT Support and quote the reference below."
    };

    private static string ForSql(SqlException sql) => sql.Number switch
    {
        // Connection-level problems: the portal could not get to the database at all.
        -2 or 258 => "The database did not respond in time. Try again in a moment.",
        53 or 17142 or 10060 or 10061 or 40613 =>
            "The portal cannot reach the PMO360 database at the moment. This is usually temporary; "
            + "if it continues, tell IT Support.",
        18456 or 4060 =>
            "The portal could not sign in to the PMO360 database. This is a configuration problem "
            + "rather than anything you did — tell IT Support and quote the reference below.",

        // Something the database expects is not there. Almost always a script in db/ that has
        // not been applied to this environment, which is worth saying plainly.
        208 or 2812 or 2209 =>
            "Part of the PMO360 database is missing, so that request could not be completed. "
            + "The database scripts may not all have been applied to this environment — "
            + "tell IT Support and quote the reference below.",

        // A constraint the business rules also enforce. The rule has already been explained by
        // the form in every path we know of, so this is the backstop.
        547 or 2601 or 2627 =>
            "That change conflicts with a record that already exists, or with a rule the database "
            + "enforces. Check the values and try again.",

        _ => "The database could not complete that request. Nothing has been changed. "
             + "If it keeps happening, tell IT Support and quote the reference below."
    };

    /// <summary>
    /// An <see cref="InvalidOperationException"/> the application raised deliberately, as opposed
    /// to one thrown from inside a library. Ours are written as complete sentences for the reader,
    /// so they are shown as they are; a library's are not, so they are not.
    /// </summary>
    private static bool IsOurs(InvalidOperationException exception) =>
        exception.TargetSite?.DeclaringType?.Namespace?.StartsWith("PMO360", StringComparison.Ordinal) == true;

    /// <summary>
    /// A short reference the user can quote and the log can be searched by. Short enough to read
    /// down a telephone, long enough not to collide within a day's logs.
    /// </summary>
    public static string NewReference() =>
        Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}
