# BRD traceability

Every requirement, business rule, notification and acceptance criterion in
*PMO Project Dashboard BRD v1.0*, and where it is implemented. Read it with `db/README.md`:
most rules live in a stored procedure, because that is where they cannot be stepped around.

## 5.1 Functional requirements

| Ref | Requirement | Where |
| --- | --- | --- |
| FR-01 | One record per project, unique Project ID | `pmo.Project`, `UQ_Project_Code`; the code is allocated under an update lock in `usp_Project_Create` |
| FR-02 | Status from a controlled list; free text not possible | `pmo.ProjectStatus` + FK; `ProjectStatus` enum; startup verifies the two match |
| FR-03 | Progress a whole number 0–100 | `CK_Project_Progress`, `CK_ProjectUpdate_Progress`, `UpdateValidator`, the form's slider |
| FR-04 | People from the corporate directory; consultant from a controlled list | `PersonRef` columns, `GraphDirectoryService`, `pmo.Consultant` |
| FR-05 | Only the PMO creates, closes or reopens; closed projects excluded from default views | `AuthorizationPolicies.Administer`, `IProjectAccessService.EnsureCanAdminister`, `usp_Project_Close` / `_Reopen`, `@View` in `usp_Portfolio_Search` |
| FR-06 | Any number of milestones, with planned, actual, owner, status, completion | `pmo.Milestone`, `usp_Milestone_Save`, `/projects/{id}/milestones` |
| FR-07 | Next milestone = earliest incomplete by planned date | `pmo.fn_NextMilestone`, `MilestoneRules.NextMilestone`, tested |
| FR-08 | Overdue milestones flagged in every view | `IsIncomplete` + planned date in the procedures; `Milestone.IsOverdueAt`; shown in words and colour |
| FR-09 | Risks and issues against the parent project | `pmo.RiskIssue`, `usp_Risk_Save`, `/projects/{id}/risks` |
| FR-10 | Open High count on the record and the dashboard; closed items keep a closure note | `OpenHighRisks` in `usp_Portfolio_Search`; `CK_RiskIssue_ClosureNote`; `usp_Risk_Close` |
| FR-11 | Managers submit through a form; dashboard not directly editable | `/updates/submit`; nothing else writes status, progress, phase or the attention flag |
| FR-12 | On submission: update the record, write dated history, stamp date and user | `usp_ProjectUpdate_Submit`, one transaction |
| FR-13 | Submitted updates are read-only | No procedure updates or deletes a submitted row; corrections are a later update |
| FR-14 | Full history in date order | `usp_ProjectUpdate_GetHistory`, project detail page |
| FR-15 | Form defaults to the previous update | `usp_ProjectUpdate_GetFormDefaults` — draft, then last submitted, then the record |
| FR-16 | Six dashboard indicators | `usp_Dashboard_Get` result set 1, `SummaryCard` |
| FR-17 | Portfolio table | `usp_Portfolio_Search`, `PortfolioTable` |
| FR-18 | Status in colour and in text | `StatusBadge` always renders the label; Excel export writes the words |
| FR-19 | Project detail page | `usp_Project_GetDetail` (5 result sets), `/projects/{id}` |
| FR-20 | Rolling milestone window and the attention list | `usp_Dashboard_Get` result sets 3 and 4 |
| FR-21 | Filtering by entity, department, status, priority, owner, consultant | `PortfolioFilter`, `usp_Portfolio_Search` parameters |
| FR-22 | A submitted update reaches the dashboard with no manual step | The dashboard reads the project record the submission wrote |
| FR-23 | Standard views | `PortfolioView`, `@View` in search; `usp_View_OverdueMilestones`, `_Risks`, `_NotReported` |
| FR-24 | Export to Excel; one-page status report as PDF | `/export/portfolio` (ClosedXML); the project page printed — the print stylesheet strips the chrome |
| FR-25 | "My projects" and search | `MyProjectsOnly`, `@SearchTerm` over name, code, owner and consultant |
| FR-26 | Documents on the project and on an update | `pmo.Attachment`, `BlobDocumentStore`, `/documents/{id}` |

## 5.2 Business rules

| Ref | Rule | Where |
| --- | --- | --- |
| BR-01 | Fixed status definitions | `pmo.ProjectStatus.Definition`, `ProjectStatusRules.Definition`, shown as the badge's tooltip |
| BR-02 | At Risk / Delayed require a key update | `usp_ProjectUpdate_Submit`; `UpdateValidator`; the form warns as soon as the status is chosen |
| BR-03 | Management attention requires a reason | `usp_ProjectUpdate_Submit`, `CK_Project_AttentionReason`, `UpdateValidator` |
| BR-04 | Completed = 100% with no open milestones | `usp_ProjectUpdate_Submit`, `UpdateValidator` |
| BR-05 | Progress may not decrease without an explanation | `usp_ProjectUpdate_Submit`, `UpdateValidator`, the form warns live |
| BR-06 | No update in the cycle → Not Reported | `IsNotReported` in the search and detail procedures; `ReportingRules.IsNotReported` |
| BR-07 | Nothing is deleted | No delete procedure exists for a project, milestone, risk or submitted update; `RecordState`, `MilestoneStatus.Cancelled`, `RiskStatus.Closed` |

