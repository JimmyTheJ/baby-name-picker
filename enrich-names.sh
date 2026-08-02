#!/usr/bin/env bash
# Run LLM name enrichment inside the baby-name-picker Docker container.
# Requires: container running (docker compose up -d)
#
# Usage:
#   ./enrich-names.sh
#   ./enrich-names.sh --batch 50
#   ./enrich-names.sh --batch 50 --force
#   ./enrich-names.sh --provider OpenAI --batch 25
#   ./enrich-names.sh --all --batch 50    # repeat until no names left

set -euo pipefail
cd "$(dirname "$0")"

ALL=false
ARGS=()
for arg in "$@"; do
  if [[ "$arg" == "--all" ]]; then
    ALL=true
  else
    ARGS+=("$arg")
  fi
done

if ! docker compose ps --status running --services 2>/dev/null | grep -Fxq "baby-name-picker"; then
  echo "baby-name-picker is not running. Start it with: docker compose up -d" >&2
  exit 1
fi

run_batch() {
  docker compose exec -T baby-name-picker dotnet BabyNamePicker.dll enrich-names "${ARGS[@]}"
}

if [[ "$ALL" == true ]]; then
  while true; do
    output="$(run_batch)"
    printf '%s\n' "$output"
    if printf '%s\n' "$output" | grep -qE 'Enrichment complete: 0/0 '; then
      echo "No more names to enrich."
      break
    fi
  done
else
  run_batch
fi
