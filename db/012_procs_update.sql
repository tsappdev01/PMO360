/*  PMO360 — 012_procs_update.sql
    The update form is the only place data is entered (FR-11), so this file carries the
    business rules of section 5.2. They are enforced here, in the database, not only in the
    portal: a submission that breaks a rule is rejected whatever route it arrived by.

    usp_ProjectUpdate_Submit returns a result set of failures. No rows means the update was
    accepted and @UpdateId carries the new history entry. Rows mean nothing was written, and
    each row names the field, the BRD rule and the message to show.
*/

/* FR-15. What the form opens on: the author's own draft, else the last submitted update,
   else the project's current position. */
CREATE OR ALTER PROCEDURE pmo.usp_ProjectUpdate_GetFormDefaults
    @ProjectId    int,
    @UserObjectId varchar(64),
    @Today        date
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DraftId int =
        (SELECT TOP (1) Id FROM pmo.ProjectUpdate
         WHERE ProjectId = @ProjectId AND IsDraft = 1 AND SubmittedByObjectId = @UserObjectId
         ORDER BY Id DESC);

    IF @DraftId IS NOT NULL
    BEGIN
        SELECT @ProjectId AS ProjectId, @Today AS UpdateDate, StatusId, ProgressPercent, PhaseId, MilestoneId,
               KeyUpdate, Achievement, NextAction,
               NextActionOwnerObjectId, NextActionOwnerDisplayName, NextActionOwnerEmail, NextActionDueDate,
               AttentionRequired, AttentionReason, CAST(1 AS bit) AS FromDraft
        FROM pmo.ProjectUpdate WHERE Id = @DraftId;
        RETURN;
    END;

    DECLARE @LastId int =
        (SELECT TOP (1) Id FROM pmo.ProjectUpdate
         WHERE ProjectId = @ProjectId AND IsDraft = 0
         ORDER BY UpdateDate DESC, Id DESC);

    IF @LastId IS NOT NULL
    BEGIN
        /* The achievement is "since last update", so it is not carried forward from a
           submitted update — the manager states what has happened since. */
        SELECT @ProjectId AS ProjectId, @Today AS UpdateDate, StatusId, ProgressPercent, PhaseId, MilestoneId,
               KeyUpdate, CAST(NULL AS nvarchar(2000)) AS Achievement, NextAction,
               NextActionOwnerObjectId, NextActionOwnerDisplayName, NextActionOwnerEmail, NextActionDueDate,
               AttentionRequired, AttentionReason, CAST(0 AS bit) AS FromDraft
        FROM pmo.ProjectUpdate WHERE Id = @LastId;
        RETURN;
    END;

    SELECT p.Id AS ProjectId, @Today AS UpdateDate, p.StatusId, p.ProgressPercent,
           p.CurrentPhaseId AS PhaseId, CAST(NULL AS int) AS MilestoneId,
           p.KeyUpdate, CAST(NULL AS nvarchar(2000)) AS Achievement, p.NextAction,
           p.NextActionOwnerObjectId, p.NextActionOwnerDisplayName, p.NextActionOwnerEmail, p.NextActionDueDate,
           p.AttentionRequired, p.AttentionReason, CAST(0 AS bit) AS FromDraft
    FROM pmo.Project AS p
    WHERE p.Id = @ProjectId;
END;
GO

