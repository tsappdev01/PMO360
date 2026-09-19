namespace PMO360.Application.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Blob service URI, e.g. https://pmo360prod.blob.core.windows.net. Managed identity is used to authenticate.</summary>
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
}
