/*  PMO360 — 020_views_powerbi.sql
    The reporting layer Power BI reads (section 5.4). Views, not tables: "no figures maintained
    inside the report", and nothing is copied. Import or DirectQuery both work against these.

    AC-11 asks that the Power BI report reconcile to the dashboard. It does so because these
    views read the same rows the dashboard procedures read, with the same definitions of
    "active", "incomplete" and "not reported".

    The views carry no row-level scope: a Power BI dataset is published to management, who see
    the whole portfolio. If the report is ever shared more widely, apply row-level security in
    the model on ReportingEntityName or ProjectManagerObjectId.
*/

CREATE OR ALTER VIEW pmo.vw_Project
AS
SELECT
    p.Id                        AS ProjectId,
    p.ProjectCode,
    p.Name                      AS ProjectName,
    p.Description,
    e.Name                      AS ReportingEntityName,
    d.Name                      AS DepartmentName,
    ISNULL(c.Name, N'Not appointed') AS ConsultantName,
    ISNULL(ph.Name, N'Not set') AS PhaseName,
    p.ProjectOwnerDisplayName   AS ProjectOwner,
    p.ProjectOwnerObjectId,
    p.BusinessOwnerDisplayName  AS BusinessOwner,
    p.ProjectManagerDisplayName AS ProjectManager,
    p.ProjectManagerObjectId,
    p.SponsorDisplayName        AS Sponsor,
    st.Name                     AS Status,
    p.StatusId,
    pr.Name                     AS Priority,
    p.ProgressPercent,
    p.StartDate,
    p.TargetCompletionDate,
    p.LastUpdateDate,
    p.AttentionRequired,
    p.AttentionReason,
    p.KeyUpdate,
    rs.Name                     AS RecordState,
    CAST(CASE WHEN p.StateId = 1 AND p.StatusId <> 5 THEN 1 ELSE 0 END AS bit) AS IsActive,
    /* BR-06. The cycle is [7] days in the BRD; if the business confirms another figure,
       change it here and in the portal's Portal:UpdateCycleDays setting together. */
    CAST(CASE WHEN p.StateId = 1 AND p.StatusId <> 5
               AND ISNULL(p.LastUpdateDate, CAST(p.CreatedOn AS date)) < DATEADD(DAY, -7, CAST(SYSDATETIMEOFFSET() AS date))
              THEN 1 ELSE 0 END AS bit) AS IsNotReported,
    (SELECT COUNT(*) FROM pmo.RiskIssue AS r
      WHERE r.ProjectId = p.Id AND r.StatusId = 1 AND r.SeverityId = 3) AS OpenHighRisks,
    p.CreatedOn
FROM pmo.Project AS p
INNER JOIN pmo.ReportingEntity AS e ON e.Id = p.ReportingEntityId
INNER JOIN pmo.Department AS d ON d.Id = p.DepartmentId
INNER JOIN pmo.ProjectStatus AS st ON st.Id = p.StatusId
INNER JOIN pmo.ProjectPriority AS pr ON pr.Id = p.PriorityId
INNER JOIN pmo.RecordState AS rs ON rs.Id = p.StateId
LEFT JOIN pmo.Consultant AS c ON c.Id = p.ConsultantId
LEFT JOIN pmo.Phase AS ph ON ph.Id = p.CurrentPhaseId;
GO

/* Milestone slippage: planned against actual, and against the baseline (section 5.4). */
CREATE OR ALTER VIEW pmo.vw_Milestone
AS
SELECT
    m.Id            AS MilestoneId,
    m.ProjectId,
    p.ProjectCode,
    p.Name          AS ProjectName,
    e.Name          AS ReportingEntityName,
    m.Name          AS MilestoneName,
    m.PlannedDate,
    m.BaselineDate,
    m.ActualDate,
    ms.Name         AS Status,
    ms.IsIncomplete,
    m.CompletionPercent,
    m.OwnerDisplayName AS Owner,
    CASE WHEN m.BaselineDate IS NULL THEN NULL
         ELSE DATEDIFF(DAY, m.BaselineDate, ISNULL(m.ActualDate, m.PlannedDate)) END AS SlippageDays,
    CAST(CASE WHEN ms.IsIncomplete = 1 AND m.PlannedDate < CAST(SYSDATETIMEOFFSET() AS date)
              THEN 1 ELSE 0 END AS bit) AS IsOverdue