/* ---------------------------------------------------------------------------
   FR-12, FR-22, AC-03, AC-04. Validates against section 5.2, then in one
   transaction: writes the dated history entry, updates the project record and
   stamps the last update date and the submitting user.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE pmo.usp_ProjectUpdate_Submit
    @ProjectId        int,
    @UpdateDate       date,
    @StatusId         tinyint,
    @ProgressPercent  int,
    @PhaseId          int = NULL,
    @MilestoneId      int = NULL,
    @KeyUpdate        nvarchar(2000) = NULL,
    @Achievement      nvarchar(2000) = NULL,
    @NextAction       nvarchar(1000) = NULL,
    @NextActionOwnerObjectId varchar(64) = NULL,
    @NextActionOwnerDisplayName nvarchar(200) = NULL,
    @NextActionOwnerEmail nvarchar(320) = NULL,
    @NextActionDueDate date = NULL,
    @AttentionRequired bit,
    @AttentionReason  nvarchar(1000) = NULL,
    @Today            date,
    @UserObjectId     varchar(64) = NULL,
    @UserName         nvarchar(200),
    @UserEmail        nvarchar(320) = NULL,
    @UpdateId         int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @UpdateId = NULL;

    /* [Rule] is bracketed because RULE is a reserved keyword in T-SQL. The column keeps that
       name because it is the BRD reference the portal shows against the failure. */
    DECLARE @Failures TABLE (Field varchar(50), [Rule] varchar(10), [Message] nvarchar(400));

    DECLARE @CurrentStatus   tinyint,
            @CurrentProgress int,
            @CurrentPhaseId  int,
            @StateId         tinyint;

    SELECT @CurrentStatus = StatusId,
           @CurrentProgress = ProgressPercent,
           @CurrentPhaseId = CurrentPhaseId,
           @StateId = StateId
    FROM pmo.Project
    WHERE Id = @ProjectId;

    IF @CurrentStatus IS NULL
    BEGIN
        ;THROW 50020, 'The project was not found.', 1;
    END;

    DECLARE @KeyUpdateGiven bit =
        CASE WHEN @KeyUpdate IS NOT NULL AND LEN(LTRIM(RTRIM(@KeyUpdate))) >= 10 THEN 1 ELSE 0 END;

    /* FR-03 */
    IF @ProgressPercent < 0 OR @ProgressPercent > 100
        INSERT INTO @Failures VALUES
            ('ProgressPercent', 'FR-03', N'Progress must be a whole number between 0 and 100 per cent.');

    IF @UpdateDate > @Today
        INSERT INTO @Failures VALUES
            ('UpdateDate', 'FR-12', N'The update date cannot be in the future.');

    /* BR-02. At Risk and Delayed require the cause and the recovery action. */
    IF @StatusId IN (2, 3) AND @KeyUpdateGiven = 0
        INSERT INTO @Failures
        SELECT 'KeyUpdate', 'BR-02',
               N'A status of ' + s.Name + N' requires a key update stating the cause and the recovery action.'
        FROM pmo.ProjectStatus AS s WHERE s.Id = @StatusId;

    /* BR-03. Management attention requires a reason. */
    IF @AttentionRequired = 1 AND (@AttentionReason IS NULL OR LEN(LTRIM(RTRIM(@AttentionReason))) < 10)
        INSERT INTO @Failures VALUES
            ('AttentionReason', 'BR-03', N'Management attention requires a reason before the update can be submitted.');

    /* BR-04. Completed means 100 per cent with no open milestones. */
    IF @StatusId = 5
    BEGIN
        IF @ProgressPercent <> 100
            INSERT INTO @Failures VALUES
                ('ProgressPercent', 'BR-04', N'A project set to Completed must be at 100 per cent.');

        DECLARE @OpenMilestones int =
        (
            SELECT COUNT(*)
            FROM pmo.Milestone AS m
            INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
            WHERE m.ProjectId = @ProjectId AND ms.IsIncomplete = 1
        );

        IF @OpenMilestones > 0
            INSERT INTO @Failures VALUES
                ('StatusId', 'BR-04',
                 N'A project set to Completed must have no open milestones; '
                 + CAST(@OpenMilestones AS nvarchar(10))
                 + CASE WHEN @OpenMilestones = 1 THEN N' milestone is' ELSE N' milestones are' END
                 + N' still open.');
    END;

    /* BR-05. Progress may not decrease without an explanation in the key update. */
    IF @ProgressPercent < @CurrentProgress AND @KeyUpdateGiven = 0
        INSERT INTO @Failures VALUES
            ('KeyUpdate', 'BR-05',
             N'Progress is going down. Explain the reduction in the key update.');

    /* FR-05. A closed project has left the reporting cycle. */
    IF @StateId <> 1
        INSERT INTO @Failures VALUES
            ('ProjectId', 'FR-05', N'This project is closed. The PMO must reopen it before an update can be submitted.');

    /* FR-06. The milestone must belong to this project. */
    IF @MilestoneId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM pmo.Milestone WHERE Id = @MilestoneId AND ProjectId = @ProjectId)
        INSERT INTO @Failures VALUES
            ('MilestoneId', 'FR-06', N'The selected milestone does not belong to this project.');

    IF EXISTS (SELECT 1 FROM @Failures)
    BEGIN
        SELECT Field, [Rule], [Message] FROM @Failures;
        RETURN;
    END;

    BEGIN TRANSACTION;

        INSERT INTO pmo.ProjectUpdate
        (
            ProjectId, UpdateDate, StatusId, ProgressPercent, PhaseId, MilestoneId,
            KeyUpdate, Achievement, NextAction,
            NextActionOwnerObjectId, NextActionOwnerDisplayName, NextActionOwnerEmail, NextActionDueDate,
            AttentionRequired, AttentionReason, IsDraft,
            SubmittedByObjectId, SubmittedByDisplayName, SubmittedByEmail, SubmittedOn,
            PreviousStatusId, PreviousProgressPercent
        )
        VALUES
        (
            @ProjectId, @UpdateDate, @StatusId, @ProgressPercent, @PhaseId, @MilestoneId,
            NULLIF(LTRIM(RTRIM(@KeyUpdate)), N''), NULLIF(LTRIM(RTRIM(@Achievement)), N''),
            NULLIF(LTRIM(RTRIM(@NextAction)), N''),
            @NextActionOwnerObjectId, @NextActionOwnerDisplayName, @NextActionOwnerEmail, @NextActionDueDate,
            @AttentionRequired,
            CASE WHEN @AttentionRequired = 1 THEN NULLIF(LTRIM(RTRIM(@AttentionReason)), N'') END,
            0,
            @UserObjectId, @UserName, @UserEmail, SYSDATETIMEOFFSET(),
            @CurrentStatus, @CurrentProgress
        );

        SET @UpdateId = SCOPE_IDENTITY();

        /* FR-12. The project record carries the current position; the entry above carries how
           it got there. Both are written here, so the dashboard can never show a position
           that has no update behind it. */
        UPDATE pmo.Project
        SET StatusId = @StatusId,
            ProgressPercent = @ProgressPercent,
            CurrentPhaseId = ISNULL(@PhaseId, @CurrentPhaseId),
            KeyUpdate = NULLIF(LTRIM(RTRIM(@KeyUpdate)), N''),
            NextAction = NULLIF(LTRIM(RTRIM(@NextAction)), N''),
            NextActionOwnerObjectId = @NextActionOwnerObjectId,
            NextActionOwnerDisplayName = @NextActionOwnerDisplayName,
            NextActionOwnerEmail = @NextActionOwnerEmail,
            NextActionDueDate = @NextActionDueDate,
            AttentionRequired = @AttentionRequired,
            AttentionReason = CASE WHEN @AttentionRequired = 1
                                   THEN NULLIF(LTRIM(RTRIM(@AttentionReason)), N'') END,
            LastUpdateDate = @UpdateDate,
            LastUpdatedByObjectId = @UserObjectId,
            LastUpdatedByDisplayName = @UserName,
            LastUpdatedByEmail = @UserEmail
        WHERE Id = @ProjectId;

        /* The author's draft has become this submission; it is not history and does not linger. */
        DELETE FROM pmo.ProjectUpdate
        WHERE ProjectId = @ProjectId AND IsDraft = 1
          AND (SubmittedByObjectId = @UserObjectId
               OR (@UserObjectId IS NULL AND SubmittedByDisplayName = @UserName));

        EXEC pmo.usp_Audit_Write 'ProjectUpdate', @UpdateId, 'Submitted', @UserObjectId, @UserName, @KeyUpdate;

    COMMIT TRANSACTION;

    /* No rows: accepted. The caller reads @UpdateId. */
    SELECT Field, [Rule], [Message] FROM @Failures;
