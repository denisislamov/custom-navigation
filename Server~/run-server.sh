#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -z "${CUSTOM_NAVIGATION_JITTER_ROOT:-}" ]]; then
  echo "CUSTOM_NAVIGATION_JITTER_ROOT must point to the extracted approved canonical Jitter release." >&2
  exit 2
fi

exec dotnet run --project "$script_dir/DotRecastServer.csproj" --configuration Release \
  -p:CanonicalJitterRoot="$CUSTOM_NAVIGATION_JITTER_ROOT" -- "$@"
