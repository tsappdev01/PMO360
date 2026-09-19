using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using PMO360.Application.Abstractions;
using PMO360.Application.Dtos;
using PMO360.Application.Options;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Domain.Entities;
using PMO360.Domain.Enums;
using PMO360.Infrastructure.Data;

namespace PMO360.Infrastructure.Services;

public sealed class ProjectService(
    ISqlConnectionFactory connections,
    IProjectAccessService access,
    ICurrentUser user,
    IClock clock,
    IOptions<PortalOptions> portalOptions) : IProjectService
{
    private readonly PortalOptions _portal = portalOptions.Value;

    /// <summary>
    /// usp_Portfolio_Search carries the total row count on every row (COUNT(*) OVER()), so one
    /// call answers both "which rows" and "how many in total".
    /// </summary>
    public async Task<PortfolioPage> GetPortfolioAsync(
        PortfolioFilter filter, int skip = 0, int? take = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = SearchCommand(connection, filter, skip, take ?? _portal.ListPageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<PortfolioRow>();
        var total = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt("TotalRows");
            rows.Add(new PortfolioRow(
                reader.GetInt("ProjectId"),
                reader.GetString("ProjectCode"),
                reader.GetString("Name"),
                reader.GetString("OwnerName"),
                reader.GetStringOrNull("ConsultantName"),
                reader.GetEnum<ProjectStatus>("StatusId"),
                reader.GetInt("ProgressPercent"),
                reader.GetStringOrNull("PhaseName"),
                reader.GetStringOrNull("NextMilestoneName"),
                reader.GetDateOrNull("NextMilestoneDue"),
                reader.GetBool("NextMilestoneOverdue"),
                reader.GetDateOrNull("LastUpdateDate"),
                reader.GetBool("IsNotReported"),
                reader.GetBool("AttentionRequired"),
                reader.GetInt("OpenHighRisks"),
                reader.GetEnum<ProjectPriority>("PriorityId"),
                reader.GetString("ReportingEntityName")));
        }

        return new PortfolioPage(rows, total);
    }

    public async Task<ProjectDetailModel?> GetDetailAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_GetDetail")
            .With("ProjectId", projectId)
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("UpdateCycleDays", _portal.UpdateCycleDays);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Result set 1 — the project. Nothing at all means out of scope or not found.
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var project = ReadProject(reader);
        var isNotReported = reader.GetBool("IsNotReported");

        await reader.NextResultAsync(cancellationToken);
        var milestones = await reader.ReadAllAsync(ReadMilestone, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        var risks = await reader.ReadAllAsync(ReadRisk, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        var history = await reader.ReadAllAsync(ReadUpdate, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        var attachments = await reader.ReadAllAsync(ReadAttachment, cancellationToken);

        project.Milestones = milestones;
        project.RisksAndIssues = risks;
        project.Updates = history;

        // FR-07. The procedure returns milestones by planned date, so the first incomplete one
        // is the next milestone — the same row the dashboard derives.
        var next = milestones.FirstOrDefault(m => m.IsIncomplete);

        return new ProjectDetailModel(
            project,
            milestones,
            risks,
            history,
            attachments,
            next,
            milestones.Count(m => m.Status == MilestoneStatus.Completed),
            isNotReported,
            clock.Today);
    }

    public async Task<Project> CreateAsync(
        CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        access.EnsureCanAdminister();

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_Create")
            .With("Name", request.Name)
            .With("Description", request.Description)
            .With("ReportingEntityId", request.ReportingEntityId)
            .With("DepartmentId", request.DepartmentId)
            .With("ConsultantId", request.ConsultantId)
            .With("CurrentPhaseId", request.CurrentPhaseId)
            .WithPerson("ProjectOwner", request.ProjectOwner)
            .WithPerson("BusinessOwner", request.BusinessOwner)
            .WithPerson("ProjectManager", request.ProjectManager)
            .WithPerson("Sponsor", request.Sponsor)
            .With("PriorityId", request.Priority)
            .With("StartDate", request.StartDate)
            .With("TargetCompletionDate", request.TargetCompletionDate)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        var idParameter = command.Output("ProjectId", SqlDbType.Int);
        var codeParameter = command.Output("ProjectCode", SqlDbType.VarChar, 20);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new Project
        {
            Id = (int)idParameter.Value,
            ProjectCode = (string)codeParameter.Value,
            Name = request.Name,
            ReportingEntityId = request.ReportingEntityId,
            DepartmentId = request.DepartmentId,
            ConsultantId = request.ConsultantId,
            ProjectOwner = request.ProjectOwner,
            BusinessOwner = request.BusinessOwner,
            ProjectManager = request.ProjectManager,
            Sponsor = request.Sponsor,
            Priority = request.Priority,
            StartDate = request.StartDate,
            TargetCompletionDate = request.TargetCompletionDate,
            CurrentPhaseId = request.CurrentPhaseId,
            Status = ProjectStatus.OnTrack,
            State = RecordState.Active,
            CreatedOn = clock.Now,
            CreatedBy = user.ToPersonRef()
        };
    }

    public async Task CloseAsync(
        int projectId, string closureNote, CancellationToken cancellationToken = default)
    {
        access.EnsureCanAdminister();

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_Close")
            .With("ProjectId", projectId)
            .With("ClosureNote", closureNote)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReopenAsync(int projectId, string reason, CancellationToken cancellationToken = default)
    {
        access.EnsureCanAdminister();

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_Reopen")
            .With("ProjectId", projectId)
            .With("Reason", reason)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateParticularsAsync(
        int projectId, CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        access.EnsureCanAdminister();

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Project_UpdateParticulars")
            .With("ProjectId", projectId)
            .With("Name", request.Name)
            .With("Description", request.Description)
            .With("ReportingEntityId", request.ReportingEntityId)
            .With("DepartmentId", request.DepartmentId)
            .With("ConsultantId", request.ConsultantId)
            .WithPerson("ProjectOwner", request.ProjectOwner)
            .WithPerson("BusinessOwner", request.BusinessOwner)
            .WithPerson("ProjectManager", request.ProjectManager)
            .WithPerson("Sponsor", request.Sponsor)
            .With("PriorityId", request.Priority)
            .With("StartDate", request.StartDate)
            .With("TargetCompletionDate", request.TargetCompletionDate)
            .With("UserObjectId", user.ObjectId)
            .With("UserName", user.DisplayName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OverdueMilestoneRow>> GetOverdueMilestonesAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_View_OverdueMilestones")
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("ReportingEntityId", filter.ReportingEntityId)
            .With("DepartmentId", filter.DepartmentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(r => new OverdueMilestoneRow(
            r.GetInt("MilestoneId"),
            r.GetInt("ProjectId"),
            r.GetString("ProjectCode"),
            r.GetString("ProjectName"),
            r.GetString("MilestoneName"),
            r.GetDate("PlannedDate"),
            r.GetDateOrNull("BaselineDate"),
            r.GetInt("DaysOverdue"),
            r.GetInt("CompletionPercent"),
            r.GetString("OwnerName")), cancellationToken);
    }

    public async Task<IReadOnlyList<RiskRow>> GetRisksAsync(
        PortfolioFilter filter, bool openHighOnly = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_View_Risks")
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("OpenHighOnly", openHighOnly)
            .With("ReportingEntityId", filter.ReportingEntityId)
            .With("DepartmentId", filter.DepartmentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(r => new RiskRow(
            r.GetInt("RiskId"),
            r.GetInt("ProjectId"),
            r.GetString("ProjectCode"),
            r.GetString("ProjectName"),
            r.GetEnum<RiskType>("TypeId"),
            r.GetString("Description"),
            r.GetEnum<RiskSeverity>("SeverityId"),
            r.GetString("OwnerName"),
            r.GetStringOrNull("Mitigation"),
            r.GetDateOrNull("DueDate"),
            r.GetEnum<RiskStatus>("StatusId"),
            r.GetBool("IsOverdue")), cancellationToken);
    }

    public async Task<IReadOnlyList<NotReportedRow>> GetNotReportedAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_View_NotReported")
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("UpdateCycleDays", _portal.UpdateCycleDays);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAllAsync(r => new NotReportedRow(
            r.GetInt("ProjectId"),
            r.GetString("ProjectCode"),
            r.GetString("ProjectName"),
            r.GetString("OwnerName"),
            r.GetEnum<ProjectStatus>("StatusId"),
            r.GetDateOrNull("LastUpdateDate"),
            r.GetInt("DaysSinceUpdate")), cancellationToken);
    }

    public async Task<ReportingCompliance> GetReportingComplianceAsync(
        PortfolioFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = Db.Proc(connection, "pmo.usp_Report_Compliance")
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("UpdateCycleDays", _portal.UpdateCycleDays);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new ReportingCompliance(
                reader.GetInt("ActiveProjects"), reader.GetInt("ReportedInCycle"), reader.GetInt("NotReported"))
            : new ReportingCompliance(0, 0, 0);
    }

    private SqlCommand SearchCommand(SqlConnection connection, PortfolioFilter filter, int skip, int take) =>
        Db.Proc(connection, "pmo.usp_Portfolio_Search")
            .With("UserObjectId", user.ObjectId)
            .With("CanSeeAll", access.CanSeeWholePortfolio)
            .With("Today", clock.Today)
            .With("UpdateCycleDays", _portal.UpdateCycleDays)
            .With("View", (byte)filter.View)
            .With("ReportingEntityId", filter.ReportingEntityId)
            .With("DepartmentId", filter.DepartmentId)
            .With("StatusId", filter.Status)
            .With("PriorityId", filter.Priority)
            .With("OwnerObjectId", filter.OwnerObjectId)
            .With("ConsultantId", filter.ConsultantId)
            .With("SearchTerm", string.IsNullOrWhiteSpace(filter.SearchTerm) ? null : filter.SearchTerm.Trim())
            .With("MyProjectsOnly", filter.MyProjectsOnly)
            .With("Skip", skip)
            .With("Take", take);

    internal static Project ReadProject(SqlDataReader r) => new()
    {
        Id = r.GetInt("ProjectId"),
        ProjectCode = r.GetString("ProjectCode"),
        Name = r.GetString("Name"),
        Description = r.GetStringOrNull("Description"),
        ReportingEntityId = r.GetInt("ReportingEntityId"),
        ReportingEntity = new ReportingEntity { Id = r.GetInt("ReportingEntityId"), Name = r.GetString("ReportingEntityName") },
        DepartmentId = r.GetInt("DepartmentId"),
        Department = new Department { Id = r.GetInt("DepartmentId"), Name = r.GetString("DepartmentName") },
        ConsultantId = r.GetIntOrNull("ConsultantId"),
        Consultant = r.GetIntOrNull("ConsultantId") is { } cid
            ? new Consultant { Id = cid, Name = r.GetString("ConsultantName") }
            : null,
        CurrentPhaseId = r.GetIntOrNull("CurrentPhaseId"),
        CurrentPhase = r.GetIntOrNull("CurrentPhaseId") is { } phid
            ? new Phase { Id = phid, Name = r.GetString("PhaseName") }
            : null,
        ProjectOwner = r.GetPerson("ProjectOwner"),
        BusinessOwner = r.GetPerson("BusinessOwner"),
        ProjectManager = r.GetPerson("ProjectManager"),
        Sponsor = r.GetPerson("Sponsor"),
        Status = r.GetEnum<ProjectStatus>("StatusId"),
        ProgressPercent = r.GetInt("ProgressPercent"),
        Priority = r.GetEnum<ProjectPriority>("PriorityId"),
        StartDate = r.GetDate("StartDate"),
        TargetCompletionDate = r.GetDate("TargetCompletionDate"),
        KeyUpdate = r.GetStringOrNull("KeyUpdate"),
        NextAction = r.GetStringOrNull("NextAction"),
        NextActionOwner = r.GetStringOrNull("NextActionOwnerDisplayName") is { } na
            ? new PersonRef(na)
            : null,
        NextActionDueDate = r.GetDateOrNull("NextActionDueDate"),
        AttentionRequired = r.GetBool("AttentionRequired"),
        AttentionReason = r.GetStringOrNull("AttentionReason"),
        LastUpdateDate = r.GetDateOrNull("LastUpdateDate"),
        LastUpdatedBy = r.GetStringOrNull("LastUpdatedByDisplayName") is { } lu ? new PersonRef(lu) : null,
        State = r.GetEnum<RecordState>("StateId"),
        ClosedOn = r.GetTimestampOrNull("ClosedOn"),
        ClosureNote = r.GetStringOrNull("ClosureNote"),
        CreatedOn = r.GetTimestamp("CreatedOn"),
        CreatedBy = new PersonRef(r.GetString("CreatedByDisplayName"))
    };

    internal static Milestone ReadMilestone(SqlDataReader r) => new()
    {
        Id = r.GetInt("MilestoneId"),
        ProjectId = r.GetInt("ProjectId"),
        Name = r.GetString("Name"),
        PlannedDate = r.GetDate("PlannedDate"),
        BaselineDate = r.GetDateOrNull("BaselineDate"),
        ActualDate = r.GetDateOrNull("ActualDate"),
        Status = r.GetEnum<MilestoneStatus>("StatusId"),
        CompletionPercent = r.GetInt("CompletionPercent"),
        Owner = r.GetPerson("Owner"),
        CreatedOn = r.GetTimestamp("CreatedOn"),
        CreatedBy = new PersonRef(r.GetString("CreatedByDisplayName")),
        ModifiedOn = r.GetTimestampOrNull("ModifiedOn"),
        ModifiedBy = r.GetStringOrNull("ModifiedByDisplayName") is { } m ? new PersonRef(m) : null
    };

    internal static RiskIssue ReadRisk(SqlDataReader r) => new()
    {
        Id = r.GetInt("RiskId"),
        ProjectId = r.GetInt("ProjectId"),
        Type = r.GetEnum<RiskType>("TypeId"),
        Description = r.GetString("Description"),
        Severity = r.GetEnum<RiskSeverity>("SeverityId"),
        Owner = r.GetPerson("Owner"),
        Mitigation = r.GetStringOrNull("Mitigation"),
        DueDate = r.GetDateOrNull("DueDate"),
        Status = r.GetEnum<RiskStatus>("StatusId"),
        ClosureNote = r.GetStringOrNull("ClosureNote"),
        ClosedOn = r.GetTimestampOrNull("ClosedOn"),
        ClosedBy = r.GetStringOrNull("ClosedByDisplayName") is { } cb ? new PersonRef(cb) : null,
        RaisedOn = r.GetTimestamp("RaisedOn"),
        RaisedBy = new PersonRef(r.GetString("RaisedByDisplayName")),
        ModifiedOn = r.GetTimestampOrNull("ModifiedOn"),
        ModifiedBy = r.GetStringOrNull("ModifiedByDisplayName") is { } mb ? new PersonRef(mb) : null
    };

    internal static ProjectUpdate ReadUpdate(SqlDataReader r) => new()
    {
        Id = r.GetInt("UpdateId"),
        ProjectId = r.GetInt("ProjectId"),
        UpdateDate = r.GetDate("UpdateDate"),
        Status = r.GetEnum<ProjectStatus>("StatusId"),
        ProgressPercent = r.GetInt("ProgressPercent"),
        PhaseId = r.GetIntOrNull("PhaseId"),
        Phase = r.GetIntOrNull("PhaseId") is { } pid ? new Phase { Id = pid, Name = r.GetString("PhaseName") } : null,
        MilestoneId = r.GetIntOrNull("MilestoneId"),
        KeyUpdate = r.GetStringOrNull("KeyUpdate"),
        Achievement = r.GetStringOrNull("Achievement"),
        NextAction = r.GetStringOrNull("NextAction"),
        NextActionOwner = r.GetStringOrNull("NextActionOwnerDisplayName") is { } na ? new PersonRef(na) : null,
        NextActionDueDate = r.GetDateOrNull("NextActionDueDate"),
        AttentionRequired = r.GetBool("AttentionRequired"),
        AttentionReason = r.GetStringOrNull("AttentionReason"),
        SubmittedBy = new PersonRef(r.GetString("SubmittedByDisplayName")),
        SubmittedOn = r.GetTimestamp("SubmittedOn"),
        PreviousStatus = r.GetEnumOrNull<ProjectStatus>("PreviousStatusId"),
        PreviousProgressPercent = r.GetIntOrNull("PreviousProgressPercent")
    };

    internal static Attachment ReadAttachment(SqlDataReader r) => new()
    {
        Id = r.GetInt("AttachmentId"),
        Scope = r.GetEnum<AttachmentScope>("Scope"),
        ProjectId = r.GetInt("ProjectId"),
        ProjectUpdateId = r.GetIntOrNull("ProjectUpdateId"),
        BlobName = r.GetString("BlobName"),
        FileName = r.GetString("FileName"),
        ContentType = r.GetString("ContentType"),
        SizeBytes = r.GetLong("SizeBytes"),
        UploadedOn = r.GetTimestamp("UploadedOn"),
        UploadedBy = new PersonRef(r.GetString("UploadedByDisplayName"))
    };
}
