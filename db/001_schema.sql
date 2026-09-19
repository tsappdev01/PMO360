/*  PMO360 — 001_schema.sql
    Tables, keys, constraints and indexes for the PMO Project Dashboard (BRD v1.0, section 4.3).

    Re-runnable: every object is guarded, so the script can be applied to an empty database or
    to one that is already current. CREATE SCHEMA and each CREATE INDEX sit in their own batch —
    a batch aborts on error and takes the rest of the batch with it, which hides later statements.

    Column widths are the ones the application enforces; the database is the second line, not the
    only one. Nothing here is created by the application at runtime: the schema is this script.
*/

IF SCHEMA_ID('pmo') IS NULL
    EXEC ('CREATE SCHEMA pmo AUTHORIZATION dbo;');
GO

/* ---------------------------------------------------------------------------
   Controlled value lists (FR-02, section 4.2 "controlled choice fields").
   Ids are fixed and must match the enums in PMO360.Domain.Enums — the portal
   verifies this at startup (usp_System_GetEnumerations) and refuses to run on
   a mismatch, so a drift is a clear message rather than a wrong dashboard.
---------------------------------------------------------------------------- */
IF OBJECT_ID('pmo.ProjectStatus', 'U') IS NULL
CREATE TABLE pmo.ProjectStatus
(
    Id          tinyint       NOT NULL CONSTRAINT PK_ProjectStatus PRIMARY KEY,
    Code        varchar(20)   NOT NULL CONSTRAINT UQ_ProjectStatus_Code UNIQUE,
    Name        nvarchar(50)  NOT NULL,
    Definition  nvarchar(400) NOT NULL,
    SortOrder   tinyint       NOT NULL
);
GO

IF OBJECT_ID('pmo.ProjectPriority', 'U') IS NULL
CREATE TABLE pmo.ProjectPriority
(
    Id        tinyint      NOT NULL CONSTRAINT PK_ProjectPriority PRIMARY KEY,
    Code      varchar(20)  NOT NULL CONSTRAINT UQ_ProjectPriority_Code UNIQUE,
    Name      nvarchar(50) NOT NULL,
    SortOrder tinyint      NOT NULL
);
GO

IF OBJECT_ID('pmo.MilestoneStatus', 'U') IS NULL
CREATE TABLE pmo.MilestoneStatus
(
    Id        tinyint      NOT NULL CONSTRAINT PK_MilestoneStatus PRIMARY KEY,
    Code      varchar(20)  NOT NULL CONSTRAINT UQ_MilestoneStatus_Code UNIQUE,
    Name      nvarchar(50) NOT NULL,
    /* Counts towards "next milestone" (FR-07) and "overdue" (FR-08). */
    IsIncomplete bit       NOT NULL,
    SortOrder tinyint      NOT NULL
);
GO

IF OBJECT_ID('pmo.RiskType', 'U') IS NULL
CREATE TABLE pmo.RiskType
(
    Id   tinyint      NOT NULL CONSTRAINT PK_RiskType PRIMARY KEY,
    Code varchar(20)  NOT NULL CONSTRAINT UQ_RiskType_Code UNIQUE,
    Name nvarchar(50) NOT NULL
);
GO

IF OBJECT_ID('pmo.RiskSeverity', 'U') IS NULL
CREATE TABLE pmo.RiskSeverity
(
    Id        tinyint      NOT NULL CONSTRAINT PK_RiskSeverity PRIMARY KEY,
    Code      varchar(20)  NOT NULL CONSTRAINT UQ_RiskSeverity_Code UNIQUE,
    Name      nvarchar(50) NOT NULL,
    SortOrder tinyint      NOT NULL
);
GO

IF OBJECT_ID('pmo.RiskStatus', 'U') IS NULL
CREATE TABLE pmo.RiskStatus
(
    Id   tinyint      NOT NULL CONSTRAINT PK_RiskStatus PRIMARY KEY,
    Code varchar(20)  NOT NULL CONSTRAINT UQ_RiskStatus_Code UNIQUE,
    Name nvarchar(50) NOT NULL
);
GO

