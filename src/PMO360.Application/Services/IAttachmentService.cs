using PMO360.Domain.Entities;
using PMO360.Domain.Enums;

namespace PMO360.Application.Services;

public sealed record AttachmentUpload(Stream Content, string FileName, string ContentType, long SizeBytes);

public interface IAttachmentService
{
    /// <summary>FR-26. Attaches a document to the project record or to one update.</summary>
    Task<Attachment> AttachAsync(
        int projectId,
        int? projectUpdateId,
        AttachmentScope scope,
        AttachmentUpload upload,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attachment>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Resolves an attachment to something the browser can download, access-checked.</summary>
    Task<(Attachment Attachment, Uri? ReadLink)?> ResolveAsync(int attachmentId, CancellationToken cancellationToken = default);
}
