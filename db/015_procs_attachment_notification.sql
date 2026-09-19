/*  PMO360 — 015_procs_attachment_notification.sql
    Supporting documents (FR-26) and the notification machinery of section 5.3.

    The BRD's low-code design ran the notifications through Power Automate. Built on .NET the
    triggers, recipients and escalations are the same; what changes is that the portal raises
    them and a scheduled sweep catches the time-based ones. The procedures here supply the
    sweep with its candidates and keep the log that stops a notice going out twice.
*/

/* FR-26. The blob itself is in Azure Storage; this is the catalogue entry. */
CREATE OR ALTER PROCEDURE pmo.usp_Attachment_Add
    @ProjectId       int,
    @ProjectUpdateId int = NULL,
    @Scope           tinyint,
    @BlobName        nvarchar(400),
    @FileName        nvarchar(260),
    @ContentType     nvarchar(150),
    @SizeBytes       bigint,
    @UserObjectId    varchar(64) = NULL,
    @UserName        nvarchar(200),
    @AttachmentId    int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO pmo.Attachment
        (Scope, ProjectId, ProjectUpdateId, BlobName, FileName, ContentType, SizeBytes,
         UploadedOn, UploadedByObjectId, UploadedByDisplayName)
    VALUES
        (@Scope, @ProjectId, @ProjectUpdateId, @BlobName, @FileName, @ContentType, @SizeBytes,
         SYSDATETIMEOFFSET(), @UserObjectId, @UserName);

    SET @AttachmentId = SCOPE_IDENTITY();

    EXEC pmo.usp_Audit_Write 'Attachment', @AttachmentId, 'Uploaded', @UserObjectId, @UserName, @FileName;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_Attachment_GetForProject
    @ProjectId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id AS AttachmentId, Scope, ProjectId, ProjectUpdateId, BlobName, FileName,
           ContentType, SizeBytes, UploadedOn, UploadedByDisplayName
    FROM pmo.Attachment
    WHERE ProjectId = @ProjectId
    ORDER BY UploadedOn DESC;
END;
GO

/* Reads one attachment with its project, so the caller can access-check before serving it. */
CREATE OR ALTER PROCEDURE pmo.usp_Attachment_GetById
    @AttachmentId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id AS AttachmentId, Scope, ProjectId, ProjectUpdateId, BlobName, FileName,
           ContentType, SizeBytes, UploadedOn, UploadedByDisplayName
    FROM pmo.Attachment
    WHERE Id = @AttachmentId;
END;
GO

/* ---------------------------------------------------------------------------
   The notification log. @Claimed = 1 means this caller may send; 0 means the
   occasion has already been recorded, by an earlier run or by another instance,
   and the caller must not send. The unique index on DedupeKey does the work, so
   two App Service instances running the sweep at the same moment cannot both
   mail the business.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_Claim
    @DedupeKey   varchar(200),
    @Kind        tinyint,
    @ProjectId   int = NULL,
    @MilestoneId int = NULL,
    @RiskIssueId int = NULL,
    @Recipients  nvarchar(2000),
    @Subject     nvarchar(400),
    @NotificationId bigint OUTPUT,
    @Claimed     bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @Claimed = 0;
    SET @NotificationId = NULL;

    BEGIN TRY
        INSERT INTO pmo.NotificationLog
            (Kind, ProjectId, MilestoneId, RiskIssueId, Recipients, Subject, SentOn, Succeeded, DedupeKey)
        VALUES
            (@Kind, @ProjectId, @MilestoneId, @RiskIssueId, @Recipients, @Subject,
             SYSDATETIMEOFFSET(), 0, @DedupeKey);

        SET @NotificationId = SCOPE_IDENTITY();
        SET @Claimed = 1;
    END TRY
    BEGIN CATCH
        /* 2601 / 2627: the occasion is already logged. Not an error — somebody got there first. */
        IF ERROR_NUMBER() NOT IN (2601, 2627)
        BEGIN
            ;THROW;
        END;
    END CATCH;
END;
GO

