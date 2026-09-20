using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Documents;

/// <summary>
/// FR-26 with the bytes in SQL Server, for when <c>Storage:Provider</c> is Database.
///
/// The catalogue row in pmo.Attachment is written by the caller either way; this only handles
/// the content. There is no read link to hand the browser, so a download is streamed through
/// the portal — correct, and the reason Blob is the better choice where a storage account exists.
/// </summary>
public sealed class DatabaseDocumentStore(
    ISqlConnectionFactory connections,
    IOptions<StorageOptions> options) : IDocumentStore
{
    private readonly StorageOptions _options = options.Value;

    public async Task<StoredDocument> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string projectCode,
        CancellationToken cancellationToken = default)
    {
        var safeName = DocumentNaming.SanitiseFileName(fileName);
        DocumentNaming.EnsureAcceptedExtension(safeName, _options.AllowedExtensions);

        // Read once into memory: a varbinary(max) parameter needs a length, and the upload is
        // already capped at Storage:MaxUploadMegabytes by the caller.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var limit = (long)_options.MaxUploadMegabytes * 1024 * 1024;
        if (bytes.LongLength > limit)
        {
            throw new InvalidOperationException(
                $"{safeName} is larger than the {_options.MaxUploadMegabytes} MB limit for a supporting document.");
        }

        // The same shape of key as a blob path, so the catalogue reads the same either way and
        // a later move between providers is a data migration rather than a schema change.
        var contentKey = DocumentNaming.BuildStorageKey(projectCode, safeName);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_AttachmentContent_Save")
            .With("ContentKey", contentKey)
            .With("ContentType", contentType)
            .With("FileName", safeName)
            .With("SizeBytes", bytes.LongLength);

        var contentParameter = command.CreateParameter();
        contentParameter.ParameterName = "@Content";
        contentParameter.SqlDbType = SqlDbType.VarBinary;
        contentParameter.Size = -1;   // -1 is varbinary(max); without it the value is truncated.
        contentParameter.Value = bytes;
        command.Parameters.Add(contentParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new StoredDocument(contentKey, safeName, contentType, bytes.LongLength);
    }

    public async Task<DocumentContent?> OpenAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_AttachmentContent_Get")
            .With("ContentKey", blobName);

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow | CommandBehavior.SequentialAccess, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        // SequentialAccess means the bytes stream out of the reader rather than being
        // materialised twice; the stream stays valid because it is copied here.
        var buffer = new MemoryStream();
        await using (var source = reader.GetStream(0))
        {
            await source.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;

        return new DocumentContent(
            buffer,
            reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1),
            reader.IsDBNull(2) ? Path.GetFileName(blobName) : reader.GetString(2));
    }

    /// <summary>
    /// Nothing to link to: the bytes are in the database, so the portal serves them itself.
    /// The caller falls back to streaming when this is null, which is exactly right here.
    /// </summary>
    public Task<Uri?> TryGetReadLinkAsync(
        string blobName, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        Task.FromResult<Uri?>(null);
}
