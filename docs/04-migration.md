# Migration

Section 3 puts "migration of active projects, open milestones and open risks" in scope, and
AC-12 requires them loaded with correct status, progress, open milestones and risks, and
reconciled by the PMO.

**This has not been built.** It needs the source extract, and a load written against a shape
nobody has seen would be guesswork. What follows is the specification to build it from, once the
PMO provides the export.

## What is needed from the PMO

One workbook per list, or one workbook with one sheet each:

1. **Projects** — name, entity, department, project owner, business owner, project manager,
   sponsor, consultant, current status, progress %, phase, priority, start date, target
   completion, last update date.
2. **Milestones** — project, milestone, planned date, baseline date if one exists, actual date,
   status, completion %, owner.
3. **Risks and issues** — project, type, description, severity, owner, mitigation, due date,
   status, closure note for anything already closed.

People must be identifiable in the directory — a display name that matches Entra ID, or better,
an email address. A person who cannot be matched gets a record with a name but no object id,
which means no notification will reach them and the project will not appear in their "My
projects" view.

## How to build it

Follow the convention from `db/`: **generated, never hand-written**. A script derived from a
data file is produced by a generator that reads the source and fails loudly if its shape is not
what it expects — so a changed export is a clear error, not a quiet mis-load.

```
db/tools/generate_migration.py   reads the workbook, emits 900_migrate_projects.sql
db/900_migrate_projects.sql      generated; re-runnable, guarded with NOT EXISTS on ProjectCode
```

The generated script calls the same procedures the portal calls — `usp_Project_Create`,
`usp_Milestone_Save`, `usp_Risk_Save` — rather than inserting into the tables. The business
rules and the audit trail then apply to the migration exactly as they apply to a user, and a
migrated project is indistinguishable from one created in the portal.

Two points need a decision from the PMO before the generator is written:

- **Baseline dates.** If the source has no baseline, use the planned date as the baseline, which
  means migrated milestones start with zero slippage and slippage is measured from go-live. The
  alternative is to leave it null and report slippage only on milestones planned after go-live.
- **History.** The BRD migrates the current position, not the past. If the PMO wants the
  existing status history carried over, it needs a fourth sheet and one `usp_ProjectUpdate_Submit`
  call per historical update, oldest first, with notifications disabled for the run.

## Reconciling (AC-12)

After the load, with notifications switched off:

1. Compare counts: projects by entity and status, open milestones, open risks.
2. Compare each project's progress and status against the source.
3. Have the PMO sign off the portfolio view against the last manual pack.

Run the load against UAT first, reconcile there, and only then against production.
