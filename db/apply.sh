#!/usr/bin/env bash
#
# Applies the PMO360 database scripts, in order, with sqlcmd.
#
# Runs every .sql file in this folder in filename order — which is why they are numbered. A new
# script is picked up by being dropped in with the right number; there is no list here to keep
# in step.
#
# Two groups are held back unless you ask for them:
#   030_permissions.sql   needs the environment's principal names set inside it first.
#   9xx_*.sql             migration loads, which are run deliberately and once.
#
# Every script is re-runnable, so this can be applied to an empty database or to one that is
# already current. sqlcmd runs with -b, so the first error stops the run and this script exits
# non-zero — a failure is never buried under the scripts that follow it.
#
# Usage:
#   ./apply.sh --server pmo360-uat.database.windows.net
#   ./apply.sh --server localhost,1433 --auth sql --user sa --trust-cert --create-database
#   ./apply.sh --server pmo360-prod.database.windows.net --include-permissions
#
# With --auth sql the password is read from the SQLCMDPASSWORD environment variable, or
# prompted for. It is never passed on the command line, where it would be visible in the
# process list and in shell history.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

SERVER=""
DATABASE="PMO360"
AUTH="entra"
USERNAME=""
CREATE_DATABASE=0
INCLUDE_PERMISSIONS=0
INCLUDE_MIGRATION=0
TRUST_CERT=0
DRY_RUN=0

usage() {
    sed -n '3,26p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    cat <<'USAGE'

Options:
  -s, --server HOST            SQL Server instance (required)
  -d, --database NAME          Database name (default: PMO360)
  -a, --auth entra|sql|integrated
                               Authentication (default: entra)
  -u, --user NAME              Login name, for --auth sql
      --create-database        Create the database first if it does not exist
      --include-permissions    Also run 030_permissions.sql
      --include-migration      Also run any 9xx_*.sql migration script
      --trust-cert             Pass -C; for a local server with a self-signed certificate
      --dry-run                List what would run, and do not run it
  -h, --help                   Show this
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -s|--server)   SERVER="$2"; shift 2 ;;
        -d|--database) DATABASE="$2"; shift 2 ;;
        -a|--auth)     AUTH="$(echo "$2" | tr '[:upper:]' '[:lower:]')"; shift 2 ;;
        -u|--user)     USERNAME="$2"; shift 2 ;;
        --create-database)     CREATE_DATABASE=1; shift ;;
        --include-permissions) INCLUDE_PERMISSIONS=1; shift ;;
        --include-migration)   INCLUDE_MIGRATION=1; shift ;;
        --trust-cert)          TRUST_CERT=1; shift ;;
        --dry-run)             DRY_RUN=1; shift ;;
        -h|--help)     usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ -z "$SERVER" ]]; then
    echo "--server is required." >&2
    usage >&2
    exit 2
fi

if ! command -v sqlcmd >/dev/null 2>&1; then
    echo "sqlcmd was not found on PATH." >&2
    echo "Install the SQL Server command line utilities, or go-sqlcmd:" >&2
    echo "  https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility" >&2
    exit 127
fi

# ------------------------------------------------------------------ authentication
AUTH_ARGS=()
case "$AUTH" in
    entra)
        AUTH_ARGS+=(-G)
        if [[ -n "$USERNAME" ]]; then
            AUTH_ARGS+=(-U "$USERNAME")
        fi
        ;;
    sql)
        if [[ -z "$USERNAME" ]]; then
            echo "--user is required with --auth sql." >&2
            exit 2
        fi
        if [[ -z "${SQLCMDPASSWORD:-}" ]]; then
            read -r -s -p "Password for SQL login '$USERNAME': " SQLCMDPASSWORD
            echo
            export SQLCMDPASSWORD
        fi
        # sqlcmd reads SQLCMDPASSWORD itself, so the password stays out of the argument list.
        AUTH_ARGS+=(-U "$USERNAME")
        ;;
    integrated)
        AUTH_ARGS+=(-E)
        ;;
    *)
        echo "--auth must be entra, sql or integrated." >&2
        exit 2
        ;;
esac

# -b: stop on the first error and set the exit code. -I: quoted identifiers on, as the scripts expect.
COMMON_ARGS=(-S "$SERVER" -b -I "${AUTH_ARGS[@]}")
if [[ $TRUST_CERT -eq 1 ]]; then
    COMMON_ARGS+=(-C)
fi

# ------------------------------------------------------------------ which scripts
mapfile -t SCRIPTS < <(
    find "$SCRIPT_DIR" -maxdepth 1 -name '*.sql' -printf '%f\n' | sort | while read -r name; do
        if [[ $INCLUDE_PERMISSIONS -eq 0 && "$name" == "030_permissions.sql" ]]; then continue; fi
        if [[ $INCLUDE_MIGRATION -eq 0 && "$name" =~ ^9[0-9][0-9] ]]; then continue; fi
        echo "$name"
    done
)

if [[ ${#SCRIPTS[@]} -eq 0 ]]; then
    echo "No .sql scripts were found in $SCRIPT_DIR." >&2
    exit 1
fi

echo
echo "PMO360 database scripts"
echo "  server   $SERVER"
echo "  database $DATABASE"
echo "  auth     $AUTH"
[[ $DRY_RUN -eq 1 ]] && echo "  dry run — nothing will be executed"
echo

run_file() {
    local name="$1" target="$2" output
    printf '  %-42s ' "$name"

    if [[ $DRY_RUN -eq 1 ]]; then
        echo "skipped (dry run)"
        return 0
    fi

    if ! output="$(sqlcmd "${COMMON_ARGS[@]}" -d "$target" -i "$SCRIPT_DIR/$name" 2>&1)"; then
        echo "FAILED"
        echo
        echo "$output" | sed 's/^/    /' >&2
        echo >&2
        echo "$name failed. Nothing after it was run." >&2
        exit 1
    fi

    echo "ok"

    # sqlcmd reports a warning without failing; worth seeing, not worth stopping for.
    echo "$output" | grep -E 'Warning|Msg [0-9]+' | sed 's/^/    /' || true
}

# ------------------------------------------------------------------ create, then apply
if [[ $CREATE_DATABASE -eq 1 ]]; then
    echo "Database"
    printf '  %-42s ' "create $DATABASE if absent"
    if [[ $DRY_RUN -eq 1 ]]; then
        echo "skipped (dry run)"
    else
        if ! output="$(sqlcmd "${COMMON_ARGS[@]}" -d master \
                -Q "IF DB_ID(N'$DATABASE') IS NULL CREATE DATABASE [$DATABASE];" 2>&1)"; then
            echo "FAILED"
            echo "$output" | sed 's/^/    /' >&2
            exit 1
        fi
        echo "ok"
    fi
    echo
fi

echo "Scripts"
for name in "${SCRIPTS[@]}"; do
    run_file "$name" "$DATABASE"
done

echo
if [[ $DRY_RUN -eq 1 ]]; then
    echo "${#SCRIPTS[@]} script(s) would be applied to $DATABASE on $SERVER."
else
    echo "${#SCRIPTS[@]} script(s) applied to $DATABASE on $SERVER."
fi

if [[ $INCLUDE_PERMISSIONS -eq 0 ]]; then
    echo
    echo "030_permissions.sql was not run. Set the principal names inside it, then re-run with"
    echo "--include-permissions."
fi

echo
echo "The portal verifies the controlled value lists against its own enums at startup and"
echo "refuses to run on a mismatch, so a script that did not apply will say so on first start."
