/*  PMO360 — 010_functions.sql
    Shared helpers used by the procedures. Inline table-valued functions, so the optimiser folds
    them into the calling query rather than materialising a result.

    Re-runnable: CREATE OR ALTER.
*/

/* ---------------------------------------------------------------------------
   Section 6 / AC-10, in one place. Every read of the register goes through this
   function, so a consultant cannot reach another entity's project by guessing
   its id, and the rule cannot drift between one procedure and the next.

   @CanSeeAll is set by the application from the caller's role: Management, PMO
   Administrator and IT Support see the whole portfolio; everyone else sees only
   the projects they own, manage or are assigned to.
---------------------------------------------------------------------------- */
CREATE OR ALTER FUNCTION pmo.fn_VisibleProjects
(
    @UserObjectId varchar(64),
    @CanSeeAll    bit,
    @Today        date
)
RETURNS TABLE
AS
RETURN
(
    SELECT p.Id AS ProjectId
    FROM pmo.Project AS p
    WHERE @CanSeeAll = 1
       OR (
            @UserObjectId IS NOT NULL
            AND (
                p.ProjectManagerObjectId = @UserObjectId
                OR p.ProjectOwnerObjectId  = @UserObjectId
                OR p.BusinessOwnerObjectId = @UserObjectId
                OR p.SponsorObjectId       = @UserObjectId
                OR EXISTS (
                    SELECT 1
                    FROM pmo.ProjectAssignment AS a
                    WHERE a.ProjectId = p.Id
                      AND a.PersonObjectId = @UserObjectId
                      AND (a.EndsOn IS NULL OR a.EndsOn >= @Today)
                )
            )
          )
);
GO

/* ---------------------------------------------------------------------------
   FR-07. The next milestone is the earliest incomplete milestone by planned
   date; the id breaks a same-date tie so the dashboard shows the same one on
   every render.
---------------------------------------------------------------------------- */
CREATE OR ALTER FUNCTION pmo.fn_NextMilestone (@ProjectId int)
RETURNS TABLE
AS
RETURN
(
    SELECT TOP (1)
           m.Id,
           m.Name,
           m.PlannedDate,
           m.OwnerDisplayName
    FROM pmo.Milestone AS m
    INNER JOIN pmo.MilestoneStatus AS s ON s.Id = m.StatusId
    WHERE m.ProjectId = @ProjectId
      AND s.IsIncomplete = 1
    ORDER BY m.PlannedDate, m.Id
);
GO

/* ---------------------------------------------------------------------------
   Writes one row of the audit trail. Called by every procedure that changes
   data, so "every change attributable to a named user with date and time"
   (section 7) is kept by the database rather than by the caller's good manners.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE pmo.usp_Audit_Write
    @EntityName   varchar(100),
    @EntityKey    varchar(100),
    @Action       varchar(30),
    @UserObjectId varchar(64),
    @UserName     nvarchar(200),
    @Detail       nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO pmo.AuditEntry (EntityName, EntityKey, Action, UserObjectId, UserName, OccurredOn, Detail)
    VALUES (@EntityName, @EntityKey, @Action, @UserObjectId, @UserName, SYSDATETIMEOFFSET(), @Detail);
END;
GO

/* ---------------------------------------------------------------------------
   The controlled lists as the database holds them. The portal reads this at
   startup and compares it with its own enums; a mismatch stops the application
   with a clear message instead of producing a wrong dashboard.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE pmo.usp_System_GetEnumerations
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 'ProjectStatus'   AS ListName, Id, Code FROM pmo.ProjectStatus
    UNION ALL SELECT 'ProjectPriority', Id, Code FROM pmo.ProjectPriority
    UNION ALL SELECT 'MilestoneStatus', Id, Code FROM pmo.MilestoneStatus
    UNION ALL SELECT 'RiskType',        Id, Code FROM pmo.RiskType
    UNION ALL SELECT 'RiskSeverity',    Id, Code FROM pmo.RiskSeverity
    UNION ALL SELECT 'RiskStatus',      Id, Code FROM pmo.RiskStatus
    UNION ALL SELECT 'RecordState',     Id, Code FROM pmo.RecordState
    UNION ALL SELECT 'AssignmentRole',  Id, Code FROM pmo.AssignmentRole
    ORDER BY ListName, Id;
END;
GO

/* Reference lists for the choice fields, in one round trip (four result sets). */
CREATE OR ALTER PROCEDURE pmo.usp_Reference_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Code, Name, SortOrder, IsActive
    FROM pmo.ReportingEntity WHERE IsActive = 1 ORDER BY SortOrder, Name;

    SELECT Id, Name, SortOrder, IsActive
    FROM pmo.Department WHERE IsActive = 1 ORDER BY SortOrder, Name;

    SELECT Id, Name, SortOrder, IsActive
    FROM pmo.Phase WHERE IsActive = 1 ORDER BY SortOrder, Name;

    SELECT Id, Name, IsActive
    FROM pmo.Consultant WHERE IsActive = 1 ORDER BY Name;
END;
GO
