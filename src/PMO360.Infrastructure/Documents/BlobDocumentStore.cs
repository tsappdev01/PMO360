using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;

namespace PMO360.Infrastructure.Documents;

/// <summary>
/// FR-26, on Azure Blob Storage. The application authenticates with its managed identity, so
/// there is no storage key in configuration to rotate or to leak.
/// </summary>
public sealed class BlobDocumentStore(
    BlobServiceClient client,
    IOptions<StorageOptions> options,
    ILogger<BlobDocumentStore> logger) : IDocumentStore
{
    private readonly StorageOptions _options = options.Value;

    public async Task<StoredDocument> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string projectCode,
        CancellationToken cancellationToken = default)
    {
        var safeName = SanitiseFileName(fileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{extension}' is not an accepted document type. Accepted types: "
                + string.Join(", ", _options.AllowedExtensions) + ".");
        }

        var container = client.GetBlobContainerClient(_options.ContainerName);

        // The blob name is built here, never taken from the upload: a file called
        // "../../secrets.txt" gets a new name like any other.
        var blobName = $"{projectCode}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType,
                    // Documents are downloaded, never rendered in place: an uploaded HTML or SVG
                    // file cannot run script against the portal's origin.
                    ContentDisposition = $"attachment; filename=\"{safeName}\""
                }
            },
            cancellationToken);

        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new StoredDocument(blobName, safeName, contentType, properties.Value.ContentLength);
    }

    public async Task<DocumentContent?> OpenAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blob = client.GetBlobContainerClient(_options.ContainerName).GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);

        return new DocumentContent(
            download.Value.Content,
            download.Value.Details.ContentType ?? "application/octet-stream",
            Path.GetFileName(blobName));
    }

    public async Task<Uri?> TryGetReadLinkAsync(
        string blobName, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var blob = client.GetBlobContainerClient(_options.ContainerName).GetBlobClient(blobName);

        // With a managed identity there is no account key to sign with, so a user delegation
        // key is requested from Entra ID. Where that is not available — a local run against the
        // storage emulator, or a role that cannot delegate — the caller streams the file through
        // the app instead, which is correct but slower.
        try
        {
            if (!blob.CanGenerateSasUri)
            {
                var start = DateTimeOffset.UtcNow.AddMinutes(-5);
                var expires = DateTimeOffset.UtcNow.Add(lifetime);

                var delegationKey = await client.GetUserDelegationKeyAsync(start, expires, cancellationToken);

                var builder = new BlobSasBuilder(BlobSasPermissions.Read, expires)
                {
                    BlobContainerName = _options.ContainerName,
                    BlobName = blobName,
                    Resource = "b",
                    StartsOn = start
                };

                var sas = builder.ToSasQueryParameters(delegationKey.Value, client.AccountName).ToString();
                return new Uri($"{blob.Uri}?{sas}");
            }

            return blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(lifetime));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not issue a read link for {BlobName}; the file will be streamed through the portal.",
                blobName);
            return null;
        }
    }

    /// <summary>Keeps the name the user will see, without letting it carry a path.</summary>
    private static string SanitiseFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "document" : cleaned;
    }
}
