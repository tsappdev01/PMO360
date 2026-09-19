/*  PMO360 — 014_procs_dashboard.sql
    The management dashboard (screen 1) and the standard views of FR-23.

    AC-04 asks that "every summary figure reconciles to the underlying list data". The
    indicators, the status bars, the upcoming milestones and the attention list are all
    computed here from the same scoped set of projects, so there is no second place for a
    number to come from and nothing is maintained by hand.
*/

/* Four result sets in one round trip: indicators, status counts, upcoming milestones,
   items needing a decision. The portfolio table itself comes from usp_Portfolio_Search,
   so the dashboard and the Projects page cannot show different rows for the same filter. */
CREATE OR ALTER PROCEDURE pmo.usp_Dashboard_Get
    @UserObjectId      varchar(64),
    @CanSeeAll         bit,
    @Today             date,
    @UpdateCycleDays   int = 7,
    @UpcomingWindowDays int = 14,
    @FinancialYearStartMonth tinyint = 1,
    @ReportingEntityId int = NULL,
    @DepartmentId      int = NULL,
    @ConsultantId      int = NULL,
    @OwnerObjectId     varchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FyStart date =
        DATEFROMPARTS(
            CASE WHEN MONTH(@Today) >= @FinancialYearStartMonth THEN YEAR(@Today) ELSE YEAR(@Today) - 1 END,
            @FinancialYearStartMonth, 1);

    DECLARE @WeekStart date = DATEADD(DAY, -7, @Today);
    DECLARE @WindowEnd date = DATEADD(DAY, @UpcomingWindowDays, @Today);

    /* The dimension filters the dashboard header offers; the view filter is not applied here
       because the indicators always describe the active portfolio. */
    DECLARE @Scoped TABLE (ProjectId int PRIMARY KEY, StateId tinyint, StatusId tinyint,
                           ProgressPercent int, AttentionRequired bit, LastUpdateDate date, CreatedOn date);

    INSERT INTO @Scoped (ProjectId, StateId, StatusId, ProgressPercent, AttentionRequired, LastUpdateDate, CreatedOn)
    SELECT p.Id, p.StateId, p.StatusId, p.ProgressPercent, p.AttentionRequired,
           p.LastUpdateDate, CAST(p.CreatedOn AS date)
    FROM pmo.Project AS p
    INNER JOIN pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today) AS v ON v.ProjectId = p.Id
    WHERE (@ReportingEntityId IS NULL OR p.ReportingEntityId = @ReportingEntityId)
      AND (@DepartmentId      IS NULL OR p.DepartmentId      = @DepartmentId)
      AND (@ConsultantId      IS NULL OR p.ConsultantId      = @ConsultantId)
      AND (@OwnerObjectId     IS NULL OR p.ProjectOwnerObjectId = @OwnerObjectId);

    /* 1 — FR-16. The six summary indicators. "Active" excludes completed and closed projects,
       so Average progress cannot be flattered by work that has already finished. */
    SELECT
        SUM(CASE WHEN s.StateId = 1 AND s.StatusId <> 5 THEN 1 ELSE 0 END) AS ActiveProjects,
        SUM(CASE WHEN s.StatusId = 5 AND s.LastUpdateDate >= @FyStart THEN 1 ELSE 0 END) AS CompletedThisFinancialYear,
        SUM(CASE WHEN s.StateId = 1 AND s.StatusId = 1 THEN 1 ELSE 0 END) AS OnTrack,
        SUM(CASE WHEN s.StateId = 1 AND s.StatusId = 2 THEN 1 ELSE 0 END) AS AtRisk,
        SUM(CASE WHEN s.StateId = 1 AND s.StatusId = 3 THEN 1 ELSE 0 END) AS Delayed,
        SUM(CASE WHEN s.StateId = 1 AND s.StatusId = 4 THEN 1 ELSE 0 END) AS OnHold,
        ISNULL(AVG(CASE WHEN s.StateId = 1 AND s.StatusId <> 5 THEN s.ProgressPercent END), 0) AS AverageProgress,
        SUM(CASE WHEN s.StateId = 1 AND s.StatusId <> 5 AND s.AttentionRequired = 1 THEN 1 ELSE 0 END) AS NeedsAttention,
        /* "2 raised this week" under the At Risk indicator, read from the update history
           rather than from a flag, so it is always true to what was submitted. */
        (
            SELECT COUNT(DISTINCT u.ProjectId)
            FROM pmo.ProjectUpdate AS u
            INNER JOIN @Scoped AS sc ON sc.ProjectId = u.ProjectId AND sc.StateId = 1
            WHERE u.IsDraft = 0
              AND u.StatusId = 2
              AND (u.PreviousStatusId IS NULL OR u.PreviousStatusId <> 2)
              AND u.UpdateDate >= @WeekStart
        ) AS AtRiskRaisedThisWeek,
        (
            SELECT COUNT(*)
            FROM @Scoped AS sc
            WHERE sc.StateId = 1 AND sc.StatusId <> 5
              AND ISNULL(sc.LastUpdateDate, sc.CreatedOn) < DATEADD(DAY, -@UpdateCycleDays, @Today)
        ) AS NotReported                                   -- BR-06
    FROM @Scoped AS s;

    /* 2 — Portfolio by status. Bars are proportional to the active portfolio, as the
       dashboard footnote states. */
    DECLARE @ActiveCount int =
        (SELECT COUNT(*) FROM @Scoped WHERE StateId = 1 AND StatusId <> 5);

    SELECT st.Id AS StatusId, st.Name AS StatusName,
           COUNT(s.ProjectId) AS ProjectCount,
           CASE WHEN @ActiveCount = 0 THEN 0
                ELSE CAST(ROUND(COUNT(s.ProjectId) * 100.0 / @ActiveCount, 0) AS int) END AS PercentOfActive
    FROM pmo.ProjectStatus AS st
    LEFT JOIN @Scoped AS s
           ON s.StatusId = st.Id
          AND (
                /* Completed is counted for the financial year; the other four are the live
                   position of the active portfolio. */
                (st.Id = 5 AND s.StatusId = 5 AND s.LastUpdateDate >= @FyStart)
             OR (st.Id <> 5 AND s.StateId = 1)
              )
    GROUP BY st.Id, st.Name, st.SortOrder
    ORDER BY st.SortOrder;

    /* 3 — FR-20. Milestones due in the rolling window, and anything already overdue. */
    SELECT TOP (10)
           m.Id AS MilestoneId, m.ProjectId, p.Name AS ProjectName, m.Name AS MilestoneName,
           m.PlannedDate, m.OwnerDisplayName AS OwnerName,
           CAST(CASE WHEN m.PlannedDate < @Today THEN 1 ELSE 0 END AS bit) AS IsOverdue
    FROM pmo.Milestone AS m
    INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
    INNER JOIN pmo.Project AS p ON p.Id = m.ProjectId
    INNER JOIN @Scoped AS s ON s.ProjectId = m.ProjectId AND s.StateId = 1
    WHERE ms.IsIncomplete = 1
      AND m.PlannedDate <= @WindowEnd
    ORDER BY m.PlannedDate, m.Id;

    /* 4 — FR-20. The items flagged for management attention, with the reason the manager gave. */
    SELECT p.Id AS ProjectId, p.ProjectCode, p.Name AS ProjectName, p.StatusId,
           ISNULL(p.AttentionReason, N'') AS Reason, p.LastUpdateDate AS RaisedOn
    FROM pmo.Project AS p
    INNER JOIN @Scoped AS s ON s.ProjectId = p.Id
    WHERE s.StateId = 1 AND p.AttentionRequired = 1
    ORDER BY CASE p.StatusId WHEN 3 THEN 0 WHEN 2 THEN 1 ELSE 2 END, p.LastUpdateDate DESC;
