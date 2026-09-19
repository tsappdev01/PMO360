using ClosedXML.Excel;
using PMO360.Application.Dtos;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Rules;

namespace PMO360.Infrastructure.Services;

/// <summary>
/// FR-24. The portfolio view exported to Excel for meeting papers. The one-page project status
/// report is produced by printing the project page — the browser's print-to-PDF gives the same
/// document without adding a PDF library and its licence to the estate.
/// </summary>
public sealed class ExportService(IProjectService projects, IClock clock) : IExportService
{
    public async Task<byte[]> ExportPortfolioToExcelAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default)
    {
        // Export the whole filtered set, not the page the user happens to be looking at.
        var page = await projects.GetPortfolioAsync(filter, 0, 5000, cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Portfolio");

        sheet.Cell(1, 1).Value = "PMO360 — portfolio position";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = $"As at {clock.Today:dd-MMM-yyyy}. {page.Total} project(s).";
        sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#64748B");

        string[] headers =
        [
            "Project ID", "Project", "Entity", "Owner", "Consultant", "Status", "Progress %",
            "Phase", "Next milestone", "Due", "Overdue", "Last update", "Reporting", "Attention",
            "Open high risks", "Priority"
        ];

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B3B6F");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var row = headerRow + 1;
        foreach (var project in page.Rows)
        {
            sheet.Cell(row, 1).Value = project.ProjectCode;
            sheet.Cell(row, 2).Value = project.Name;
            sheet.Cell(row, 3).Value = project.ReportingEntityName;
            sheet.Cell(row, 4).Value = project.OwnerName;
            sheet.Cell(row, 5).Value = project.ConsultantName ?? "";
            // FR-18: the status is stated in text here too, so a printed sheet does not rely on colour.
            sheet.Cell(row, 6).Value = ProjectStatusRules.DisplayName(project.Status);
            sheet.Cell(row, 7).Value = project.ProgressPercent;
            sheet.Cell(row, 8).Value = project.PhaseName ?? "";
            sheet.Cell(row, 9).Value = project.NextMilestoneName ?? "";
            sheet.Cell(row, 10).Value = project.NextMilestoneDue?.ToString("dd-MMM-yyyy") ?? "";
            sheet.Cell(row, 11).Value = project.NextMilestoneOverdue ? "Overdue" : "";
            sheet.Cell(row, 12).Value = project.LastUpdateDate?.ToString("dd-MMM-yyyy") ?? "Never";
            sheet.Cell(row, 13).Value = project.IsNotReported ? "Not Reported" : "Reported";
            sheet.Cell(row, 14).Value = project.AttentionRequired ? "Yes" : "";
            sheet.Cell(row, 15).Value = project.OpenHighRisks;
            sheet.Cell(row, 16).Value = project.Priority.ToString();

            sheet.Cell(row, 6).Style.Fill.BackgroundColor = StatusFill(project.Status);
            row++;
        }

        sheet.Range(headerRow, 1, Math.Max(headerRow, row - 1), headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static XLColor StatusFill(Domain.Enums.ProjectStatus status) => status switch
    {
        Domain.Enums.ProjectStatus.OnTrack => XLColor.FromHtml("#E6F4EC"),
        Domain.Enums.ProjectStatus.AtRisk => XLColor.FromHtml("#FCF1DC"),
        Domain.Enums.ProjectStatus.Delayed => XLColor.FromHtml("#FBE7E6"),
        Domain.Enums.ProjectStatus.OnHold => XLColor.FromHtml("#EEF1F4"),
        _ => XLColor.FromHtml("#E7EEF8")
    };
}
