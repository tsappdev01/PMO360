using System.Data;
using Microsoft.Extensions.Options;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;
using PMO360.Application.Services;
using PMO360.Domain.Entities;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

public sealed class AttachmentService(
    ISqlConnectionFactory connections,
    IDocumentStore documents,
    IProjectAccessService access,
    IProjectService projects,
    ICurrentUser user,
    IOptions<StorageOptions> storageOptions) : IAttachmentService
{
    private readonly StorageOptions _storage = storageOptions.Value;

    public async Task<Attachment> AttachAsync(
        int projectId,
        int? projectUpdateId,
        AttachmentScope scope,
        AttachmentUpload upload,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureCanContributeAsync(projectId, cancellationToken);

        var limit = (long)_storage.MaxUploadMegabytes * 1024 * 1024;
        if (upload.SizeBytes > limit)
        {
            throw new InvalidOperationException(
                $"{upload.FileName} is larger than the {_storage.MaxUploadMegabytes} MB limit for a supporting document.");
        }

        var detail = await projects.GetDetailAsync(projectId, cancellationToken)
                     ?? throw new InvalidOperationException($"Project {projectId} was not found.");

        var stored = await documents.SaveAsync(
            upload.Content, upload.FileName, upload.ContentType, detail.Project.ProjectCode, cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Attachment_Add")
            .With("ProjectId", projectId)
            .With("ProjectUpdateId", projectUpdateId)
            .With("Scope", scope)
            .With("BlobName", stored.BlobName)
            .With("FileName", stored.FileName)
            .With("ContentType", stored.ContentType)
            .With("SizeBytes", stored.SizeBytes)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        var idParameter = command.Output("AttachmentId", SqlDbType.Int);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new Attachment
        {
            Id = idParameter.Value is int id ? id : 0,
            Scope = scope,
            ProjectId = projectId,
            ProjectUpdateId = projectUpdateId,
            BlobName = stored.BlobName,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            UploadedBy = user.ToPersonRef()
        };
    }

    public async Task<IReadOnlyList<Attachment>> GetForProjectAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanViewAsync(projectId, cancellationToken))
        {
            return Array.Empty<Attachment>();
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Attachment_GetForProject")
            .With("ProjectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(ProjectService.ReadAttachment, cancellationToken);
    }

    public async Task<(Attachment Attachment, Uri? ReadLink)?> ResolveAsync(
        int attachmentId, CancellationToken cancellationToken = default)
    {
        Attachment attachment;

        await using (var connection = await connections.OpenAsync(cancellationToken))
        {
            await using var command = Db.Proc(connection, "pmo.usp_Attachment_GetById")
                .With("AttachmentId", attachmentId);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            attachment = ProjectService.ReadAttachment(reader);
        }

        // The access check is made against the project, after the row is read: a document is no
        // more visible than the project it belongs to.
        if (!await access.CanViewAsync(attachment.ProjectId, cancellationToken))
        {
            return null;
        }

        var link = await documents.TryGetReadLinkAsync(
            attachment.BlobName, TimeSpan.FromMinutes(10), cancellationToken);

        return (attachment, link);
    }
}
