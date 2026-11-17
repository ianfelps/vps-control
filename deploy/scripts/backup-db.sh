#!/usr/bin/env bash
# Backup consistente do SQLite (VACUUM INTO) a partir do volume do compose.
# Agendar via cron/systemd timer, ex.: 0 3 * * * /opt/vps-control/repo/deploy/scripts/backup-db.sh
set -euo pipefail

DEST="${1:-/var/backups/vps-control}"
VOLUME="${VPSCTL_VOLUME:-vps-control_data}"
KEEP_DAYS="${KEEP_DAYS:-14}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

mkdir -p "$DEST"
chmod 0700 "$DEST"

# Container efêmero com sqlite3; o volume é montado somente leitura exceto o destino.
docker run --rm \
  -v "$VOLUME:/data:ro" \
  -v "$DEST:/backup" \
  alpine:3.22 sh -c "apk add --no-cache sqlite >/dev/null && sqlite3 /data/vpscontrol.db \"VACUUM INTO '/backup/vpscontrol-$STAMP.db'\""

find "$DEST" -name 'vpscontrol-*.db' -mtime "+$KEEP_DAYS" -delete
echo "Backup: $DEST/vpscontrol-$STAMP.db"
