<#
.SYNOPSIS
    Applies the PMO360 database scripts, in order, with sqlcmd.

.DESCRIPTION
    Runs every .sql file in this folder in filename order — which is why they are numbered.
    A new script is picked up by being dropped in with the right number; there is no list here
    to keep in step.

    Two groups are held back unless you ask for them:
      030_permissions.sql  needs the environment's principal names set inside it first.
      9xx_*.sql            migration loads, which are run deliberately and once.

    Every script is re-runnable, so this can be applied to an empty database or to one that is
    already current. sqlcmd runs with -b, so the first error stops the run and this script
    exits non-zero — a failure is never buried under the scripts that follow it.

.PARAMETER Server
    The SQL Server instance, e.g. pmo360-prod.database.windows.net or localhost,1433.

.PARAMETER Database
    Defaults to PMO360.

.PARAMETER Auth
    Entra      (default) Microsoft Entra ID — sqlcmd -G. Use this for Azure SQL.
    Sql        SQL login, with -User and -Password.
    Integrated Windows authentication — sqlcmd -E.

.PARAMETER CreateDatabase
    Creates the database first if it does not exist. Leave this off against Azure SQL, where
    the database is created with the server.

.PARAMETER IncludePermissions
    Also runs 030_permissions.sql. Set the principal names inside that file first.

.PARAMETER IncludeMigration
    Also runs any 9xx_*.sql migration script. See docs/04-migration.md.

.PARAMETER TrustCert
    Passes -C. Needed against a local SQL Server with a self-signed certificate; never needed,
    and should not be used, against Azure SQL.

.PARAMETER DryRun
    Lists what would run, and does not run it.

.EXAMPLE
    ./apply.ps1 -Server pmo360-uat.database.windows.net

.EXAMPLE
    ./apply.ps1 -Server localhost,1433 -Auth Sql -User sa -TrustCert -CreateDatabase

.EXAMPLE
    ./apply.ps1 -Server pmo360-prod.database.windows.net -IncludePermissions
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $Database = 'PMO360',

    [ValidateSet('Entra', 'Sql', 'Integrated')]
    [string] $Auth = 'Entra',

    [string] $User,

    [securestring] $Password,

    [switch] $CreateDatabase,
    [switch] $IncludePermissions,
    [switch] $IncludeMigration,
    [switch] $TrustCert,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd was not found on PATH. Install it with 'winget install sqlcmd', or from the " +
          "SQL Server command line utilities."
}

# ---------------------------------------------------------------- authentication
$authArgs = switch ($Auth) {
    'Entra' {
        if ($User) {
            if (-not $Password) { throw "-Password is required when -Auth Entra is used with -User." }
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
            @('-G', '-U', $User, '-P', $plain)
        }
        else {
            # Entra with no user: sqlcmd uses the signed-in identity, prompting if it must.
            @('-G')
        }
    }
    'Sql' {
        if (-not $User) { throw "-User is required when -Auth Sql is used." }
        if (-not $Password) {
            $Password = Read-Host -AsSecureString "Password for SQL login '$User'"
        }
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
        @('-U', $User, '-P', $plain)
    }
    'Integrated' { @('-E') }
}

# -b: stop on the first error and set the exit code. -I: quoted identifiers on, as the scripts expect.
$commonArgs = @('-S', $Server, '-b', '-I') + $authArgs
if ($TrustCert) { $commonArgs += '-C' }

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $TargetDatabase
    )

    $name = Split-Path -Leaf $Path
    Write-Host ("  {0,-42} " -f $name) -NoNewline

    if ($DryRun) {
        Write-Host 'skipped (dry run)' -ForegroundColor DarkGray
        return
    }

    $output = & sqlcmd @commonArgs '-d' $TargetDatabase '-i' $Path 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAILED' -ForegroundColor Red
        Write-Host ''
        $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "$name failed. Nothing after it was run."
    }

    Write-Host 'ok' -ForegroundColor Green

    # sqlcmd reports a warning without failing; worth seeing, not worth stopping for.
    $output | Where-Object { $_ -match 'Warning|Msg \d+' } |
        ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}

# ---------------------------------------------------------------- which scripts
$scripts = Get-ChildItem -Path $scriptRoot -Filter '*.sql' |
    Where-Object { $IncludePermissions -or $_.Name -ne '030_permissions.sql' } |
    Where-Object { $IncludeMigration -or $_.Name -notmatch '^9\d\d' } |
    Sort-Object Name

if (-not $scripts) {
    throw "No .sql scripts were found in $scriptRoot."
}

Write-Host ''
Write-Host "PMO360 database scripts" -ForegroundColor Cyan
Write-Host "  server   $Server"
Write-Host "  database $Database"
Write-Host "  auth     $Auth"
if ($DryRun) { Write-Host "  dry run — nothing will be executed" -ForegroundColor DarkGray }
Write-Host ''

# ---------------------------------------------------------------- create, then apply
if ($CreateDatabase) {
    Write-Host 'Database' -ForegroundColor Cyan
    Write-Host ("  {0,-42} " -f "create $Database if absent") -NoNewline

    if ($DryRun) {
        Write-Host 'skipped (dry run)' -ForegroundColor DarkGray
    }
    else {
        $create = "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];"
        $output = & sqlcmd @commonArgs '-d' 'master' '-Q' $create 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAILED' -ForegroundColor Red
            $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            throw "The database could not be created."
        }
        Write-Host 'ok' -ForegroundColor Green
    }
    Write-Host ''
}

Write-Host 'Scripts' -ForegroundColor Cyan
foreach ($script in $scripts) {
    Invoke-SqlFile -Path $script.FullName -TargetDatabase $Database
}

Write-Host ''
$verb = if ($DryRun) { 'would be applied' } else { 'applied' }
Write-Host "$($scripts.Count) script(s) $verb to $Database on $Server." -ForegroundColor Green

if (-not $IncludePermissions) {
    Write-Host ''
    Write-Host '030_permissions.sql was not run. Set the principal names inside it, then re-run' -ForegroundColor Yellow
    Write-Host 'with -IncludePermissions.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'The portal verifies the controlled value lists against its own enums at startup and' -ForegroundColor DarkGray
Write-Host 'refuses to run on a mismatch, so a script that did not apply will say so on first start.' -ForegroundColor DarkGray
