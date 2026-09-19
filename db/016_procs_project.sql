/*  PMO360 — 011_procs_project.sql
    The project register: search, read, create, close, reopen and PMO maintenance.
    FR-01, FR-05, FR-17, FR-19, FR-21, FR-23, FR-25, AC-01, AC-05.

    Every read takes @UserObjectId and @CanSeeAll and filters through
    pmo.fn_VisibleProjects, so scope is applied in the database, not left to the caller.

    Re-runnable: CREATE OR ALTER throughout.
*/

/* ---------------------------------------------------------------------------
   FR-17, FR-21, FR-23, FR-25. One row per project for the portfolio table,
   including the derived next milestone and the open High severity count, so the
   dashboard does not issue a query per row.

   @View: 0 Active, 1 At risk and delayed, 2 Overdue milestones, 3 Open high
   risks, 4 Not reported, 5 Completed, 6 All (FR-23).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE pmo.usp_Portfolio_Search
    @UserObjectId      varchar(64),
    @CanSeeAll         bit,
    @Today             date,
    @UpdateCycleDays   int          = 7,
    @View              tinyint      = 0,
    @ReportingEntityId int          = NULL,
    @DepartmentId      int          = NULL,
    @StatusId          tinyint      = NULL,
    @PriorityId        tinyint      = NULL,
    @OwnerObjectId     varchar(64)  = NULL,
    @ConsultantId      int          = NULL,
    @SearchTerm        nvarchar(200) = NULL,
    @MyProjectsOnly    bit          = 0,
    @Skip              int          = 0,
    @Take              int          = 50
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NotReportedBefore date = DATEADD(DAY, -@UpdateCycleDays, @Today);
    DECLARE @Search nvarchar(210) = CASE WHEN @SearchTerm IS NULL THEN NULL ELSE N'%' + @SearchTerm + N'%' END;

    ;WITH visible AS (
        SELECT ProjectId FROM pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today)
    ),
    filtered AS (
        SELECT p.*
        FROM pmo.Project AS p
        INNER JOIN visible AS v ON v.ProjectId = p.Id
        WHERE
            /* View (FR-23). Active excludes completed and closed projects (FR-05). */
            (
                (@View = 0 AND p.StateId = 1 AND p.StatusId <> 5)
             OR (@View = 1 AND p.StateId = 1 AND p.StatusId IN (2, 3))
             OR (@View = 2 AND p.StateId = 1 AND EXISTS (
                    SELECT 1 FROM pmo.Milestone AS m
                    INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
                    WHERE m.ProjectId = p.Id AND ms.IsIncomplete = 1 AND m.PlannedDate < @Today))
             OR (@View = 3 AND p.StateId = 1 AND EXISTS (
                    SELECT 1 FROM pmo.RiskIssue AS r
                    WHERE r.ProjectId = p.Id AND r.StatusId = 1 AND r.SeverityId = 3))
             OR (@View = 4 AND p.StateId = 1 AND (p.LastUpdateDate IS NULL OR p.LastUpdateDate < @NotReportedBefore))
             OR (@View = 5 AND p.StatusId = 5)
             OR (@View = 6)
            )
            AND (@ReportingEntityId IS NULL OR p.ReportingEntityId = @ReportingEntityId)
            AND (@DepartmentId      IS NULL OR p.DepartmentId      = @DepartmentId)
            AND (@StatusId          IS NULL OR p.StatusId          = @StatusId)
            AND (@PriorityId        IS NULL OR p.PriorityId        = @PriorityId)
            AND (@OwnerObjectId     IS NULL OR p.ProjectOwnerObjectId = @OwnerObjectId)
            AND (@ConsultantId      IS NULL OR p.ConsultantId      = @ConsultantId)
            AND (
                    @MyProjectsOnly = 0
                 OR @UserObjectId IS NULL
                 OR p.ProjectManagerObjectId = @UserObjectId
                 OR EXISTS (SELECT 1 FROM pmo.ProjectAssignment AS a
                            WHERE a.ProjectId = p.Id AND a.PersonObjectId = @UserObjectId
                              AND (a.EndsOn IS NULL OR a.EndsOn >= @Today))
                )
            /* FR-25. Search by project name, ID, owner and consultant. */
            AND (
                    @Search IS NULL
                 OR p.Name LIKE @Search
                 OR p.ProjectCode LIKE @Search
                 OR p.ProjectOwnerDisplayName LIKE @Search
                 OR EXISTS (SELECT 1 FROM pmo.Consultant AS c WHERE c.Id = p.ConsultantId AND c.Name LIKE @Search)
                )
    )
    SELECT
        f.Id                  AS ProjectId,
        f.ProjectCode,
        f.Name,
        f.ProjectOwnerDisplayName AS OwnerName,
        c.Name                AS ConsultantName,
        f.StatusId,
        f.ProgressPercent,
        ph.Name               AS PhaseName,
        nm.Name               AS NextMilestoneName,
        nm.PlannedDate        AS NextMilestoneDue,
        CAST(CASE WHEN nm.PlannedDate IS NOT NULL AND nm.PlannedDate < @Today THEN 1 ELSE 0 END AS bit)
                              AS NextMilestoneOverdue,   -- FR-08
        f.LastUpdateDate,
        CAST(CASE WHEN f.StateId = 1
                   AND ISNULL(f.LastUpdateDate, CAST(f.CreatedOn AS date)) < @NotReportedBefore
                  THEN 1 ELSE 0 END AS bit)
                              AS IsNotReported,           -- BR-06
        f.AttentionRequired,
        (SELECT COUNT(*) FROM pmo.RiskIssue AS r
          WHERE r.ProjectId = f.Id AND r.StatusId = 1 AND r.SeverityId = 3)
                              AS OpenHighRisks,           -- FR-10
        f.PriorityId,
        e.Name                AS ReportingEntityName,
        COUNT(*) OVER ()      AS TotalRows
    FROM filtered AS f
    INNER JOIN pmo.ReportingEntity AS e ON e.Id = f.ReportingEntityId
    LEFT  JOIN pmo.Consultant      AS c ON c.Id = f.ConsultantId
    LEFT  JOIN pmo.Phase           AS ph ON ph.Id = f.CurrentPhaseId
    OUTER APPLY pmo.fn_NextMilestone(f.Id) AS nm
    /* Attention first, then the worst status, so what management must act on is at the top. */
    ORDER BY f.AttentionRequired DESC,
             CASE f.StatusId WHEN 3 THEN 0 WHEN 2 THEN 1 ELSE 2 END,
             f.Name
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END;
GO

