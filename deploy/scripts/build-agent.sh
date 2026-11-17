#!/usr/bin/env bash
# Compila o agente (Native AOT, linux-x64 ou linux-arm64) via Docker.
# Uso: deploy/scripts/build-agent.sh [x64|arm64]
set -euo pipefail

ARCH="${1:-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/artifacts/agent-$ARCH"
rm -rf "$OUT"   # --output local não remove arquivos de builds anteriores

docker build \
  --file "$ROOT/deploy/docker/agent-build.Dockerfile" \
  --build-arg "ARCH=$ARCH" \
  --output "type=local,dest=$OUT" \
  "$ROOT"

echo "Agente gerado em $OUT"
ls -lh "$OUT"
