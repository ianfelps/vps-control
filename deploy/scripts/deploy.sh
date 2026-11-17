#!/usr/bin/env bash
# Deploy de uma versão (SHA do commit) na VPS. Executado como root, via:
#   sudo /opt/vps-control/bin/deploy.sh <sha40>
# É o ÚNICO comando que o usuário "deploy" pode rodar com sudo (ver bootstrap-vps.sh).
#
# Passos: lock → pull das imagens fixadas pelo SHA → backup do banco → agente (se mudou)
# → API (compose up --wait) → healthcheck → registra versão. Qualquer falha após o backup
# reativa a versão anterior automaticamente.
set -Eeuo pipefail

VERSION="${1:-}"
APP_DIR=/opt/vps-control
CONF=/etc/vps-control/deploy.env
RELEASES="$APP_DIR/releases"
CURRENT_FILE="$APP_DIR/current"
KEEP_RELEASES=5

log() { printf '[deploy %s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { log "ERRO: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "execute como root (sudo)."
[[ "$VERSION" =~ ^[0-9a-f]{40}$ ]] || die "versão inválida: use o SHA completo do commit (40 hex)."
[[ -r "$CONF" ]] || die "$CONF ausente; rode bootstrap-vps.sh."
# shellcheck source=/dev/null
source "$CONF"
[[ "${IMAGE_PREFIX:-}" =~ ^[a-z0-9./:_-]+$ ]] || die "IMAGE_PREFIX inválido em $CONF."
[[ -f "$APP_DIR/.env" ]] || die "$APP_DIR/.env ausente; rode bootstrap-vps.sh."

exec 9>/run/vps-control-deploy.lock
flock -n 9 || die "outro deploy em andamento."

PREVIOUS="$(cat "$CURRENT_FILE" 2>/dev/null || true)"
compose() { docker compose --project-name vps-control --project-directory "$APP_DIR" -f "$APP_DIR/compose.yaml" "$@"; }

set_env() { # set_env CHAVE VALOR — atualiza ou acrescenta no .env
  local key="$1" value="$2" file="$APP_DIR/.env"
  if grep -q "^${key}=" "$file"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$file"
  else
    printf '%s=%s\n' "$key" "$value" >> "$file"
  fi
}

fetch_release() { # baixa imagens e extrai o bundle em releases/<versão>
  local version="$1" dir="$RELEASES/$1"
  docker pull --quiet "$IMAGE_PREFIX/vps-control-api:$version" >/dev/null
  if [[ ! -x "$dir/agent/vpsctl-agent" ]]; then
    docker pull --quiet "$IMAGE_PREFIX/vps-control-bundle:$version" >/dev/null
    local tmp cid
    tmp="$(mktemp -d)"
    cid="$(docker create "$IMAGE_PREFIX/vps-control-bundle:$version" /none)"
    docker cp "$cid:/bundle/." "$tmp/" && docker rm "$cid" >/dev/null
    install -d -m 0755 "$RELEASES"
    rm -rf "$dir" && mv "$tmp" "$dir" && chmod 0755 "$dir"
  fi
}

# ativa uma versão já baixada: agente (se mudou) + API.
# Cada passo tem "|| return 1": a função é chamada em contexto condicional (rollback), onde o
# bash ignora o set -e.
activate() {
  local version="$1" dir="$RELEASES/$1"
  [[ -x "$dir/agent/vpsctl-agent" ]] || { log "release $version incompleta"; return 1; }

  local new_sum cur_sum=""
  new_sum="$(sha256sum "$dir/agent/vpsctl-agent" | cut -d' ' -f1)" || return 1
  if [[ -f "$APP_DIR/agent/vpsctl-agent" ]]; then
    cur_sum="$(sha256sum "$APP_DIR/agent/vpsctl-agent" | cut -d' ' -f1)"
  fi
  if [[ "$new_sum" != "$cur_sum" ]] || ! systemctl is-active --quiet vpsctl-agent || [[ ! -S /run/vps-control/agent.sock ]]; then
    log "atualizando agente"
    # 9>&-: processos iniciados aqui não herdam o lock do deploy.
    bash "$dir/scripts/install-agent.sh" "$dir/agent" 9>&- || { log "agente não subiu"; return 1; }
  else
    log "agente inalterado"
  fi

  install -m 0644 "$dir/compose.yaml" "$APP_DIR/compose.yaml" || return 1
  install -m 0755 "$dir/scripts/backup-db.sh" "$APP_DIR/bin/backup-db.sh" || return 1
  set_env VPSCTL_API_IMAGE "$IMAGE_PREFIX/vps-control-api:$version" || return 1
  set_env VPSCTL_GID "$(getent group vpsctl | cut -d: -f3)" || return 1

  log "subindo API $version"
  compose up -d --no-build --remove-orphans --wait --wait-timeout 120 9>&- || { log "container não ficou saudável"; return 1; }

  local ok=""
  for _ in $(seq 1 30); do
    if curl -fsS --max-time 3 http://127.0.0.1:8080/api/health >/dev/null; then ok=1; break; fi
    sleep 1
  done
  [[ -n "$ok" ]] || { log "healthcheck falhou"; return 1; }

  # /api/health não depende do agente: confirma que ele segue ativo.
  if [[ ! -S /run/vps-control/agent.sock ]] || ! systemctl is-active --quiet vpsctl-agent; then
    log "agente inativo"
    return 1
  fi
}

rollback() {
  if [[ -n "$PREVIOUS" && "$PREVIOUS" != "$VERSION" && -d "$RELEASES/$PREVIOUS" ]]; then
    log "ROLLBACK para $PREVIOUS"
    if activate "$PREVIOUS"; then
      log "rollback concluído; versão $VERSION NÃO foi implantada."
    else
      log "rollback também falhou; verifique 'docker compose ps' e 'systemctl status vpsctl-agent'."
    fi
  else
    log "sem versão anterior para rollback."
  fi
  exit 1
}

log "versão $VERSION (anterior: ${PREVIOUS:-nenhuma})"
fetch_release "$VERSION"
install -d -m 0755 "$APP_DIR/bin"

# Backup antes de qualquer migration (a API aplica migrations no start).
if docker volume inspect vps-control_data >/dev/null 2>&1; then
  log "backup do banco"
  bash "$RELEASES/$VERSION/scripts/backup-db.sh" /var/backups/vps-control >/dev/null
fi

if ! activate "$VERSION"; then
  rollback
fi

echo "$VERSION" > "$CURRENT_FILE"
log "OK: $VERSION ativa"

# Limpeza: mantém as últimas releases (inclui a atual e a anterior para rollback manual).
mapfile -t old < <(ls -1t "$RELEASES" | tail -n +$((KEEP_RELEASES + 1)))
for r in "${old[@]}"; do
  [[ "$r" == "$VERSION" || "$r" == "$PREVIOUS" ]] && continue
  rm -rf "${RELEASES:?}/$r"
  docker image rm "$IMAGE_PREFIX/vps-control-api:$r" "$IMAGE_PREFIX/vps-control-bundle:$r" >/dev/null 2>&1 || true
done
