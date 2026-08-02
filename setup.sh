#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Created .env from .env.example"
fi

load_env_var() {
  local key="$1"
  if [[ -n "${!key:-}" ]]; then
    return
  fi
  if [[ ! -f .env ]]; then
    return
  fi
  local line
  line="$(grep -E "^[[:space:]]*${key}[[:space:]]*=" .env | tail -n 1 || true)"
  if [[ -z "$line" ]]; then
    return
  fi
  local value="${line#*=}"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  value="${value#\"}"
  value="${value%\"}"
  value="${value#\'}"
  value="${value%\'}"
  export "$key=$value"
}

load_env_var DOCKER_SHARED_NETWORK

if [[ -z "${DOCKER_SHARED_NETWORK:-}" ]]; then
  echo "DOCKER_SHARED_NETWORK is not set. Add it to .env (see .env.example)." >&2
  exit 1
fi

if docker network ls --format '{{.Name}}' | grep -Fxq "$DOCKER_SHARED_NETWORK"; then
  echo "Docker network '$DOCKER_SHARED_NETWORK' already exists"
else
  docker network create "$DOCKER_SHARED_NETWORK"
  echo "Created Docker network '$DOCKER_SHARED_NETWORK'"
fi

echo "Setup complete. Run: docker compose up --build"