/* FR-19 / AC-05. The project detail page, in one round trip: project, milestones,
   risks and issues, update history, attachments. Returns nothing at all when the
   project is outside the caller's scope. */
CREATE OR ALTER PROCEDURE pmo.usp_Project_GetDetail
    @ProjectId       int,
    @UserObjectId    varchar(64),
    @CanSeeAll       bit,
    @Today           date,
    @UpdateCycleDays int = 7
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today)
                   WHERE ProjectId = @ProjectId)
    BEGIN
        RETURN;
    END;

    SELECT
        p.Id AS ProjectId, p.ProjectCode, p.Name, p.Description,
        p.ReportingEntityId, e.Name AS ReportingEntityName,
        p.DepartmentId, d.Name AS DepartmentName,
        p.ConsultantId, c.Name AS ConsultantName,
        p.CurrentPhaseId, ph.Name AS PhaseName,
        p.ProjectOwnerObjectId, p.ProjectOwnerDisplayName, p.ProjectOwnerEmail,
        p.BusinessOwnerObjectId, p.BusinessOwnerDisplayName, p.BusinessOwnerEmail,
        p.ProjectManagerObjectId, p.ProjectManagerDisplayName, p.ProjectManagerEmail,
        p.SponsorObjectId, p.SponsorDisplayName, p.SponsorEmail,
        p.StatusId, p.ProgressPercent, p.PriorityId,
        p.StartDate, p.TargetCompletionDate,
        p.KeyUpdate, p.NextAction, p.NextActionOwnerDisplayName, p.NextActionDueDate,
        p.AttentionRequired, p.AttentionReason,
        p.LastUpdateDate, p.LastUpdatedByDisplayName,
        p.StateId, p.ClosedOn, p.ClosureNote,
        p.CreatedOn, p.CreatedByDisplayName,
        CAST(CASE WHEN p.StateId = 1
                   AND ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date))
                       < DATEADD(DAY, -@UpdateCycleDays, @Today)
                  THEN 1 ELSE 0 END AS bit) AS IsNotReported
    FROM pmo.Project AS p
    INNER JOIN pmo.ReportingEntity AS e ON e.Id = p.ReportingEntityId
    INNER JOIN pmo.Department AS d ON d.Id = p.DepartmentId
    LEFT JOIN pmo.Consultant AS c ON c.Id = p.ConsultantId
    LEFT JOIN pmo.Phase AS ph ON ph.Id = p.CurrentPhaseId
    WHERE p.Id = @ProjectId;

    EXEC pmo.usp_Milestone_GetForProject @ProjectId = @ProjectId;
    EXEC pmo.usp_Risk_GetForProject @ProjectId = @ProjectId;
    EXEC pmo.usp_ProjectUpdate_GetHistory @ProjectId = @ProjectId;
    EXEC pmo.usp_Attachment_GetForProject @ProjectId = @ProjectId;