/* Records how the send went, against the row claimed above. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_Complete
    @NotificationId bigint,
    @Succeeded      bit,
    @Error          nvarchar(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE pmo.NotificationLog
    SET Succeeded = @Succeeded,
        Error = @Error,
        SentOn = SYSDATETIMEOFFSET()
    WHERE Id = @NotificationId;
END;
GO

/* WF-04. Milestones due within the reminder window, or already overdue. Overdue notices repeat
   daily, so the sweep asks for candidates every day and the dedupe key carries the date. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_GetMilestoneReminders
    @Today        date,
    @ReminderDays int = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WindowEnd date = DATEADD(DAY, @ReminderDays, @Today);

    SELECT m.Id AS MilestoneId, m.Name AS MilestoneName, m.PlannedDate,
           m.OwnerDisplayName, m.OwnerEmail,
           p.Id AS ProjectId, p.ProjectCode, p.Name AS ProjectName,
           p.ProjectManagerDisplayName, p.ProjectManagerEmail,
           p.ProjectOwnerDisplayName, p.ProjectOwnerEmail,
           CAST(CASE WHEN m.PlannedDate < @Today THEN 1 ELSE 0 END AS bit) AS IsOverdue
    FROM pmo.Milestone AS m
    INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
    INNER JOIN pmo.Project AS p ON p.Id = m.ProjectId
    WHERE ms.IsIncomplete = 1
      AND p.StateId = 1
      AND p.StatusId <> 5
      AND m.PlannedDate <= @WindowEnd
    ORDER BY m.PlannedDate, p.Name;
END;
GO

/* WF-05. Two stages: a reminder to the project manager at [7] days, then an escalation to the
   Project Owner at [14]. Stage is returned so the caller does not have to work it out again. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_GetUpdateReminders
    @Today           date,
    @ReminderDays    int = 7,
    @EscalationDays  int = 14
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.Id AS ProjectId, p.ProjectCode, p.Name AS ProjectName,
           p.ProjectManagerDisplayName, p.ProjectManagerEmail,
           p.ProjectOwnerDisplayName, p.ProjectOwnerEmail,
           p.LastUpdateDate,
           DATEDIFF(DAY, ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date)), @Today) AS DaysSinceUpdate,
           CASE WHEN DATEDIFF(DAY, ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date)), @Today) >= @EscalationDays
                THEN 2 ELSE 1 END AS Stage
    FROM pmo.Project AS p
    WHERE p.StateId = 1
      AND p.StatusId <> 5
      AND DATEDIFF(DAY, ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date)), @Today) >= @ReminderDays
    ORDER BY DaysSinceUpdate DESC;
END;
GO

/* WF-07. The scheduled portfolio digest: counts by status, the exceptions, and what is coming
   up. Four result sets, read straight into the mail body. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_GetDigest
    @Today              date,
    @UpdateCycleDays    int = 7,
    @UpcomingWindowDays int = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Cutoff date = DATEADD(DAY, -@UpdateCycleDays, @Today);
    DECLARE @WindowEnd date = DATEADD(DAY, @UpcomingWindowDays, @Today);

    /* 1 — counts by status across the whole active portfolio. */
    SELECT st.Id AS StatusId, st.Name AS StatusName, COUNT(p.Id) AS ProjectCount
    FROM pmo.ProjectStatus AS st
    LEFT JOIN pmo.Project AS p ON p.StatusId = st.Id AND p.StateId = 1 AND p.StatusId <> 5
    GROUP BY st.Id, st.Name, st.SortOrder
    ORDER BY st.SortOrder;

    /* 2 — the exceptions: at risk, delayed, or flagged for attention. */
    SELECT p.Id AS ProjectId, p.ProjectCode, p.Name AS ProjectName, p.StatusId, st.Name AS StatusName,
           p.ProgressPercent, p.ProjectOwnerDisplayName, p.AttentionRequired,
           ISNULL(p.AttentionReason, p.KeyUpdate) AS Note
    FROM pmo.Project AS p
    INNER JOIN pmo.ProjectStatus AS st ON st.Id = p.StatusId
    WHERE p.StateId = 1 AND (p.StatusId IN (2, 3) OR p.AttentionRequired = 1)
    ORDER BY CASE p.StatusId WHEN 3 THEN 0 WHEN 2 THEN 1 ELSE 2 END, p.Name;

    /* 3 — milestones due in the coming week, and anything already overdue. */
    SELECT m.Id AS MilestoneId, p.Id AS ProjectId, p.Name AS ProjectName, m.Name AS MilestoneName,
           m.PlannedDate, m.OwnerDisplayName,
           CAST(CASE WHEN m.PlannedDate < @Today THEN 1 ELSE 0 END AS bit) AS IsOverdue
    FROM pmo.Milestone AS m
    INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
    INNER JOIN pmo.Project AS p ON p.Id = m.ProjectId
    WHERE ms.IsIncomplete = 1 AND p.StateId = 1 AND m.PlannedDate <= @WindowEnd
    ORDER BY m.PlannedDate;

    /* 4 — reporting compliance, so the digest says who has not reported. */
    SELECT COUNT(*) AS ActiveProjects,
           SUM(CASE WHEN p.LastUpdateDate IS NOT NULL AND p.LastUpdateDate >= @Cutoff THEN 1 ELSE 0 END) AS ReportedInCycle,
           SUM(CASE WHEN p.LastUpdateDate IS NULL OR p.LastUpdateDate < @Cutoff THEN 1 ELSE 0 END) AS NotReported
    FROM pmo.Project AS p
    WHERE p.StateId = 1 AND p.StatusId <> 5;