END;
GO

/* FR-23. Overdue milestones across the portfolio the caller can see (FR-08). */
CREATE OR ALTER PROCEDURE pmo.usp_View_OverdueMilestones
    @UserObjectId      varchar(64),
    @CanSeeAll         bit,
    @Today             date,
    @ReportingEntityId int = NULL,
    @DepartmentId      int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT m.Id AS MilestoneId, m.ProjectId, p.ProjectCode, p.Name AS ProjectName,
           m.Name AS MilestoneName, m.PlannedDate, m.BaselineDate,
           DATEDIFF(DAY, m.PlannedDate, @Today) AS DaysOverdue,
           m.CompletionPercent, m.OwnerDisplayName AS OwnerName
    FROM pmo.Milestone AS m
    INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
    INNER JOIN pmo.Project AS p ON p.Id = m.ProjectId
    INNER JOIN pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today) AS v ON v.ProjectId = p.Id
    WHERE ms.IsIncomplete = 1
      AND m.PlannedDate < @Today
      AND p.StateId = 1
      AND (@ReportingEntityId IS NULL OR p.ReportingEntityId = @ReportingEntityId)
      AND (@DepartmentId IS NULL OR p.DepartmentId = @DepartmentId)
    ORDER BY m.PlannedDate, p.Name;
