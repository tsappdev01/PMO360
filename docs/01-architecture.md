# Architecture

PMO360 delivers the PMO Project Dashboard described in the BRD as a custom .NET application.
The BRD's section 4 describes a Microsoft 365 low-code solution — Microsoft Lists, Power
Automate, Power BI. The build platform was set separately: .NET 8 Blazor, SQL Server, Azure,
Entra ID single sign-on, Blob Storage and Microsoft Graph. Every requirement, business rule,
role and screen is implemented as the BRD states it; only the platform beneath them differs.

## What replaces what

| BRD (section 4.1) | PMO360 | Why it is equivalent |
| --- | --- | --- |
| Microsoft Lists as the single source of truth | SQL Server, schema in `db/` | One record per project, one dated history entry per update, nothing maintained by hand elsewhere. |
| Online portal | Blazor Server (.NET 8) on Azure App Service | The three screens of section 9, with the same read-only dashboard and single update form. |
| Power Automate flows | The portal raises WF-01, WF-02, WF-03, WF-06 and WF-08 as updates are submitted; a hosted `NotificationScheduler` runs WF-04, WF-05 and WF-07 | Same triggers, same recipients, same escalations, logged so they can be evidenced. |
| Power BI | Power BI, reading `pmo.vw_*` | Unchanged. The executive layer still reads the same records with no figures kept inside the report. |
| List attachments | Azure Blob Storage | Documents attach to the project record and to an individual update (FR-26). |
| Outlook via Power Automate | Microsoft Graph `sendMail` from the PMO mailbox | Same mailbox, and the PMO keeps a copy in Sent Items. |

## The projects

```
src/PMO360.Domain          entities, the five controlled statuses, the section 5.2 rules
src/PMO360.Application     service contracts, DTOs, configuration
src/PMO360.Infrastructure  ADO.NET over the stored procedures, Blob Storage, Graph, scheduling
src/PMO360.Web             Blazor Server UI, Entra ID sign-in, download and export endpoints
tests/PMO360.Tests         the business rules and the derivations
db/                        the schema, the procedures, the reference data, the reporting views
```

`Domain` and `Application` have no dependency on SQL Server, Azure or ASP.NET. The rules are
therefore testable without any of them, which is what `tests/` exercises.

## Data access

Stored procedures only. `PMO360.Infrastructure/Data/Db.cs` is the whole data access surface:
open a connection, call a procedure, read the rows. There is no ORM and no SQL text in the
application.

Two consequences are worth stating, because they are the reason for the choice:

- The application's SQL principal holds `EXECUTE` on the `pmo` schema and no table rights at
  all. A write that has not gone through a procedure cannot happen, so the audit trail and the
  business rules cannot be stepped around — not by a future feature, and not by an injection.
- The rules of section 5.2 live with the data. `usp_ProjectUpdate_Submit` validates and then,
  in one transaction, writes the history entry, updates the project record and stamps the last
  update date and the submitting user. A partial update is not possible.

The domain also carries a copy of those rules (`UpdateValidator`). That copy exists so the form
can show every problem at once, against the field it belongs to, before anything is submitted.
The procedure decides; the copy is there for the manager's benefit.

## Who can see what

Section 6 is applied in the database. Every read procedure takes the caller's Entra object id
and a `@CanSeeAll` flag and filters through `pmo.fn_VisibleProjects`. Management, the PMO and IT
Support see the whole portfolio; a project manager, owner, sponsor or consultant sees only the
projects they hold or are assigned to. A query cannot forget to apply the rule, because the rule
is inside the thing the query calls.

Roles come from Entra ID security groups, mapped to the BRD's six roles by
`RoleClaimsTransformation` using the `Authorization:RoleGroups` setting. Nothing in the
application knows a group id, and access is granted by adding somebody to a group — never to a
named individual.

## Dates

Every date-relative rule — overdue, the reporting cycle, the rolling window — is evaluated in
Dubai time through `IClock`, not in UTC. A milestone must not become overdue because a server
has not yet reached midnight. The clock is an interface so the tests can set "today".

## Scaling out

The portal is stateless apart from Blazor circuits, so it scales horizontally behind App
Service. The scheduler runs on every instance, and that is safe: each notification is claimed in
`pmo.NotificationLog` against a key identifying the occasion, and the unique index on that key
means only one instance can send. No distributed lock is needed.