END;
GO

/* The recipients a notification about one project goes to (section 5.3). Returned as a single
   row so the caller does not need to know the column names of pmo.Project. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_GetProjectRecipients
    @ProjectId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.Id AS ProjectId, p.ProjectCode, p.Name AS ProjectName,
           p.ProjectManagerDisplayName, p.ProjectManagerEmail,
           p.ProjectOwnerDisplayName, p.ProjectOwnerEmail,
           p.BusinessOwnerDisplayName, p.BusinessOwnerEmail,
           p.SponsorDisplayName, p.SponsorEmail,
           p.StatusId, st.Name AS StatusName, p.ProgressPercent,
           p.KeyUpdate, p.AttentionRequired, p.AttentionReason
    FROM pmo.Project AS p
    INNER JOIN pmo.ProjectStatus AS st ON st.Id = p.StatusId
    WHERE p.Id = @ProjectId;
END;
GO

/* Everything a WF-01 / WF-02 / WF-03 / WF-08 notification needs about one submitted update:
   what changed, and who is to be told. One row, so the caller makes one call. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_GetUpdateContext
    @UpdateId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.Id AS UpdateId, u.ProjectId, p.ProjectCode, p.Name AS ProjectName,
           u.UpdateDate, u.StatusId, st.Name AS StatusName,
           u.PreviousStatusId, prev.Name AS PreviousStatusName,
           u.ProgressPercent, u.PreviousProgressPercent,
           u.KeyUpdate, u.Achievement, u.NextAction, u.NextActionDueDate,
           u.AttentionRequired, u.AttentionReason,
           u.SubmittedByDisplayName, u.SubmittedOn,
           p.ProjectManagerDisplayName, p.ProjectManagerEmail,
           p.ProjectOwnerDisplayName, p.ProjectOwnerEmail,
           p.BusinessOwnerDisplayName, p.BusinessOwnerEmail,
           p.SponsorDisplayName, p.SponsorEmail,
           e.Name AS ReportingEntityName
    FROM pmo.ProjectUpdate AS u
    INNER JOIN pmo.Project AS p ON p.Id = u.ProjectId
    INNER JOIN pmo.ReportingEntity AS e ON e.Id = p.ReportingEntityId
    INNER JOIN pmo.ProjectStatus AS st ON st.Id = u.StatusId
    LEFT JOIN pmo.ProjectStatus AS prev ON prev.Id = u.PreviousStatusId
    WHERE u.Id = @UpdateId;
END;
GO

/* WF-06. The risk as raised, with the project's recipients. */
CREATE OR ALTER PROCEDURE pmo.usp_Notification_GetRiskContext
    @RiskId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT r.Id AS RiskId, r.ProjectId, p.ProjectCode, p.Name AS ProjectName,
           t.Name AS TypeName, r.Description, sv.Name AS SeverityName,
           r.OwnerDisplayName, r.OwnerEmail, r.Mitigation, r.DueDate,
           r.RaisedByDisplayName, r.RaisedOn,
           p.ProjectManagerDisplayName, p.ProjectManagerEmail,
           p.ProjectOwnerDisplayName, p.ProjectOwnerEmail,
           p.SponsorDisplayName, p.SponsorEmail
    FROM pmo.RiskIssue AS r
    INNER JOIN pmo.Project AS p ON p.Id = r.ProjectId
    INNER JOIN pmo.RiskType AS t ON t.Id = r.TypeId
    INNER JOIN pmo.RiskSeverity AS sv ON sv.Id = r.SeverityId
    WHERE r.Id = @RiskId;
END;
GO
