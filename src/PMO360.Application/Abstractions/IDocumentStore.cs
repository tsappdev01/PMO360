namespace PMO360.Application.Abstractions;

public sealed record StoredDocument(string BlobName, string FileName, string ContentType, long SizeBytes);

public sealed record DocumentContent(Stream Content, string ContentType, string FileName);

/// <summary>
/// Supporting documents (FR-26). Backed by Azure Blob Storage; the interface exists so the
/// update path can be tested without a storage account.
/// </summary>
public interface IDocumentStore
{
    Task<StoredDocument> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string projectCode,
        CancellationToken cancellationToken = default);

    Task<DocumentContent?> OpenAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived read link for the browser, so a large file is served by storage rather than
    /// through the Blazor circuit. Returns null when the store cannot issue one, and the caller
    /// falls back to streaming through the app.
    /// </summary>
    Task<Uri?> TryGetReadLinkAsync(string blobName, TimeSpan lifetime, CancellationToken cancellationToken = default);
}
