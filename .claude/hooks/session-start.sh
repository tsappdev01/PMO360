#!/bin/bash
#
# SessionStart hook — prepares a Claude Code on the web container to build and test PMO360.
#
# The container starts without a .NET SDK, so `dotnet build` and `dotnet test` are unavailable
# until this has run. Container state is cached after the hook completes, so the install and the
# warmed NuGet cache carry into later turns.
#
# Idempotent: an SDK that is already present is left alone, and `dotnet restore` is a no-op once
# the cache is warm.

set -euo pipefail

# Only the web containers need this. A developer's own machine already has an SDK, and this
# must not start apt-get on it.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
    exit 0
fi

REQUIRED_MAJOR=8

echo "PMO360 session setup"

# ------------------------------------------------------------------ .NET SDK
if command -v dotnet >/dev/null 2>&1 \
   && dotnet --list-sdks 2>/dev/null | grep -q "^${REQUIRED_MAJOR}\."; then
    echo "  .NET ${REQUIRED_MAJOR} SDK already present: $(dotnet --version)"
else
    echo "  installing the .NET ${REQUIRED_MAJOR} SDK…"

    # Ubuntu's own package, not the dotnet-install script: this environment's network policy
    # blocks builds.dotnet.microsoft.com, so the official installer cannot reach its downloads.
    #
    # The update is required, not optional — without it the archive index is stale and the
    # dotnet8 .debs 404. Third-party PPAs in the image can fail to refresh behind the proxy, and
    # that is survivable, so a non-zero exit here is tolerated and the install below is what
    # actually decides.
    apt-get update -qq || echo "  (apt-get update reported problems; continuing)"

    if ! DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-8.0; then
        echo "  ERROR: the .NET ${REQUIRED_MAJOR} SDK could not be installed." >&2
        echo "  Without it, dotnet build and dotnet test will not run in this session." >&2
        exit 1
    fi

    echo "  installed: $(dotnet --version)"
fi

# Keep the build output to the point, and do not send telemetry from a throwaway container.
{
    echo 'export DOTNET_NOLOGO=1'
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1'
} >> "${CLAUDE_ENV_FILE:-/dev/null}"

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# ------------------------------------------------------------------ packages
# Restoring now means the first build of the session is a build, not a download.
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$(dirname "$(dirname "$(readlink -f "$0")")")")}"

echo "  restoring NuGet packages…"
if ! dotnet restore PMO360.sln --nologo --verbosity quiet; then
    echo "  ERROR: dotnet restore failed. Check that nuget.org is reachable." >&2
    exit 1
fi

echo "  ready: dotnet build / dotnet test"
