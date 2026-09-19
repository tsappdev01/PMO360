/*  PMO360 — 013_procs_milestone_risk.sql
    Milestones (FR-06, FR-07, FR-08) and the risk and issue register (FR-09, FR-10).
    BR-07: nothing is deleted. A milestone that is no longer wanted is cancelled; a risk is
    closed with a note. There is no delete procedure in this file, by design.
*/

CREATE OR ALTER PROCEDURE pmo.usp_Milestone_GetForProject
    @ProjectId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT m.Id AS MilestoneId, m.ProjectId, m.Name, m.PlannedDate, m.BaselineDate, m.ActualDate,
           m.StatusId, s.Name AS StatusName, s.IsIncomplete,
           m.CompletionPercent,
           m.OwnerObjectId, m.OwnerDisplayName, m.OwnerEmail,
           m.CreatedOn, m.CreatedByDisplayName, m.ModifiedOn, m.ModifiedByDisplayName,
           /* Planned against baseline, for the slippage report (section 5.4). */
           CASE WHEN m.BaselineDate IS NULL THEN NULL
                ELSE DATEDIFF(DAY, m.BaselineDate, ISNULL(m.ActualDate, m.PlannedDate)) END AS SlippageDays
    FROM pmo.Milestone AS m
    INNER JOIN pmo.MilestoneStatus AS s ON s.Id = m.StatusId
    WHERE m.ProjectId = @ProjectId
    ORDER BY m.PlannedDate, m.Id;
END;
GO

/* FR-06. Adds or amends one milestone. The baseline is captured on first save and is not
   moved again, so slippage keeps something to measure against. */
CREATE OR ALTER PROCEDURE pmo.usp_Milestone_Save
    @MilestoneId      int = NULL,
    @ProjectId        int,
    @Name             nvarchar(250),
    @PlannedDate      date,
    @BaselineDate     date = NULL,
    @ActualDate       date = NULL,
    @StatusId         tinyint,
    @CompletionPercent int,
    @OwnerObjectId    varchar(64) = NULL,
    @OwnerDisplayName nvarchar(200),
    @OwnerEmail       nvarchar(320) = NULL,
    @Today            date,
    @UserObjectId     varchar(64) = NULL,
    @UserName         nvarchar(200),
    @SavedMilestoneId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Completion int = CASE WHEN @CompletionPercent < 0 THEN 0
                                   WHEN @CompletionPercent > 100 THEN 100
                                   ELSE @CompletionPercent END;
    DECLARE @Actual date = @ActualDate;

    /* A completed milestone is complete: it carries its actual date and 100 per cent, so
       "2 of 5 complete" and the slippage report cannot disagree with its status. */
    IF @StatusId = 3
    BEGIN
        SET @Completion = 100;
        SET @Actual = ISNULL(@Actual, @Today);
    END;

    IF @MilestoneId IS NULL
    BEGIN
        INSERT INTO pmo.Milestone
            (ProjectId, Name, PlannedDate, BaselineDate, ActualDate, StatusId, CompletionPercent,
             OwnerObjectId, OwnerDisplayName, OwnerEmail, CreatedOn, CreatedByDisplayName)
        VALUES
            (@ProjectId, LTRIM(RTRIM(@Name)), @PlannedDate, ISNULL(@BaselineDate, @PlannedDate), @Actual,
             @StatusId, @Completion, @OwnerObjectId, @OwnerDisplayName, @OwnerEmail,
             SYSDATETIMEOFFSET(), @UserName);

        SET @SavedMilestoneId = SCOPE_IDENTITY();
        EXEC pmo.usp_Audit_Write 'Milestone', @SavedMilestoneId, 'Created', @UserObjectId, @UserName, @Name;
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pmo.Milestone WHERE Id = @MilestoneId AND ProjectId = @ProjectId)
        BEGIN
            ;THROW 50030, 'The milestone does not belong to this project.', 1;
        END;

        UPDATE pmo.Milestone
        SET Name = LTRIM(RTRIM(@Name)),
            PlannedDate = @PlannedDate,
            BaselineDate = ISNULL(BaselineDate, ISNULL(@BaselineDate, @PlannedDate)),
            ActualDate = @Actual,
            StatusId = @StatusId,
            CompletionPercent = @Completion,
            OwnerObjectId = @OwnerObjectId,
            OwnerDisplayName = @OwnerDisplayName,
            OwnerEmail = @OwnerEmail,
            ModifiedOn = SYSDATETIMEOFFSET(),
            ModifiedByDisplayName = @UserName
        WHERE Id = @MilestoneId;

        SET @SavedMilestoneId = @MilestoneId;
        EXEC pmo.usp_Audit_Write 'Milestone', @MilestoneId, 'Amended', @UserObjectId, @UserName, @Name;
    END;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_Milestone_Cancel
    @MilestoneId  int,
    @UserObjectId varchar(64) = NULL,
    @UserName     nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE pmo.Milestone
    SET StatusId = 4 /* Cancelled */,
        ModifiedOn = SYSDATETIMEOFFSET(),
        ModifiedByDisplayName = @UserName
    WHERE Id = @MilestoneId;

    EXEC pmo.usp_Audit_Write 'Milestone', @MilestoneId, 'Cancelled', @UserObjectId, @UserName, NULL;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_Risk_GetForProject
    @ProjectId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT r.Id AS RiskId, r.ProjectId, r.TypeId, t.Name AS TypeName,
           r.Description, r.SeverityId, sv.Name AS SeverityName,
           r.OwnerObjectId, r.OwnerDisplayName, r.OwnerEmail,
           r.Mitigation, r.DueDate, r.StatusId, st.Name AS StatusName,
           r.ClosureNote, r.ClosedOn, r.ClosedByDisplayName,
           r.RaisedOn, r.RaisedByDisplayName, r.ModifiedOn, r.ModifiedByDisplayName
    FROM pmo.RiskIssue AS r
    INNER JOIN pmo.RiskType AS t ON t.Id = r.TypeId
    INNER JOIN pmo.RiskSeverity AS sv ON sv.Id = r.SeverityId
    INNER JOIN pmo.RiskStatus AS st ON st.Id = r.StatusId
    WHERE r.ProjectId = @ProjectId
    /* Open first, worst first: the register reads as a to-do list, not a filing cabinet. */
    ORDER BY CASE WHEN r.StatusId = 1 THEN 0 ELSE 1 END, r.SeverityId DESC, r.DueDate, r.Id;