END;
GO

/* FR-05, AC-01. PMO only — the application checks the role, and the project code is
   allocated here so two people creating a project at once cannot be handed the same one. */
CREATE OR ALTER PROCEDURE pmo.usp_Project_Create
    @Name                  nvarchar(250),
    @Description           nvarchar(2000) = NULL,
    @ReportingEntityId     int,
    @DepartmentId          int,
    @ConsultantId          int = NULL,
    @CurrentPhaseId        int = NULL,
    @ProjectOwnerObjectId  varchar(64) = NULL,
    @ProjectOwnerDisplayName nvarchar(200),
    @ProjectOwnerEmail     nvarchar(320) = NULL,
    @BusinessOwnerObjectId varchar(64) = NULL,
    @BusinessOwnerDisplayName nvarchar(200),
    @BusinessOwnerEmail    nvarchar(320) = NULL,
    @ProjectManagerObjectId varchar(64) = NULL,
    @ProjectManagerDisplayName nvarchar(200),
    @ProjectManagerEmail   nvarchar(320) = NULL,
    @SponsorObjectId       varchar(64) = NULL,
    @SponsorDisplayName    nvarchar(200),
    @SponsorEmail          nvarchar(320) = NULL,
    @PriorityId            tinyint,
    @StartDate             date,
    @TargetCompletionDate  date,
    @UserObjectId          varchar(64) = NULL,
    @UserName              nvarchar(200),
    @ProjectId             int OUTPUT,
    @ProjectCode           varchar(20) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TargetCompletionDate < @StartDate
    BEGIN
        ;THROW 50010, 'The target completion date cannot be before the start date.', 1;
    END;

    BEGIN TRANSACTION;

        /* Serialise code allocation: the next number is read under an update lock, so two
           concurrent creates cannot both read the same highest code. */
        DECLARE @Next int =
        (
            SELECT ISNULL(MAX(TRY_CAST(RIGHT(ProjectCode, LEN(ProjectCode) - 4) AS int)), 0) + 1
            FROM pmo.Project WITH (UPDLOCK, HOLDLOCK)
            WHERE ProjectCode LIKE 'PRJ-%'
        );

        SET @ProjectCode = 'PRJ-' + RIGHT('0000' + CAST(@Next AS varchar(10)), 4);

        INSERT INTO pmo.Project
        (
            ProjectCode, Name, Description, ReportingEntityId, DepartmentId, ConsultantId, CurrentPhaseId,
            ProjectOwnerObjectId, ProjectOwnerDisplayName, ProjectOwnerEmail,
            BusinessOwnerObjectId, BusinessOwnerDisplayName, BusinessOwnerEmail,
            ProjectManagerObjectId, ProjectManagerDisplayName, ProjectManagerEmail,
            SponsorObjectId, SponsorDisplayName, SponsorEmail,
            StatusId, ProgressPercent, PriorityId, StartDate, TargetCompletionDate,
            AttentionRequired, StateId, CreatedOn, CreatedByObjectId, CreatedByDisplayName
        )
        VALUES
        (
            @ProjectCode, LTRIM(RTRIM(@Name)), @Description, @ReportingEntityId, @DepartmentId, @ConsultantId, @CurrentPhaseId,
            @ProjectOwnerObjectId, @ProjectOwnerDisplayName, @ProjectOwnerEmail,
            @BusinessOwnerObjectId, @BusinessOwnerDisplayName, @BusinessOwnerEmail,
            @ProjectManagerObjectId, @ProjectManagerDisplayName, @ProjectManagerEmail,
            @SponsorObjectId, @SponsorDisplayName, @SponsorEmail,
            1 /* On Track */, 0, @PriorityId, @StartDate, @TargetCompletionDate,
            0, 1 /* Active */, SYSDATETIMEOFFSET(), @UserObjectId, @UserName
        );

        SET @ProjectId = SCOPE_IDENTITY();

        /* The manager is assigned as well as named, so scoping still works if the project is
           later handed to someone else. */
        INSERT INTO pmo.ProjectAssignment
            (ProjectId, PersonObjectId, PersonDisplayName, PersonEmail, RoleId, AssignedOn, AssignedByDisplayName)
        VALUES
            (@ProjectId, @ProjectManagerObjectId, @ProjectManagerDisplayName, @ProjectManagerEmail,
             1 /* ProjectManager */, SYSDATETIMEOFFSET(), @UserName);

        EXEC pmo.usp_Audit_Write 'Project', @ProjectCode, 'Created', @UserObjectId, @UserName, @Name;

    COMMIT TRANSACTION;
