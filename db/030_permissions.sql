/*  PMO360 — 030_permissions.sql
    The application signs in to SQL as its Azure managed identity and reaches the data only
    through the procedures. Granting EXECUTE on the schema and nothing else means an injected
    or mistaken ad-hoc statement has no table to read: the procedure surface is the whole API.

    Run this once per environment, after the objects exist. Set @AppPrincipal to the App
    Service's managed identity name (for a system-assigned identity this is the App Service
    name, e.g. 'app-pmo360-prod').

    Re-runnable: the user is created only if absent, and GRANT is idempotent.
*/

DECLARE @AppPrincipal sysname = N'app-pmo360-prod';   -- <<< set per environment
DECLARE @sql nvarchar(max);

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @AppPrincipal)
BEGIN
    /* Azure SQL: contained user mapped to the managed identity. On SQL Server on a VM,
       replace this with a login-backed user instead. */
    SET @sql = N'CREATE USER ' + QUOTENAME(@AppPrincipal) + N' FROM EXTERNAL PROVIDER;';
    EXEC sp_executesql @sql;
END;
GO

DECLARE @AppPrincipal sysname = N'app-pmo360-prod';   -- <<< keep in step with the value above
DECLARE @sql nvarchar(max);

/* Execute on the procedure surface. */
SET @sql = N'GRANT EXECUTE ON SCHEMA::pmo TO ' + QUOTENAME(@AppPrincipal) + N';';
EXEC sp_executesql @sql;

/* The reporting views are read directly by Power BI and by the portal's Excel export. */
SET @sql = N'GRANT SELECT ON OBJECT::pmo.vw_Project TO ' + QUOTENAME(@AppPrincipal) + N';';
EXEC sp_executesql @sql;
SET @sql = N'GRANT SELECT ON OBJECT::pmo.vw_Milestone TO ' + QUOTENAME(@AppPrincipal) + N';';
EXEC sp_executesql @sql;
SET @sql = N'GRANT SELECT ON OBJECT::pmo.vw_RiskIssue TO ' + QUOTENAME(@AppPrincipal) + N';';
EXEC sp_executesql @sql;
SET @sql = N'GRANT SELECT ON OBJECT::pmo.vw_ProjectUpdate TO ' + QUOTENAME(@AppPrincipal) + N';';
EXEC sp_executesql @sql;

/*  No table rights are granted anywhere in this script, and that is the point: a principal
    holding only EXECUTE cannot read or write a table directly. The procedures reach the tables
    through ownership chaining, so every write carries its audit row and every business rule is
    applied. A DENY is deliberately not used here — it would also block the reporting views
    above, and DENY beats GRANT. */
GO

/* Power BI connects as its own principal, read-only, to the reporting views alone. */
DECLARE @BiPrincipal sysname = N'svc-pmo360-powerbi';   -- <<< set per environment
DECLARE @sql2 nvarchar(max);

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @BiPrincipal)
BEGIN
    SET @sql2 = N'CREATE USER ' + QUOTENAME(@BiPrincipal) + N' FROM EXTERNAL PROVIDER;';
    EXEC sp_executesql @sql2;
END;

SET @sql2 = N'GRANT SELECT ON OBJECT::pmo.vw_Project TO ' + QUOTENAME(@BiPrincipal) + N';';
EXEC sp_executesql @sql2;
SET @sql2 = N'GRANT SELECT ON OBJECT::pmo.vw_Milestone TO ' + QUOTENAME(@BiPrincipal) + N';';
EXEC sp_executesql @sql2;
SET @sql2 = N'GRANT SELECT ON OBJECT::pmo.vw_RiskIssue TO ' + QUOTENAME(@BiPrincipal) + N';';
EXEC sp_executesql @sql2;
SET @sql2 = N'GRANT SELECT ON OBJECT::pmo.vw_ProjectUpdate TO ' + QUOTENAME(@BiPrincipal) + N';';
EXEC sp_executesql @sql2;
SET @sql2 = N'GRANT SELECT ON OBJECT::pmo.vw_ReportingCompliance TO ' + QUOTENAME(@BiPrincipal) + N';';
EXEC sp_executesql @sql2;
GO
