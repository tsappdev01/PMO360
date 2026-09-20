namespace PMO360.Infrastructure.Documents;

/// <summary>
/// Naming and vetting rules shared by every document store, so switching Storage:Provider
/// cannot change what is accepted or how a stored file is named.
/// </summary>
internal static class DocumentNaming
{
    /// <summary>Keeps the name the user will see, without letting it carry a path.</summary>
    public static string SanitiseFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "document" : cleaned;
    }

    public static void EnsureAcceptedExtension(string safeFileName, string[] allowedExtensions)
    {
        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{extension}' is not an accepted document type. Accepted types: "
                + string.Join(", ", allowedExtensions) + ".");
        }
    }

    /// <summary>
    /// The storage key: built here, never taken from the upload, so a file called
    /// "../../secrets.txt" gets a new name like any other.
    /// </summary>
    public static string BuildStorageKey(string projectCode, string safeFileName) =>
        $"{projectCode}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}"
        + Path.GetExtension(safeFileName).ToLowerInvariant();
}