END;
GO

/* FR-05, BR-07. Closed, not deleted: the project leaves the default views and stays in reporting. */
CREATE OR ALTER PROCEDURE pmo.usp_Project_Close
    @ProjectId    int,
    @ClosureNote  nvarchar(2000),
    @UserObjectId varchar(64) = NULL,
    @UserName     nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    IF @ClosureNote IS NULL OR LEN(LTRIM(@ClosureNote)) = 0
    BEGIN
        ;THROW 50011, 'A closure note is required when a project is closed.', 1;
    END;

    UPDATE pmo.Project
    SET StateId = 2,
        ClosedOn = SYSDATETIMEOFFSET(),
        ClosedByObjectId = @UserObjectId,
        ClosedByDisplayName = @UserName,
        ClosureNote = LTRIM(RTRIM(@ClosureNote))
    WHERE Id = @ProjectId;

    EXEC pmo.usp_Audit_Write 'Project', @ProjectId, 'Closed', @UserObjectId, @UserName, @ClosureNote;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_Project_Reopen
    @ProjectId    int,
    @Reason       nvarchar(2000),
    @UserObjectId varchar(64) = NULL,
    @UserName     nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    IF @Reason IS NULL OR LEN(LTRIM(@Reason)) = 0
    BEGIN
        ;THROW 50012, 'A reason is required when a project is reopened.', 1;
    END;

    /* The closure note is kept and extended, so the record still shows it was once closed. */
    UPDATE pmo.Project
    SET StateId = 1,
        ClosedOn = NULL,
        ClosedByObjectId = NULL,
        ClosedByDisplayName = NULL,
        ClosureNote = LTRIM(RTRIM(ISNULL(ClosureNote + NCHAR(10), N'')))
                      + N'Reopened ' + FORMAT(SYSDATETIMEOFFSET(), 'dd-MMM-yyyy') + N': ' + LTRIM(RTRIM(@Reason))
    WHERE Id = @ProjectId;

    EXEC pmo.usp_Audit_Write 'Project', @ProjectId, 'Reopened', @UserObjectId, @UserName, @Reason;