IF OBJECT_ID('pmo.RecordState', 'U') IS NULL
CREATE TABLE pmo.RecordState
(
    Id   tinyint      NOT NULL CONSTRAINT PK_RecordState PRIMARY KEY,
    Code varchar(20)  NOT NULL CONSTRAINT UQ_RecordState_Code UNIQUE,
    Name nvarchar(50) NOT NULL
);
GO

IF OBJECT_ID('pmo.AssignmentRole', 'U') IS NULL
CREATE TABLE pmo.AssignmentRole
(
    Id   tinyint      NOT NULL CONSTRAINT PK_AssignmentRole PRIMARY KEY,
    Code varchar(30)  NOT NULL CONSTRAINT UQ_AssignmentRole_Code UNIQUE,
    Name nvarchar(50) NOT NULL
);
GO

/* ---------------------------------------------------------------------------
   Reference data (section 4.3 "Reference lists"). Maintained by script in db/,
   never seeded from application code, so the business can change the list
   without a rebuild and a redeploy.
---------------------------------------------------------------------------- */
IF OBJECT_ID('pmo.ReportingEntity', 'U') IS NULL
CREATE TABLE pmo.ReportingEntity
(
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReportingEntity PRIMARY KEY,
    Code      varchar(20)   NOT NULL CONSTRAINT UQ_ReportingEntity_Code UNIQUE,
    Name      nvarchar(200) NOT NULL,
    SortOrder int           NOT NULL CONSTRAINT DF_ReportingEntity_SortOrder DEFAULT (100),
    IsActive  bit           NOT NULL CONSTRAINT DF_ReportingEntity_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID('pmo.Department', 'U') IS NULL
CREATE TABLE pmo.Department
(
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Department PRIMARY KEY,
    Name      nvarchar(200) NOT NULL CONSTRAINT UQ_Department_Name UNIQUE,
    SortOrder int           NOT NULL CONSTRAINT DF_Department_SortOrder DEFAULT (100),
    IsActive  bit           NOT NULL CONSTRAINT DF_Department_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID('pmo.Phase', 'U') IS NULL
CREATE TABLE pmo.Phase
(
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Phase PRIMARY KEY,
    Name      nvarchar(100) NOT NULL CONSTRAINT UQ_Phase_Name UNIQUE,
    SortOrder int           NOT NULL CONSTRAINT DF_Phase_SortOrder DEFAULT (100),
    IsActive  bit           NOT NULL CONSTRAINT DF_Phase_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID('pmo.Consultant', 'U') IS NULL
CREATE TABLE pmo.Consultant
(
    Id       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Consultant PRIMARY KEY,
    Name     nvarchar(200) NOT NULL CONSTRAINT UQ_Consultant_Name UNIQUE,
    IsActive bit           NOT NULL CONSTRAINT DF_Consultant_IsActive DEFAULT (1)
);
GO

/* ---------------------------------------------------------------------------
   pmo.Project — FR-01. One master record per project, across every entity.
   The current position lives here; how the project reached it lives in
   pmo.ProjectUpdate. Status, progress, phase and the attention flag are written
   only by usp_ProjectUpdate_Submit (FR-11).
---------------------------------------------------------------------------- */
IF OBJECT_ID('pmo.Project', 'U') IS NULL
CREATE TABLE pmo.Project
(
    Id                      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Project PRIMARY KEY,
    ProjectCode             varchar(20)    NOT NULL CONSTRAINT UQ_Project_Code UNIQUE,  -- FR-01, AC-01
    Name                    nvarchar(250)  NOT NULL,
    Description             nvarchar(2000) NULL,

    ReportingEntityId       int            NOT NULL CONSTRAINT FK_Project_ReportingEntity REFERENCES pmo.ReportingEntity (Id),
    DepartmentId            int            NOT NULL CONSTRAINT FK_Project_Department      REFERENCES pmo.Department (Id),
    ConsultantId            int            NULL     CONSTRAINT FK_Project_Consultant      REFERENCES pmo.Consultant (Id),
    CurrentPhaseId          int            NULL     CONSTRAINT FK_Project_Phase           REFERENCES pmo.Phase (Id),

    /* FR-04. People come from the corporate directory and are stored on the record, so a
       historical view still shows who held the role at the time. */
    ProjectOwnerObjectId    varchar(64)    NULL,
    ProjectOwnerDisplayName nvarchar(200)  NOT NULL,
    ProjectOwnerEmail       nvarchar(320)  NULL,
    BusinessOwnerObjectId   varchar(64)    NULL,
    BusinessOwnerDisplayName nvarchar(200) NOT NULL,
    BusinessOwnerEmail      nvarchar(320)  NULL,
    ProjectManagerObjectId  varchar(64)    NULL,
    ProjectManagerDisplayName nvarchar(200) NOT NULL,
    ProjectManagerEmail     nvarchar(320)  NULL,
    SponsorObjectId         varchar(64)    NULL,
    SponsorDisplayName      nvarchar(200)  NOT NULL,
    SponsorEmail            nvarchar(320)  NULL,

    StatusId                tinyint        NOT NULL CONSTRAINT FK_Project_Status   REFERENCES pmo.ProjectStatus (Id),
    ProgressPercent         int            NOT NULL CONSTRAINT CK_Project_Progress CHECK (ProgressPercent BETWEEN 0 AND 100),  -- FR-03
    PriorityId              tinyint        NOT NULL CONSTRAINT FK_Project_Priority REFERENCES pmo.ProjectPriority (Id),

    StartDate               date           NOT NULL,
    TargetCompletionDate    date           NOT NULL,

    KeyUpdate               nvarchar(2000) NULL,
    NextAction              nvarchar(1000) NULL,
    NextActionOwnerObjectId varchar(64)    NULL,
    NextActionOwnerDisplayName nvarchar(200) NULL,
    NextActionOwnerEmail    nvarchar(320)  NULL,
    NextActionDueDate       date           NULL,

    AttentionRequired       bit            NOT NULL CONSTRAINT DF_Project_Attention DEFAULT (0),   -- FR-16
    AttentionReason         nvarchar(1000) NULL,                                                   -- BR-03

    LastUpdateDate          date           NULL,                                                   -- FR-12, stamped
    LastUpdatedByObjectId   varchar(64)    NULL,
    LastUpdatedByDisplayName nvarchar(200) NULL,
    LastUpdatedByEmail      nvarchar(320)  NULL,

    StateId                 tinyint        NOT NULL CONSTRAINT FK_Project_State REFERENCES pmo.RecordState (Id),  -- FR-05, BR-07
    ClosedOn                datetimeoffset(0) NULL,
    ClosedByObjectId        varchar(64)    NULL,
    ClosedByDisplayName     nvarchar(200)  NULL,
    ClosureNote             nvarchar(2000) NULL,

    CreatedOn               datetimeoffset(0) NOT NULL,
    CreatedByObjectId       varchar(64)    NULL,
    CreatedByDisplayName    nvarchar(200)  NOT NULL,
    CreatedByEmail          nvarchar(320)  NULL,

    /* BR-03, enforced by the database as well as by the form: attention without a reason
       cannot be stored, whatever route the write came in by. */
    CONSTRAINT CK_Project_AttentionReason
        CHECK (AttentionRequired = 0 OR (AttentionReason IS NOT NULL AND LEN(LTRIM(AttentionReason)) >= 10))
);
GO

IF OBJECT_ID('pmo.Milestone', 'U') IS NULL
CREATE TABLE pmo.Milestone
(
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Milestone PRIMARY KEY,
    ProjectId         int           NOT NULL CONSTRAINT FK_Milestone_Project REFERENCES pmo.Project (Id),
    Name              nvarchar(250) NOT NULL,
    PlannedDate       date          NOT NULL,
    /* Set once, at the first approved plan, and not moved again — the slippage reference. */
    BaselineDate      date          NULL,
    ActualDate        date          NULL,
    StatusId          tinyint       NOT NULL CONSTRAINT FK_Milestone_Status REFERENCES pmo.MilestoneStatus (Id),
    CompletionPercent int           NOT NULL CONSTRAINT CK_Milestone_Completion CHECK (CompletionPercent BETWEEN 0 AND 100),
    OwnerObjectId     varchar(64)   NULL,
    OwnerDisplayName  nvarchar(200) NOT NULL,
    OwnerEmail        nvarchar(320) NULL,
    CreatedOn         datetimeoffset(0) NOT NULL,
    CreatedByDisplayName nvarchar(200) NOT NULL,
    ModifiedOn        datetimeoffset(0) NULL,
    ModifiedByDisplayName nvarchar(200) NULL
);
GO

IF OBJECT_ID('pmo.RiskIssue', 'U') IS NULL
CREATE TABLE pmo.RiskIssue
(
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RiskIssue PRIMARY KEY,
    ProjectId        int            NOT NULL CONSTRAINT FK_RiskIssue_Project  REFERENCES pmo.Project (Id),
    TypeId           tinyint        NOT NULL CONSTRAINT FK_RiskIssue_Type     REFERENCES pmo.RiskType (Id),
    Description      nvarchar(2000) NOT NULL,
    SeverityId       tinyint        NOT NULL CONSTRAINT FK_RiskIssue_Severity REFERENCES pmo.RiskSeverity (Id),
    OwnerObjectId    varchar(64)    NULL,
    OwnerDisplayName nvarchar(200)  NOT NULL,
    OwnerEmail       nvarchar(320)  NULL,
    Mitigation       nvarchar(2000) NULL,
    DueDate          date           NULL,
    StatusId         tinyint        NOT NULL CONSTRAINT FK_RiskIssue_Status   REFERENCES pmo.RiskStatus (Id),
    ClosureNote      nvarchar(2000) NULL,                                       -- FR-10
    ClosedOn         datetimeoffset(0) NULL,
    ClosedByDisplayName nvarchar(200) NULL,
    RaisedOn         datetimeoffset(0) NOT NULL,
    RaisedByObjectId varchar(64)    NULL,
    RaisedByDisplayName nvarchar(200) NOT NULL,
    ModifiedOn       datetimeoffset(0) NULL,
    ModifiedByDisplayName nvarchar(200) NULL,

    /* FR-10. A closed item keeps its record and carries the reason it was closed. */
    CONSTRAINT CK_RiskIssue_ClosureNote
        CHECK (StatusId <> 2 OR (ClosureNote IS NOT NULL AND LEN(LTRIM(ClosureNote)) > 0))
);
GO

/* ---------------------------------------------------------------------------
   pmo.ProjectUpdate — FR-12, FR-13, FR-14. The dated history.
   A submitted row is never updated or deleted: there is no procedure that does
   so, and a correction is a later update. Drafts (IsDraft = 1) are the author's
   private working copy, are not history, and never reach the dashboard.
---------------------------------------------------------------------------- */
IF OBJECT_ID('pmo.ProjectUpdate', 'U') IS NULL
CREATE TABLE pmo.ProjectUpdate
(
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectUpdate PRIMARY KEY,
    ProjectId         int            NOT NULL CONSTRAINT FK_ProjectUpdate_Project REFERENCES pmo.Project (Id),
    UpdateDate        date           NOT NULL,
    StatusId          tinyint        NOT NULL CONSTRAINT FK_ProjectUpdate_Status  REFERENCES pmo.ProjectStatus (Id),
    ProgressPercent   int            NOT NULL CONSTRAINT CK_ProjectUpdate_Progress CHECK (ProgressPercent BETWEEN 0 AND 100),
    PhaseId           int            NULL     CONSTRAINT FK_ProjectUpdate_Phase     REFERENCES pmo.Phase (Id),
    MilestoneId       int            NULL     CONSTRAINT FK_ProjectUpdate_Milestone REFERENCES pmo.Milestone (Id),
    KeyUpdate         nvarchar(2000) NULL,                                            -- BR-02
    Achievement       nvarchar(2000) NULL,
    NextAction        nvarchar(1000) NULL,
    NextActionOwnerObjectId varchar(64) NULL,
    NextActionOwnerDisplayName nvarchar(200) NULL,
    NextActionOwnerEmail nvarchar(320) NULL,
    NextActionDueDate date           NULL,
    AttentionRequired bit            NOT NULL CONSTRAINT DF_ProjectUpdate_Attention DEFAULT (0),
    AttentionReason   nvarchar(1000) NULL,
    IsDraft           bit            NOT NULL CONSTRAINT DF_ProjectUpdate_IsDraft DEFAULT (0),
    SubmittedByObjectId varchar(64)  NULL,
    SubmittedByDisplayName nvarchar(200) NOT NULL,
    SubmittedByEmail  nvarchar(320)  NULL,
    SubmittedOn       datetimeoffset(0) NOT NULL,
    /* The position immediately before this update, so a trend and an alert can be read from
       one row without walking the history. */
    PreviousStatusId  tinyint        NULL CONSTRAINT FK_ProjectUpdate_PreviousStatus REFERENCES pmo.ProjectStatus (Id),
    PreviousProgressPercent int      NULL
);
GO

IF OBJECT_ID('pmo.ProjectAssignment', 'U') IS NULL
CREATE TABLE pmo.ProjectAssignment
(
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectAssignment PRIMARY KEY,
    ProjectId        int           NOT NULL CONSTRAINT FK_ProjectAssignment_Project REFERENCES pmo.Project (Id),
    PersonObjectId   varchar(64)   NULL,
    PersonDisplayName nvarchar(200) NOT NULL,
    PersonEmail      nvarchar(320) NULL,
    RoleId           tinyint       NOT NULL CONSTRAINT FK_ProjectAssignment_Role REFERENCES pmo.AssignmentRole (Id),
    AssignedOn       datetimeoffset(0) NOT NULL,
    AssignedByDisplayName nvarchar(200) NOT NULL,
    /* Guest consultant access is reviewed at the end of an engagement (section 6). Ending an
       assignment removes the access without removing the record of who held it. */
    EndsOn           date          NULL
);
GO

IF OBJECT_ID('pmo.Attachment', 'U') IS NULL
CREATE TABLE pmo.Attachment
(
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachment PRIMARY KEY,
    Scope           tinyint       NOT NULL CONSTRAINT CK_Attachment_Scope CHECK (Scope IN (1, 2)),  -- 1 project, 2 update
    ProjectId       int           NOT NULL CONSTRAINT FK_Attachment_Project REFERENCES pmo.Project (Id),
    ProjectUpdateId int           NULL     CONSTRAINT FK_Attachment_Update  REFERENCES pmo.ProjectUpdate (Id),
    /* Server-generated, never the user's file name, so an uploaded name cannot steer the path. */
    BlobName        nvarchar(400) NOT NULL,
    FileName        nvarchar(260) NOT NULL,
    ContentType     nvarchar(150) NOT NULL,
    SizeBytes       bigint        NOT NULL,
    UploadedOn      datetimeoffset(0) NOT NULL,
    UploadedByObjectId varchar(64) NULL,
    UploadedByDisplayName nvarchar(200) NOT NULL
);
GO

IF OBJECT_ID('pmo.NotificationLog', 'U') IS NULL
CREATE TABLE pmo.NotificationLog
(
    Id         bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificationLog PRIMARY KEY,
    Kind       tinyint        NOT NULL,          -- NotificationKind: WF-01 .. WF-08
    ProjectId  int            NULL CONSTRAINT FK_NotificationLog_Project REFERENCES pmo.Project (Id),
    MilestoneId int           NULL CONSTRAINT FK_NotificationLog_Milestone REFERENCES pmo.Milestone (Id),
    RiskIssueId int           NULL CONSTRAINT FK_NotificationLog_Risk REFERENCES pmo.RiskIssue (Id),
    Recipients nvarchar(2000) NOT NULL,
    Subject    nvarchar(400)  NOT NULL,
    SentOn     datetimeoffset(0) NOT NULL,
    Succeeded  bit            NOT NULL,
    Error      nvarchar(2000) NULL,
    /* Identifies the occasion, so a restart or a second scheduler pass cannot send twice. */
    DedupeKey  varchar(200)   NOT NULL CONSTRAINT UQ_NotificationLog_DedupeKey UNIQUE
);
GO

IF OBJECT_ID('pmo.AuditEntry', 'U') IS NULL
CREATE TABLE pmo.AuditEntry
(
    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEntry PRIMARY KEY,
    EntityName   varchar(100)   NOT NULL,
    EntityKey    varchar(100)   NOT NULL,
    Action       varchar(30)    NOT NULL,
    UserObjectId varchar(64)    NULL,
    UserName     nvarchar(200)  NOT NULL,
    OccurredOn   datetimeoffset(0) NOT NULL,
    Detail       nvarchar(max)  NULL
);
GO

/* ---------------------------------------------------------------------------
   Indexes. Section 7 asks for [500] projects, [10,000] milestones and risks and
   [50,000] updates "using indexed columns and filtered views". Each index below
   is here for a named query, not on principle.
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Project_State_Status' AND object_id = OBJECT_ID('pmo.Project'))
CREATE INDEX IX_Project_State_Status ON pmo.Project (StateId, StatusId) INCLUDE (ProgressPercent, AttentionRequired);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Project_State_Entity' AND object_id = OBJECT_ID('pmo.Project'))
CREATE INDEX IX_Project_State_Entity ON pmo.Project (StateId, ReportingEntityId, DepartmentId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Project_State_LastUpdate' AND object_id = OBJECT_ID('pmo.Project'))
CREATE INDEX IX_Project_State_LastUpdate ON pmo.Project (StateId, LastUpdateDate);  -- BR-06, WF-05
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Project_Manager' AND object_id = OBJECT_ID('pmo.Project'))
CREATE INDEX IX_Project_Manager ON pmo.Project (ProjectManagerObjectId) INCLUDE (StateId);  -- "My projects", scoping
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Milestone_Open' AND object_id = OBJECT_ID('pmo.Milestone'))
CREATE INDEX IX_Milestone_Open ON pmo.Milestone (ProjectId, StatusId, PlannedDate) INCLUDE (Name, CompletionPercent);  -- FR-07, FR-08
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Milestone_Planned' AND object_id = OBJECT_ID('pmo.Milestone'))
CREATE INDEX IX_Milestone_Planned ON pmo.Milestone (StatusId, PlannedDate) INCLUDE (ProjectId);  -- WF-04 sweep
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RiskIssue_Open' AND object_id = OBJECT_ID('pmo.RiskIssue'))
CREATE INDEX IX_RiskIssue_Open ON pmo.RiskIssue (ProjectId, StatusId, SeverityId);  -- FR-10
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RiskIssue_OpenHigh' AND object_id = OBJECT_ID('pmo.RiskIssue'))
CREATE INDEX IX_RiskIssue_OpenHigh ON pmo.RiskIssue (StatusId, SeverityId, DueDate) INCLUDE (ProjectId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectUpdate_History' AND object_id = OBJECT_ID('pmo.ProjectUpdate'))
CREATE INDEX IX_ProjectUpdate_History ON pmo.ProjectUpdate (ProjectId, IsDraft, UpdateDate DESC, Id DESC);  -- FR-14
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectUpdate_Recent' AND object_id = OBJECT_ID('pmo.ProjectUpdate'))
CREATE INDEX IX_ProjectUpdate_Recent ON pmo.ProjectUpdate (IsDraft, SubmittedOn DESC) INCLUDE (ProjectId, StatusId);
GO

/* One draft per author per project: "Save as draft" replaces the author's own draft rather than
   accumulating copies. Filtered, so submitted history is unaffected. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProjectUpdate_Draft' AND object_id = OBJECT_ID('pmo.ProjectUpdate'))
CREATE UNIQUE INDEX UX_ProjectUpdate_Draft ON pmo.ProjectUpdate (ProjectId, SubmittedByObjectId)
    WHERE IsDraft = 1 AND SubmittedByObjectId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectAssignment_Person' AND object_id = OBJECT_ID('pmo.ProjectAssignment'))
CREATE INDEX IX_ProjectAssignment_Person ON pmo.ProjectAssignment (PersonObjectId, ProjectId) INCLUDE (RoleId, EndsOn);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_Project' AND object_id = OBJECT_ID('pmo.Attachment'))
CREATE INDEX IX_Attachment_Project ON pmo.Attachment (ProjectId, Scope);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditEntry_Entity' AND object_id = OBJECT_ID('pmo.AuditEntry'))
CREATE INDEX IX_AuditEntry_Entity ON pmo.AuditEntry (EntityName, EntityKey, OccurredOn DESC);
GO