END;
GO

/* FR-09. @BecameHigh tells the caller whether WF-06 is owed: raised at High severity, or
   raised to High from something lower. */
CREATE OR ALTER PROCEDURE pmo.usp_Risk_Save
    @RiskId           int = NULL,
    @ProjectId        int,
    @TypeId           tinyint,
    @Description      nvarchar(2000),
    @SeverityId       tinyint,
    @OwnerObjectId    varchar(64) = NULL,
    @OwnerDisplayName nvarchar(200),
    @OwnerEmail       nvarchar(320) = NULL,
    @Mitigation       nvarchar(2000) = NULL,
    @DueDate          date = NULL,
    @UserObjectId     varchar(64) = NULL,
    @UserName         nvarchar(200),
    @SavedRiskId      int OUTPUT,
    @BecameHigh       bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PreviousSeverity tinyint = NULL;
    DECLARE @CurrentStatus tinyint = 1;

    IF @RiskId IS NULL
    BEGIN
        INSERT INTO pmo.RiskIssue
            (ProjectId, TypeId, Description, SeverityId, OwnerObjectId, OwnerDisplayName, OwnerEmail,
             Mitigation, DueDate, StatusId, RaisedOn, RaisedByObjectId, RaisedByDisplayName)
        VALUES
            (@ProjectId, @TypeId, LTRIM(RTRIM(@Description)), @SeverityId,
             @OwnerObjectId, @OwnerDisplayName, @OwnerEmail,
             NULLIF(LTRIM(RTRIM(@Mitigation)), N''), @DueDate, 1 /* Open */,
             SYSDATETIMEOFFSET(), @UserObjectId, @UserName);

        SET @SavedRiskId = SCOPE_IDENTITY();
        EXEC pmo.usp_Audit_Write 'RiskIssue', @SavedRiskId, 'Raised', @UserObjectId, @UserName, @Description;
    END
    ELSE
    BEGIN
        SELECT @PreviousSeverity = SeverityId, @CurrentStatus = StatusId
        FROM pmo.RiskIssue WHERE Id = @RiskId AND ProjectId = @ProjectId;

        IF @PreviousSeverity IS NULL
        BEGIN
            ;THROW 50031, 'The risk or issue does not belong to this project.', 1;
        END;

        UPDATE pmo.RiskIssue
        SET TypeId = @TypeId,
            Description = LTRIM(RTRIM(@Description)),
            SeverityId = @SeverityId,
            OwnerObjectId = @OwnerObjectId,
            OwnerDisplayName = @OwnerDisplayName,
            OwnerEmail = @OwnerEmail,
            Mitigation = NULLIF(LTRIM(RTRIM(@Mitigation)), N''),
            DueDate = @DueDate,
            ModifiedOn = SYSDATETIMEOFFSET(),
            ModifiedByDisplayName = @UserName
        WHERE Id = @RiskId;

        SET @SavedRiskId = @RiskId;
        EXEC pmo.usp_Audit_Write 'RiskIssue', @RiskId, 'Amended', @UserObjectId, @UserName, @Description;
    END;

    SET @BecameHigh = CASE
        WHEN @SeverityId = 3 AND @CurrentStatus = 1
             AND (@PreviousSeverity IS NULL OR @PreviousSeverity <> 3)
        THEN 1 ELSE 0 END;
END;
GO

/* FR-10. Closing keeps the record and requires the closure note. */
CREATE OR ALTER PROCEDURE pmo.usp_Risk_Close
    @RiskId       int,
    @ClosureNote  nvarchar(2000),
    @UserObjectId varchar(64) = NULL,
    @UserName     nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    IF @ClosureNote IS NULL OR LEN(LTRIM(@ClosureNote)) = 0
    BEGIN
        ;THROW 50032, 'A closure note is required when a risk or issue is closed.', 1;
    END;

    UPDATE pmo.RiskIssue
    SET StatusId = 2 /* Closed */,
        ClosureNote = LTRIM(RTRIM(@ClosureNote)),
        ClosedOn = SYSDATETIMEOFFSET(),
        ClosedByDisplayName = @UserName
    WHERE Id = @RiskId;

    EXEC pmo.usp_Audit_Write 'RiskIssue', @RiskId, 'Closed', @UserObjectId, @UserName, @ClosureNote;
END;
GO

/* The parent project of a milestone or of a risk. The portal asks before cancelling or closing
   one, because the access check is made against the project, not against the child record. */
CREATE OR ALTER PROCEDURE pmo.usp_Milestone_GetProjectId
    @MilestoneId int,
    @ProjectId   int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ProjectId = (SELECT ProjectId FROM pmo.Milestone WHERE Id = @MilestoneId);
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_Risk_GetProjectId
    @RiskId    int,
    @ProjectId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ProjectId = (SELECT ProjectId FROM pmo.RiskIssue WHERE Id = @RiskId);
END;
GO