END;
GO

/* FR-23. The risk register across the portfolio; @OpenHighOnly gives the "open high risks" view. */
CREATE OR ALTER PROCEDURE pmo.usp_View_Risks
    @UserObjectId      varchar(64),
    @CanSeeAll         bit,
    @Today             date,
    @OpenHighOnly      bit = 0,
    @ReportingEntityId int = NULL,
    @DepartmentId      int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT r.Id AS RiskId, r.ProjectId, p.ProjectCode, p.Name AS ProjectName,
           r.TypeId, r.Description, r.SeverityId, r.OwnerDisplayName AS OwnerName,
           r.Mitigation, r.DueDate, r.StatusId,
           CAST(CASE WHEN r.StatusId = 1 AND r.DueDate IS NOT NULL AND r.DueDate < @Today
                     THEN 1 ELSE 0 END AS bit) AS IsOverdue
    FROM pmo.RiskIssue AS r
    INNER JOIN pmo.Project AS p ON p.Id = r.ProjectId
    INNER JOIN pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today) AS v ON v.ProjectId = p.Id
    WHERE p.StateId = 1
      AND (@OpenHighOnly = 0 OR (r.StatusId = 1 AND r.SeverityId = 3))
      AND (@ReportingEntityId IS NULL OR p.ReportingEntityId = @ReportingEntityId)
      AND (@DepartmentId IS NULL OR p.DepartmentId = @DepartmentId)
    ORDER BY CASE WHEN r.StatusId = 1 THEN 0 ELSE 1 END, r.SeverityId DESC, r.DueDate, p.Name;
END;
GO

/* FR-23, BR-06. Active projects with no update inside the agreed cycle —
   "no news" separated from "on track". */
CREATE OR ALTER PROCEDURE pmo.usp_View_NotReported
    @UserObjectId    varchar(64),
    @CanSeeAll       bit,
    @Today           date,
    @UpdateCycleDays int = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Cutoff date = DATEADD(DAY, -@UpdateCycleDays, @Today);

    SELECT p.Id AS ProjectId, p.ProjectCode, p.Name AS ProjectName,
           p.ProjectOwnerDisplayName AS OwnerName, p.StatusId, p.LastUpdateDate,
           DATEDIFF(DAY, ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date)), @Today) AS DaysSinceUpdate
    FROM pmo.Project AS p
    INNER JOIN pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today) AS v ON v.ProjectId = p.Id
    WHERE p.StateId = 1
      AND p.StatusId <> 5
      AND ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date)) < @Cutoff
    ORDER BY DaysSinceUpdate DESC, p.Name;
END;
GO

/* Section 5.4, reporting compliance: how much of the active portfolio reported inside the cycle. */
CREATE OR ALTER PROCEDURE pmo.usp_Report_Compliance
    @UserObjectId    varchar(64),
    @CanSeeAll       bit,
    @Today           date,
    @UpdateCycleDays int = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Cutoff date = DATEADD(DAY, -@UpdateCycleDays, @Today);

    SELECT COUNT(*) AS ActiveProjects,
           SUM(CASE WHEN p.LastUpdateDate IS NOT NULL AND p.LastUpdateDate >= @Cutoff THEN 1 ELSE 0 END) AS ReportedInCycle,
           SUM(CASE WHEN p.LastUpdateDate IS NULL OR p.LastUpdateDate < @Cutoff THEN 1 ELSE 0 END) AS NotReported
    FROM pmo.Project AS p
    INNER JOIN pmo.fn_VisibleProjects(@UserObjectId, @CanSeeAll, @Today) AS v ON v.ProjectId = p.Id
    WHERE p.StateId = 1 AND p.StatusId <> 5;
END;
GO