END;
GO

/* PMO maintenance of the particulars the update form does not carry. Status, progress, phase
   and the attention flag are deliberately absent: those change only through an update (FR-11). */
CREATE OR ALTER PROCEDURE pmo.usp_Project_UpdateParticulars
    @ProjectId             int,
    @Name                  nvarchar(250),
    @Description           nvarchar(2000) = NULL,
    @ReportingEntityId     int,
    @DepartmentId          int,
    @ConsultantId          int = NULL,
    @ProjectOwnerObjectId  varchar(64) = NULL,
    @ProjectOwnerDisplayName nvarchar(200),
    @ProjectOwnerEmail     nvarchar(320) = NULL,
    @BusinessOwnerObjectId varchar(64) = NULL,
    @BusinessOwnerDisplayName nvarchar(200),
    @BusinessOwnerEmail    nvarchar(320) = NULL,
    @ProjectManagerObjectId varchar(64) = NULL,
    @ProjectManagerDisplayName nvarchar(200),
    @ProjectManagerEmail   nvarchar(320) = NULL,
    @SponsorObjectId       varchar(64) = NULL,
    @SponsorDisplayName    nvarchar(200),
    @SponsorEmail          nvarchar(320) = NULL,
    @PriorityId            tinyint,
    @StartDate             date,
    @TargetCompletionDate  date,
    @UserObjectId          varchar(64) = NULL,
    @UserName              nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TargetCompletionDate < @StartDate
    BEGIN
        ;THROW 50010, 'The target completion date cannot be before the start date.', 1;
    END;

    BEGIN TRANSACTION;

        DECLARE @PreviousManager varchar(64) =
            (SELECT ProjectManagerObjectId FROM pmo.Project WHERE Id = @ProjectId);

        UPDATE pmo.Project
        SET Name = LTRIM(RTRIM(@Name)),
            Description = @Description,
            ReportingEntityId = @ReportingEntityId,
            DepartmentId = @DepartmentId,
            ConsultantId = @ConsultantId,
            ProjectOwnerObjectId = @ProjectOwnerObjectId,
            ProjectOwnerDisplayName = @ProjectOwnerDisplayName,
            ProjectOwnerEmail = @ProjectOwnerEmail,
            BusinessOwnerObjectId = @BusinessOwnerObjectId,
            BusinessOwnerDisplayName = @BusinessOwnerDisplayName,
            BusinessOwnerEmail = @BusinessOwnerEmail,
            ProjectManagerObjectId = @ProjectManagerObjectId,
            ProjectManagerDisplayName = @ProjectManagerDisplayName,
            ProjectManagerEmail = @ProjectManagerEmail,
            SponsorObjectId = @SponsorObjectId,
            SponsorDisplayName = @SponsorDisplayName,
            SponsorEmail = @SponsorEmail,
            PriorityId = @PriorityId,
            StartDate = @StartDate,
            TargetCompletionDate = @TargetCompletionDate
        WHERE Id = @ProjectId;

        /* A new manager needs an assignment, or they would lose sight of their own project. */
        IF @ProjectManagerObjectId IS NOT NULL
           AND (@PreviousManager IS NULL OR @PreviousManager <> @ProjectManagerObjectId)
           AND NOT EXISTS (SELECT 1 FROM pmo.ProjectAssignment
                           WHERE ProjectId = @ProjectId
                             AND PersonObjectId = @ProjectManagerObjectId
                             AND RoleId = 1)
        BEGIN
            INSERT INTO pmo.ProjectAssignment
                (ProjectId, PersonObjectId, PersonDisplayName, PersonEmail, RoleId, AssignedOn, AssignedByDisplayName)
            VALUES
                (@ProjectId, @ProjectManagerObjectId, @ProjectManagerDisplayName, @ProjectManagerEmail,
                 1, SYSDATETIMEOFFSET(), @UserName);
        END;

        EXEC pmo.usp_Audit_Write 'Project', @ProjectId, 'ParticularsAmended', @UserObjectId, @UserName, @Name;

    COMMIT TRANSACTION;