END;
GO

/* "Save as draft". A draft is private to its author, is not history, and never reaches the
   dashboard. Saving again replaces it rather than accumulating copies. */
CREATE OR ALTER PROCEDURE pmo.usp_ProjectUpdate_SaveDraft
    @ProjectId        int,
    @UpdateDate       date,
    @StatusId         tinyint,
    @ProgressPercent  int,
    @PhaseId          int = NULL,
    @MilestoneId      int = NULL,
    @KeyUpdate        nvarchar(2000) = NULL,
    @Achievement      nvarchar(2000) = NULL,
    @NextAction       nvarchar(1000) = NULL,
    @NextActionOwnerObjectId varchar(64) = NULL,
    @NextActionOwnerDisplayName nvarchar(200) = NULL,
    @NextActionOwnerEmail nvarchar(320) = NULL,
    @NextActionDueDate date = NULL,
    @AttentionRequired bit,
    @AttentionReason  nvarchar(1000) = NULL,
    @UserObjectId     varchar(64) = NULL,
    @UserName         nvarchar(200),
    @UserEmail        nvarchar(320) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Progress int = CASE WHEN @ProgressPercent < 0 THEN 0
                                 WHEN @ProgressPercent > 100 THEN 100
                                 ELSE @ProgressPercent END;

    DECLARE @DraftId int =
        (SELECT TOP (1) Id FROM pmo.ProjectUpdate
         WHERE ProjectId = @ProjectId AND IsDraft = 1 AND SubmittedByObjectId = @UserObjectId
         ORDER BY Id DESC);

    IF @DraftId IS NULL
    BEGIN
        INSERT INTO pmo.ProjectUpdate
        (
            ProjectId, UpdateDate, StatusId, ProgressPercent, PhaseId, MilestoneId,
            KeyUpdate, Achievement, NextAction,
            NextActionOwnerObjectId, NextActionOwnerDisplayName, NextActionOwnerEmail, NextActionDueDate,
            AttentionRequired, AttentionReason, IsDraft,
            SubmittedByObjectId, SubmittedByDisplayName, SubmittedByEmail, SubmittedOn
        )
        VALUES
        (
            @ProjectId, @UpdateDate, @StatusId, @Progress, @PhaseId, @MilestoneId,
            @KeyUpdate, @Achievement, @NextAction,
            @NextActionOwnerObjectId, @NextActionOwnerDisplayName, @NextActionOwnerEmail, @NextActionDueDate,
            @AttentionRequired, @AttentionReason, 1,
            @UserObjectId, @UserName, @UserEmail, SYSDATETIMEOFFSET()
        );
    END
    ELSE
    BEGIN
        UPDATE pmo.ProjectUpdate
        SET UpdateDate = @UpdateDate,
            StatusId = @StatusId,
            ProgressPercent = @Progress,
            PhaseId = @PhaseId,
            MilestoneId = @MilestoneId,
            KeyUpdate = @KeyUpdate,
            Achievement = @Achievement,
            NextAction = @NextAction,
            NextActionOwnerObjectId = @NextActionOwnerObjectId,
            NextActionOwnerDisplayName = @NextActionOwnerDisplayName,
            NextActionOwnerEmail = @NextActionOwnerEmail,
            NextActionDueDate = @NextActionDueDate,
            AttentionRequired = @AttentionRequired,
            AttentionReason = @AttentionReason,
            SubmittedOn = SYSDATETIMEOFFSET()
        WHERE Id = @DraftId;
    END;
