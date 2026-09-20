namespace PMO360.Application.Options;

/// <summary>Where the bytes of a supporting document are kept (FR-26).</summary>
public enum DocumentStorageProvider
{
    /// <summary>Azure Blob Storage. The right answer in Azure: cheap, and it keeps large files out of the database.</summary>
    Blob = 0,

    /// <summary>
    /// SQL Server, in pmo.AttachmentContent. For an on-premises deployment with no storage
    /// account, and for a UAT environment where one database is simpler to move and to back up
    /// than a database plus a container.
    /// </summary>
    Database = 1,

    /// <summary>No document storage. Attaching a document says so; everything else works.</summary>
    None = 2
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Blob, Database or None. Blob falls back to None, with a warning, when
    /// <see cref="ServiceUri"/> is not a usable absolute URI — the portal is worth more running
    /// without attachments than not running at all.
    /// </summary>
    public DocumentStorageProvider Provider { get; set; } = DocumentStorageProvider.Blob;

    /// <summary>Blob service URI, e.g. https://pmo360prod.blob.core.windows.net. Managed identity authenticates.</summary>
    public string ServiceUri { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "project-documents";

    /// <summary>Largest supporting document accepted, in megabytes.</summary>
    public int MaxUploadMegabytes { get; set; } = 20;

    /// <summary>
    /// Extensions accepted for a supporting document. An allow list, not a block list:
    /// the portal takes meeting papers and evidence, not executables.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
        ".png", ".jpg", ".jpeg", ".msg", ".txt", ".csv"
    ];

    /// <summary>
    /// True when <see cref="ServiceUri"/> is something a BlobServiceClient can actually be built
    /// from. The committed settings file carries "https://&lt;storage-account&gt;..." as a
    /// placeholder, which is not empty and so looks configured — building a Uri from it throws,
    /// and it threw at dependency resolution, which took out the one page that attaches a file.
    /// </summary>
    public bool HasUsableServiceUri =>
        !string.IsNullOrWhiteSpace(ServiceUri)
        && !ServiceUri.Contains('<')
        && !ServiceUri.Contains('>')
        && Uri.TryCreate(ServiceUri, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
