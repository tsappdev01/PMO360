using Microsoft.AspNetCore.Mvc;
using PMO360.Application.Abstractions;
using PMO360.Application.Dtos;
using PMO360.Application.Services;
using PMO360.Domain.Enums;
using PMO360.Web.Identity;

namespace PMO360.Web.Endpoints;

/// <summary>
/// The two things the portal serves as files rather than as pages: a supporting document
/// (FR-26) and the portfolio export (FR-24). Both are plain HTTP endpoints so the browser
/// downloads them, rather than passing megabytes through the Blazor circuit.
/// </summary>
public static class PortalEndpoints
{
    public static void MapPortalEndpoints(this WebApplication app)
    {
        // FR-26. Access-checked against the project: a document is no more visible than the
        // project it belongs to.
        app.MapGet("/documents/{attachmentId:int}", async (
            int attachmentId,
            IAttachmentService attachments,
            IDocumentStore documents,
            CancellationToken cancellationToken) =>
        {
            var resolved = await attachments.ResolveAsync(attachmentId, cancellationToken);
            if (resolved is null)
            {
                return Results.NotFound();
            }

            var (attachment, readLink) = resolved.Value;

            // Where storage can issue one, the browser fetches the file from storage directly.
            if (readLink is not null)
            {
                return Results.Redirect(readLink.ToString());
            }

            var content = await documents.OpenAsync(attachment.BlobName, cancellationToken);
            return content is null
                ? Results.NotFound()
                : Results.File(content.Content, attachment.ContentType, attachment.FileName);
        })
        .RequireAuthorization(AuthorizationPolicies.PortalUser);

        // FR-24. The portfolio view as it is filtered, for meeting papers.
        app.MapGet("/export/portfolio", async (
            [FromQuery] int view,
            [FromQuery] int? entity,
            [FromQuery] int? department,
            [FromQuery] int? status,
            [FromQuery] int? consultant,
            [FromQuery] bool? mine,
            [FromQuery] string? search,
            IExportService export,
            CancellationToken cancellationToken) =>
        {
            var filter = new PortfolioFilter
            {
                View = Enum.IsDefined(typeof(PortfolioView), view) ? (PortfolioView)view : PortfolioView.Active,
                ReportingEntityId = entity,
                DepartmentId = department,
                Status = status is { } s && Enum.IsDefined(typeof(ProjectStatus), s) ? (ProjectStatus)s : null,
                ConsultantId = consultant,
                MyProjectsOnly = mine ?? false,
                SearchTerm = search
            };

            var workbook = await export.ExportPortfolioToExcelAsync(filter, cancellationToken);

            return Results.File(
                workbook,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"PMO360-portfolio-{DateTime.UtcNow:yyyy-MM-dd}.xlsx");
        })
        .RequireAuthorization(AuthorizationPolicies.PortalUser);
    }
}