FROM pmo.Milestone AS m
INNER JOIN pmo.MilestoneStatus AS ms ON ms.Id = m.StatusId
INNER JOIN pmo.Project AS p ON p.Id = m.ProjectId
INNER JOIN pmo.ReportingEntity AS e ON e.Id = p.ReportingEntityId;
GO

CREATE OR ALTER VIEW pmo.vw_RiskIssue
AS
SELECT
    r.Id            AS RiskIssueId,
    r.ProjectId,
    p.ProjectCode,
    p.Name          AS ProjectName,
    e.Name          AS ReportingEntityName,
    t.Name          AS Type,
    r.Description,
    sv.Name         AS Severity,
    r.SeverityId,
    r.OwnerDisplayName AS Owner,
    r.Mitigation,
    r.DueDate,
    stt.Name        AS Status,
    r.ClosureNote,
    r.RaisedOn,
    r.ClosedOn,
    CAST(CASE WHEN r.StatusId = 1 AND r.DueDate IS NOT NULL AND r.DueDate < CAST(SYSDATETIMEOFFSET() AS date)
              THEN 1 ELSE 0 END AS bit) AS IsOverdue
FROM pmo.RiskIssue AS r
INNER JOIN pmo.RiskType AS t ON t.Id = r.TypeId
INNER JOIN pmo.RiskSeverity AS sv ON sv.Id = r.SeverityId
INNER JOIN pmo.RiskStatus AS stt ON stt.Id = r.StatusId
INNER JOIN pmo.Project AS p ON p.Id = r.ProjectId
INNER JOIN pmo.ReportingEntity AS e ON e.Id = p.ReportingEntityId;
GO

/* The status and progress trend of section 5.4, built from the update history. Drafts excluded:
   a draft is not something that happened. */
CREATE OR ALTER VIEW pmo.vw_ProjectUpdate
AS
SELECT
    u.Id            AS UpdateId,
    u.ProjectId,
    p.ProjectCode,
    p.Name          AS ProjectName,
    e.Name          AS ReportingEntityName,
    d.Name          AS DepartmentName,
    u.UpdateDate,
    st.Name         AS Status,
    u.StatusId,
    u.ProgressPercent,
    ISNULL(ph.Name, N'Not set') AS PhaseName,
    u.KeyUpdate,
    u.Achievement,
    u.NextAction,
    u.NextActionDueDate,
    u.AttentionRequired,
    u.AttentionReason,
    u.SubmittedByDisplayName AS SubmittedBy,
    u.SubmittedOn,
    prev.Name       AS PreviousStatus,
    u.PreviousProgressPercent,
    /* Movement since the previous update, so the trend visual does not have to compute it. */
    CASE WHEN u.PreviousProgressPercent IS NULL THEN NULL
         ELSE u.ProgressPercent - u.PreviousProgressPercent END AS ProgressMovement
FROM pmo.ProjectUpdate AS u
INNER JOIN pmo.Project AS p ON p.Id = u.ProjectId
INNER JOIN pmo.ReportingEntity AS e ON e.Id = p.ReportingEntityId
INNER JOIN pmo.Department AS d ON d.Id = p.DepartmentId
INNER JOIN pmo.ProjectStatus AS st ON st.Id = u.StatusId
LEFT JOIN pmo.ProjectStatus AS prev ON prev.Id = u.PreviousStatusId
LEFT JOIN pmo.Phase AS ph ON ph.Id = u.PhaseId
WHERE u.IsDraft = 0;
GO

/* Reporting compliance as a single row, for the executive page. */
CREATE OR ALTER VIEW pmo.vw_ReportingCompliance
AS
SELECT
    COUNT(*) AS ActiveProjects,
    SUM(CASE WHEN p.LastUpdateDate >= DATEADD(DAY, -7, CAST(SYSDATETIMEOFFSET() AS date)) THEN 1 ELSE 0 END) AS ReportedInCycle,
    SUM(CASE WHEN p.LastUpdateDate IS NULL
                   OR p.LastUpdateDate < DATEADD(DAY, -7, CAST(SYSDATETIMEOFFSET() AS date)) THEN 1 ELSE 0 END) AS NotReported
FROM pmo.Project AS p
WHERE p.StateId = 1 AND p.StatusId <> 5;
GO
