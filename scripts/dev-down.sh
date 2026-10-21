#!/usr/bin/env bash
# Stops the local dev stack and removes its data.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose -f dev/docker-compose.yml down -v
