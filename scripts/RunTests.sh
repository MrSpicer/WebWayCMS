#!/usr/bin/env bash
# Runs every test project in the solution. Each project enforces 100% line+branch
# coverage on its own assembly via coverlet; the run fails if any project falls short.
#
# Usage:
#   ./Scripts/RunTests.sh            # run all test projects
#   ./Scripts/RunTests.sh --report   # also emit a merged HTML coverage report (needs reportgenerator)
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet test WebWayCMS.sln "$@"
