using PMO360.Domain.Enums;

namespace PMO360.Domain.Entities;

/// <summary>
/// FR-26. The file itself lives in Azure Blob Storage; this row is the catalogue entry that
/// carries who attached it and what it belongs to. <see cref="BlobName"/> is server-generated,
/// never the user's file name, so an uploaded name cannot steer the storage path.
/// </summary>
public class Attachment
{
    public int Id { get; set; }

    public AttachmentScope Scope { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? ProjectUpdateId { get; set; }
    public ProjectUpdate? ProjectUpdate { get; set; }

    public string BlobName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public DateTimeOffset UploadedOn { get; set; }
    public PersonRef UploadedBy { get; set; } = new();
}