END;
GO

/* FR-14. The full history in date order, newest first. Drafts are never included. */
CREATE OR ALTER PROCEDURE pmo.usp_ProjectUpdate_GetHistory
    @ProjectId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.Id AS UpdateId, u.ProjectId, u.UpdateDate, u.StatusId, s.Name AS StatusName,
           u.ProgressPercent, u.PhaseId, ph.Name AS PhaseName,
           u.MilestoneId, m.Name AS MilestoneName,
           u.KeyUpdate, u.Achievement, u.NextAction, u.NextActionOwnerDisplayName, u.NextActionDueDate,
           u.AttentionRequired, u.AttentionReason,
           u.SubmittedByDisplayName, u.SubmittedOn,
           u.PreviousStatusId, u.PreviousProgressPercent
    FROM pmo.ProjectUpdate AS u
    INNER JOIN pmo.ProjectStatus AS s ON s.Id = u.StatusId
    LEFT JOIN pmo.Phase AS ph ON ph.Id = u.PhaseId
    LEFT JOIN pmo.Milestone AS m ON m.Id = u.MilestoneId
    WHERE u.ProjectId = @ProjectId AND u.IsDraft = 0
    ORDER BY u.UpdateDate DESC, u.Id DESC;
END;
GO

/* The Updates page: what has been submitted across the portfolio the caller can see. */
CREATE OR ALTER PROCEDURE pmo.usp_ProjectUpdate_GetRecent
    @UserObjectId varchar(64),
    @CanSeeAll    bit,
    @Today        date,
    @Take         int = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take)
           u.Id AS UpdateId, u.ProjectId, p.ProjectCode, p.Name AS ProjectName,
           u.UpdateDate, u.StatusId, s.Name AS StatusName, u.ProgressPercent,
           u.KeyUpdate, u.AttentionRequired, u.SubmittedByDisplayName, u.SubmittedOn
    FROM pmo.ProjectUpdate AS u
    INNER JOIN pmo.Project AS p ON p.Id = u.ProjectId
    INNER JOIN pmo.ProjectStatus AS s ON s.Id = u.StatusId
    INNER JOIN pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today) AS v ON v.ProjectId = u.ProjectId
    WHERE u.IsDraft = 0
    ORDER BY u.SubmittedOn DESC, u.Id DESC;
END;
GO