A draft update is the one thing that is deleted, when it becomes the submission it was drafting.
A draft is the author's private working copy: it is not history, and no one else ever sees it.

## 5.3 Automated notifications

| Ref | Trigger | Where |
| --- | --- | --- |
| WF-01 | Status → At Risk | `NotificationService.OnUpdateSubmittedAsync`, PMO + Project Owner |
| WF-02 | Status → Delayed | Same, adding the Sponsor |
| WF-03 | Attention flagged | Same, PMO + management distribution list |
| WF-04 | Milestone due within [7] days, or overdue (repeating daily) | `RunReminderSweepAsync` + `usp_Notification_GetMilestoneReminders` |
| WF-05 | No update for [7] / [14] days | `RunReminderSweepAsync` + `usp_Notification_GetUpdateReminders`, two stages |
| WF-06 | High severity risk raised | `RiskService` → `OnHighSeverityRiskRaisedAsync`; the procedure decides what counts as newly High |
| WF-07 | Scheduled digest, [Monday 08:00] | `NotificationScheduler` → `SendPortfolioDigestAsync` |
| WF-08 | Project set to Completed | `OnUpdateSubmittedAsync`, PMO |

Every notification carries a deep link to the record. Each send is claimed in
`pmo.NotificationLog` against a key identifying the occasion, so a retry, a restart or a second
instance cannot send it twice.

## 6 Roles and permissions

| Role | Policy | Can do |
| --- | --- | --- |
| Management / Executive | `ViewPortfolio` | Sees everything, enters nothing — no page offers them a write |
| PMO Administrator | `Administer` | Everything, including create, close and reopen |
| Project Manager | `Contribute` | Submits updates and maintains milestones and risks on their own projects |
| Project Owner | `PortalUser` | Sees everything on their projects; does not submit on the manager's behalf |
| Consultant / Vendor | `Contribute` | Their contracted projects only — `fn_VisibleProjects` and `usp_Project_CanContribute` |
| IT Support | `ViewPortfolio` | Site administration; not a route into project data |

## 7 Non-functional requirements

| Area | How it is met |
| --- | --- |
| Performance | One procedure call per page; the dashboard is four result sets in one round trip; the portfolio search carries its own total, so paging costs no second query |
| Data currency | The dashboard reads the record the submission wrote — there is no refresh interval to wait for |
| Capacity | Indexes in `001_schema.sql` are sized for [500] projects, [10,000] milestones and risks, [50,000] updates; list views are paged, never unbounded |
| Usability | The form opens on the previous update (FR-15), so a routine update is a few edits |
| Accessibility | WCAG 2.1 AA: status in text as well as colour, labelled controls, a visible focus ring, semantic tables with scoped headers, contrast checked |
| Devices | Current Edge, Chrome and Safari; the layout reflows at tablet and phone width |
| Auditability | Every procedure that writes calls `usp_Audit_Write` with the named user and the time |
| Licensing | No commercial component. ClosedXML (MIT) for Excel; PDF is the browser's own print, so no PDF library is added to the estate |

## 8 Acceptance criteria

| Ref | Met by |
| --- | --- |
| AC-01 | `/admin/projects/new`; `usp_Project_Create` allocates the code under an update lock |
| AC-02 | `pmo.ProjectStatus` + FK everywhere a status is stored; enum verified against the database at startup |
| AC-03 | The update form; `usp_ProjectUpdate_Submit` writes the record, the stamp and the read-only history entry together |
| AC-04 | The dashboard's figures come from the same scoped query as the table beneath them |
| AC-05 | `usp_Project_GetDetail` returns overview, milestones, risks, history and documents in one call |
| AC-06 | `fn_NextMilestone` and the overdue flag, both tested |
| AC-07 | BR-02 rejection (tested); WF-01 and WF-02 notifications, logged |
| AC-08 | BR-03 rejection (tested); WF-03 notification; the item appears in the attention list |
| AC-09 | `NotificationScheduler`, with `pmo.NotificationLog` as the evidence |
| AC-10 | `fn_VisibleProjects` and `usp_Project_CanContribute`; policies on every page |
| AC-11 | `pmo.vw_*` read the same rows as the dashboard |
| AC-12 | Migration is the outstanding item — see `docs/04-migration.md` |

## Values still to be confirmed

The BRD leaves these in square brackets. They are configuration, not constants, so confirming
one is a setting change rather than a build:

| BRD | Setting | Default |
| --- | --- | --- |
| Update cycle [7] days | `Portal:UpdateCycleDays` | 7 |
| Escalation [14] days | `Portal:UpdateEscalationDays` | 14 |
| Milestone reminder [7] days | `Portal:MilestoneReminderDays` | 7 |
| Digest [Monday 08:00] | `Notifications:DigestDay`, `DigestTime` | Monday, 08:00 |
| Capacity [500] projects | Indexing and paging are sized for it | — |
| Dashboard [3] s, lists [5] s | To be measured at UAT with migrated volumes | — |
| OBJ-03, OBJ-06 target % | Reported by the compliance view | — |

One item in the BRD is a decision for the business rather than a setting: section 3 lists
"Migration of active projects, open milestones and open risks" in scope. That work is specified
in `docs/04-migration.md` and has not been built, because it needs the source data.
