#!/usr/bin/env bash
# Preparação única da VPS para o deploy automático. Executar como root a partir de um clone do repo:
#   sudo bash deploy/scripts/bootstrap-vps.sh ghcr.io/<owner-em-minúsculas>
#
# Cria:
#   - usuário "deploy" (sem senha), que só pode rodar /opt/vps-control/bin/deploy.sh com sudo;
#   - /opt/vps-control (compose, .env, releases) e /etc/vps-control/deploy.env;
#   - .env com senha inicial do admin gerada aleatoriamente.
# Idempotente: pode ser executado de novo para atualizar o deploy.sh.
set -euo pipefail

PREFIX="${1:?uso: bootstrap-vps.sh ghcr.io/<owner>}"
[[ "$PREFIX" =~ ^[a-z0-9./:_-]+$ ]] || { echo "Prefixo inválido (use minúsculas): $PREFIX" >&2; exit 1; }
[[ $EUID -eq 0 ]] || { echo "Execute como root." >&2; exit 1; }

HERE="$(cd "$(dirname "$0")" && pwd)"
APP_DIR=/opt/vps-control

command -v docker >/dev/null || { echo "Docker não instalado." >&2; exit 1; }
docker compose version >/dev/null || { echo "Plugin docker compose ausente." >&2; exit 1; }
command -v tailscale >/dev/null || { echo "Tailscale não instalado." >&2; exit 1; }

# Usuário de deploy: entra via Tailscale SSH; não tem senha nem acesso ao Docker.
if ! id deploy >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash deploy
  passwd -l deploy >/dev/null
  echo "Usuário 'deploy' criado."
fi

install -d -m 0755 "$APP_DIR" "$APP_DIR/bin" "$APP_DIR/releases"
install -o root -g root -m 0755 "$HERE/deploy.sh" "$APP_DIR/bin/deploy.sh"

SUDOERS=/etc/sudoers.d/vpsctl-deploy
TMP="$(mktemp)"
cat > "$TMP" <<'EOF'
# Gerado por bootstrap-vps.sh: o usuário deploy só pode executar o script de deploy.
deploy ALL=(root) NOPASSWD: /opt/vps-control/bin/deploy.sh
EOF
visudo -cf "$TMP" >/dev/null
install -m 0440 "$TMP" "$SUDOERS"
rm -f "$TMP"

install -d -m 0755 /etc/vps-control
printf 'IMAGE_PREFIX=%s\n' "$PREFIX" > /etc/vps-control/deploy.env
chmod 0644 /etc/vps-control/deploy.env

if [[ ! -f "$APP_DIR/.env" ]]; then
  ADMIN_PASSWORD="$(head -c 24 /dev/urandom | base64 | tr -d '\n/+=' | head -c 24)"
  umask 077
  cat > "$APP_DIR/.env" <<EOF
# Gerado por bootstrap-vps.sh. VPSCTL_API_IMAGE e VPSCTL_GID são mantidos pelo deploy.sh.
VPSCTL_ADMIN_PASSWORD=$ADMIN_PASSWORD
VPSCTL_TAILSCALE_USER=
EOF
  umask 022
  echo
  echo "================================================================"
  echo " Senha inicial do admin (usuário: admin):"
  echo "   $ADMIN_PASSWORD"
  echo " Guarde agora. Depois do primeiro login, remova a linha"
  echo " VPSCTL_ADMIN_PASSWORD de $APP_DIR/.env."
  echo "================================================================"
fi

echo
echo "Bootstrap concluído."
echo "Próximos passos:"
echo "  1. (repo privado) sudo docker login ghcr.io -u <usuário>   # token clássico com read:packages"
echo "  2. Disparar o workflow Deploy no GitHub (ou: sudo $APP_DIR/bin/deploy.sh <sha>)"
echo "  3. sudo tailscale serve --bg --https=443 http://127.0.0.1:8080"
