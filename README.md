# PMO360 — Project Management Office Portal

*One View. Every Project.*

The PMO Project Dashboard for Dubai Investments PJSC and its reporting entities — Dubai
Investments Park, Dubai Investment Real Estate, Properties Investment and Al Taif Investment.
A single dashboard showing the current status of every project in the portfolio, a structured
form as the only point of data entry, and a complete dated history of how each project reached
its position.

Built to *PMO Project Dashboard BRD v1.0* (19-Sep-2026). Every requirement, business rule, role
and screen is implemented as the BRD states it. The BRD's section 4 describes a Microsoft Lists
and Power Automate solution; the build platform was set separately as .NET, SQL Server and
Azure, so what changes is the platform beneath the requirements, not the requirements.
`docs/01-architecture.md` sets out what replaces what, and `docs/02-brd-traceability.md` maps
every FR, BR, WF and AC to where it lives.

## Platform

- **.NET 8, Blazor Server** — the three screens of BRD section 9
- **SQL Server / Azure SQL** — **stored procedures only**; no ORM, no ad-hoc SQL
- **Azure App Service** — with a system-assigned managed identity as the principal throughout
- **Entra ID single sign-on** — roles from security groups, never from named individuals
- **Azure Blob Storage** — supporting documents
- **Microsoft Graph** — notifications from the PMO mailbox, and the directory picker
- **Power BI** — the executive layer, over the views in `db/020_views_powerbi.sql`

## Layout

```
db/         schema, stored procedures, reference data, reporting views   — start here
docs/       architecture, BRD traceability, deployment, migration
src/
  PMO360.Domain           entities, the five controlled statuses, the section 5.2 rules
  PMO360.Application      service contracts, DTOs, configuration
  PMO360.Infrastructure   ADO.NET over the procedures, Blob Storage, Graph, scheduling
  PMO360.Web              Blazor UI, sign-in, download and export endpoints
tests/      the business rules and the derivations
```

## Running it

```bash
# 1. Create the database and apply db/ in order — see db/README.md
# 2. Set the connection string, the Entra ID registration and the role groups
dotnet run --project src/PMO360.Web
```

The portal checks at startup that the database's controlled value lists match its own enums and
refuses to run if they differ, naming the mismatch — a script that was not applied shows as a
clear message rather than as a wrong dashboard.

```bash
dotnet test        # the business rules of section 5.2 and the derivations of FR-07, FR-08, BR-06
```

## Things worth knowing before changing anything

**All data access is through stored procedures in `db/`.** The application's SQL principal holds
`EXECUTE` and no table rights, so a write that has not gone through a procedure cannot happen.
That is what keeps the audit trail and the business rules from being stepped around. A new
query means a new procedure, committed in `db/`.

**The update form is the only write path** for status, progress, phase and the attention flag
(FR-11). `usp_ProjectUpdate_Submit` validates against section 5.2 and then, in one transaction,
writes the dated history entry, updates the project record and stamps the submitting user.
`UpdateValidator` in the domain holds a second copy of those rules — it exists so the form can
show every problem at once before anything is submitted. The procedure decides.

**Nothing is deleted** (BR-07). There is no delete procedure for a project, a milestone, a risk
or a submitted update; records are closed or cancelled. The one exception is a draft update,
which is the author's private working copy and is removed when it becomes the submission.

**Reference data is data.** Entities, departments, phases and consultants are maintained by
script in `db/003_reference_data.sql`, never seeded from code, so the business can add one
without a rebuild and a redeploy.

**Roles come from Entra ID groups.** Nothing in the application knows a group id; the mapping is
the `Authorization:RoleGroups` setting. Access is granted by adding somebody to a group.

**Dates are Dubai's.** Everything date-relative goes through `IClock`. A milestone must not
become overdue because a server in UTC has not yet reached midnight.

## Outstanding

Migration of the active portfolio (BRD section 3, AC-12) is specified in `docs/04-migration.md`
but not built: it needs the PMO's export of current projects, open milestones and open risks.
The values the BRD leaves in square brackets are configuration with sensible defaults, listed at
the end of `docs/02-brd-traceability.md` for confirmation before sign-off.
