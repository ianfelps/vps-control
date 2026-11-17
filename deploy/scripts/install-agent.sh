#!/usr/bin/env bash
# Instala/atualiza o agente na VPS. Executar como root a partir da raiz do repositório:
#   sudo deploy/scripts/install-agent.sh artifacts/agent-x64
set -euo pipefail

SRC="${1:?uso: install-agent.sh <diretório com vpsctl-agent>}"
INSTALL_DIR=/opt/vps-control/agent
CONF_DIR=/etc/vps-control
TOKEN_FILE="$CONF_DIR/agent.token"
UNIT=/etc/systemd/system/vpsctl-agent.service

if [[ $EUID -ne 0 ]]; then
  echo "Execute como root." >&2
  exit 1
fi

# Grupo compartilhado: o container da API entra nele (group_add) para usar o socket e ler o token.
getent group vpsctl >/dev/null || groupadd --system vpsctl
id vpsctl-agent >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin \
  --gid vpsctl vpsctl-agent

install -d -m 0755 "$INSTALL_DIR"
install -m 0755 "$SRC/vpsctl-agent" "$INSTALL_DIR/vpsctl-agent"
install -m 0644 "$SRC/appsettings.json" "$INSTALL_DIR/appsettings.json"

install -d -m 0750 -o root -g vpsctl "$CONF_DIR"
if [[ ! -s "$TOKEN_FILE" ]]; then
  umask 027
  head -c 48 /dev/urandom | base64 | tr -d '\n/+=' > "$TOKEN_FILE"
  echo "Token do agente gerado em $TOKEN_FILE"
fi
chown root:vpsctl "$TOKEN_FILE"
chmod 0640 "$TOKEN_FILE"

install -m 0644 "$(dirname "$0")/../systemd/vpsctl-agent.service" "$UNIT"
systemctl daemon-reload
systemctl enable vpsctl-agent >/dev/null
systemctl restart vpsctl-agent

# Espera o socket aparecer (até 20 s); falha de verdade para o deploy poder fazer rollback.
for _ in $(seq 1 40); do
  if [[ -S /run/vps-control/agent.sock ]] && systemctl is-active --quiet vpsctl-agent; then
    echo "Agente ativo em /run/vps-control/agent.sock"
    echo "VPSCTL_GID=$(getent group vpsctl | cut -d: -f3)"
    exit 0
  fi
  sleep 0.5
done

systemctl --no-pager --lines=20 status vpsctl-agent >&2 || true
echo "Agente não ficou ativo." >&2
exit 1
