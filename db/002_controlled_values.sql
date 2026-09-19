/*  PMO360 — 002_controlled_values.sql
    The controlled value lists (FR-02, BR-01). Ids are fixed and must stay in step with the
    enums in PMO360.Domain.Enums; the portal compares the two at startup and refuses to run on
    a mismatch, so a drift shows as a clear message rather than a wrong dashboard.

    Re-runnable: each insert is guarded, and the names and definitions are refreshed on every
    run so a wording change in the BRD is applied by re-running this script.
*/

SET NOCOUNT ON;
GO

/* BR-01. The five agreed values and their fixed definitions. */
MERGE pmo.ProjectStatus AS target
USING (VALUES
    (1, 'OnTrack',   N'On Track',  N'Delivering to plan.', 1),
    (2, 'AtRisk',    N'At Risk',   N'An identified threat could cause a milestone or the completion date to be missed.', 2),
    (3, 'Delayed',   N'Delayed',   N'A milestone or the completion date has already been missed.', 3),
    (4, 'OnHold',    N'On Hold',   N'Formally paused.', 4),
    (5, 'Completed', N'Completed', N'Delivered and accepted.', 5)
) AS source (Id, Code, Name, Definition, SortOrder)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name,
                             Definition = source.Definition, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Name, Definition, SortOrder)
    VALUES (source.Id, source.Code, source.Name, source.Definition, source.SortOrder);
GO

MERGE pmo.ProjectPriority AS target
USING (VALUES
    (1, 'Low', N'Low', 1), (2, 'Medium', N'Medium', 2), (3, 'High', N'High', 3), (4, 'Critical', N'Critical', 4)
) AS source (Id, Code, Name, SortOrder)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Name, SortOrder) VALUES (source.Id, source.Code, source.Name, source.SortOrder);
GO

/* IsIncomplete drives FR-07 (next milestone) and FR-08 (overdue), so it is data, not a
   condition repeated in every query. */
MERGE pmo.MilestoneStatus AS target
USING (VALUES
    (1, 'NotStarted', N'Not Started', 1, 1),
    (2, 'InProgress', N'In Progress', 1, 2),
    (3, 'Completed',  N'Completed',   0, 3),
    (4, 'Cancelled',  N'Cancelled',   0, 4)
) AS source (Id, Code, Name, IsIncomplete, SortOrder)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name,
                             IsIncomplete = source.IsIncomplete, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Name, IsIncomplete, SortOrder)
    VALUES (source.Id, source.Code, source.Name, source.IsIncomplete, source.SortOrder);
GO

MERGE pmo.RiskType AS target
USING (VALUES (1, 'Risk', N'Risk'), (2, 'Issue', N'Issue')) AS source (Id, Code, Name)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name
WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code, Name) VALUES (source.Id, source.Code, source.Name);
GO

MERGE pmo.RiskSeverity AS target
USING (VALUES (1, 'Low', N'Low', 1), (2, 'Medium', N'Medium', 2), (3, 'High', N'High', 3))
    AS source (Id, Code, Name, SortOrder)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Name, SortOrder) VALUES (source.Id, source.Code, source.Name, source.SortOrder);
GO

MERGE pmo.RiskStatus AS target
USING (VALUES (1, 'Open', N'Open'), (2, 'Closed', N'Closed'), (3, 'Cancelled', N'Cancelled'))
    AS source (Id, Code, Name)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name
WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code, Name) VALUES (source.Id, source.Code, source.Name);
GO

/* BR-07. Nothing is deleted; a record is Active, Closed or Cancelled. */
MERGE pmo.RecordState AS target
USING (VALUES (1, 'Active', N'Active'), (2, 'Closed', N'Closed'), (3, 'Cancelled', N'Cancelled'))
    AS source (Id, Code, Name)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name
WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code, Name) VALUES (source.Id, source.Code, source.Name);
GO

MERGE pmo.AssignmentRole AS target
USING (VALUES
    (1, 'ProjectManager',  N'Project Manager'),
    (2, 'ProjectOwner',    N'Project Owner'),
    (3, 'BusinessOwner',   N'Business Owner'),
    (4, 'Sponsor',         N'Sponsor'),
    (5, 'ConsultantStaff', N'Consultant / Vendor')
) AS source (Id, Code, Name)
ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Code = source.Code, Name = source.Name
WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code, Name) VALUES (source.Id, source.Code, source.Name);
GO