END;
GO

/* Assignments: who may see and contribute to a project (section 6). */
CREATE OR ALTER PROCEDURE pmo.usp_ProjectAssignment_Save
    @ProjectId        int,
    @PersonObjectId   varchar(64) = NULL,
    @PersonDisplayName nvarchar(200),
    @PersonEmail      nvarchar(320) = NULL,
    @RoleId           tinyint,
    @EndsOn           date = NULL,
    @UserObjectId     varchar(64) = NULL,
    @UserName         nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM pmo.ProjectAssignment
               WHERE ProjectId = @ProjectId AND PersonObjectId = @PersonObjectId AND RoleId = @RoleId)
    BEGIN
        UPDATE pmo.ProjectAssignment
        SET PersonDisplayName = @PersonDisplayName,
            PersonEmail = @PersonEmail,
            EndsOn = @EndsOn
        WHERE ProjectId = @ProjectId AND PersonObjectId = @PersonObjectId AND RoleId = @RoleId;
    END
    ELSE
    BEGIN
        INSERT INTO pmo.ProjectAssignment
            (ProjectId, PersonObjectId, PersonDisplayName, PersonEmail, RoleId, AssignedOn, AssignedByDisplayName)
        VALUES
            (@ProjectId, @PersonObjectId, @PersonDisplayName, @PersonEmail, @RoleId, SYSDATETIMEOFFSET(), @UserName);
    END;

    EXEC pmo.usp_Audit_Write 'ProjectAssignment', @ProjectId, 'Saved', @UserObjectId, @UserName, @PersonDisplayName;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_ProjectAssignment_GetForProject
    @ProjectId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT a.Id, a.ProjectId, a.PersonObjectId, a.PersonDisplayName, a.PersonEmail,
           a.RoleId, r.Name AS RoleName, a.AssignedOn, a.AssignedByDisplayName, a.EndsOn
    FROM pmo.ProjectAssignment AS a
    INNER JOIN pmo.AssignmentRole AS r ON r.Id = a.RoleId
    WHERE a.ProjectId = @ProjectId
    ORDER BY r.Id, a.PersonDisplayName;
END;
GO

/* Can this person submit updates and maintain milestones and risks here? Management and a
   project owner may read everything on their projects but do not submit on the manager's
   behalf, so contributing is narrower than viewing (section 6). */
CREATE OR ALTER PROCEDURE pmo.usp_Project_CanContribute
    @ProjectId    int,
    @UserObjectId varchar(64),
    @IsPmoAdmin   bit,
    @Today        date,
    @CanContribute bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @IsPmoAdmin = 1
    BEGIN
        SET @CanContribute = 1;
        RETURN;
    END;

    SET @CanContribute = CASE WHEN EXISTS (
        SELECT 1
        FROM pmo.Project AS p
        WHERE p.Id = @ProjectId
          AND @UserObjectId IS NOT NULL
          AND (
                p.ProjectManagerObjectId = @UserObjectId
             OR EXISTS (SELECT 1 FROM pmo.ProjectAssignment AS a
                        WHERE a.ProjectId = p.Id
                          AND a.PersonObjectId = @UserObjectId
                          AND a.RoleId IN (1 /* ProjectManager */, 5 /* ConsultantStaff */)
                          AND (a.EndsOn IS NULL OR a.EndsOn >= @Today))
              )
    ) THEN 1 ELSE 0 END;
END;
GO
