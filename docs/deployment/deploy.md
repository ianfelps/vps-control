# Deploy na VPS (Ubuntu)

Pré-requisitos na VPS: Docker Engine + Compose plugin, Tailscale autenticado na tailnet.

## 1. Agente (host, systemd)

Compile em qualquer máquina com Docker (inclusive Windows/WSL):

```bash
deploy/scripts/build-agent.sh x64      # ou arm64
```

Copie `artifacts/agent-x64/` e o repositório para a VPS e instale:

```bash
sudo deploy/scripts/install-agent.sh artifacts/agent-x64
```

O script:

- cria o grupo `vpsctl` e o usuário de sistema `vpsctl-agent`;
- instala o binário em `/opt/vps-control/agent`;
- gera `/etc/vps-control/agent.token` (`root:vpsctl 0640`);
- instala e inicia `vpsctl-agent.service` (socket em `/run/vps-control/agent.sock`, `0660`);
- imprime o `VPSCTL_GID` para o `.env`.

Verifique:

```bash
systemctl status vpsctl-agent
ls -l /run/vps-control/
```

## 2. API (Docker Compose)

```bash
cp .env.example .env
# preencha VPSCTL_GID, VPSCTL_ADMIN_PASSWORD (>= 12 caracteres) e, opcionalmente, VPSCTL_TAILSCALE_USER
docker compose up -d --build
docker compose ps
curl -s http://127.0.0.1:8080/api/health
```

Depois do primeiro login, remova `VPSCTL_ADMIN_PASSWORD` do `.env` (o hash fica no SQLite).

## 3. Tailscale Serve

```bash
sudo tailscale serve --bg --https=443 http://127.0.0.1:8080
tailscale serve status
```

Acesse `https://<host>.<tailnet>.ts.net`.

O Serve termina o TLS e repassa HTTP puro. Por isso a API trata toda requisição como HTTPS
(`Proxy:AssumeHttps=true`, padrão em produção); sem isso, cookies `Secure` e o antiforgery
recusariam o login. Só desligue se expuser a API de outra forma (não recomendado). Restrinja quem alcança a VPS nas ACLs da tailnet e,
para defesa em profundidade, defina `VPSCTL_TAILSCALE_USER` (a API valida o header
`Tailscale-User-Login` injetado pelo Serve).

## 4. Validação de exposição

```bash
sudo ss -tlnp | grep -E ':8080|vpsctl'     # deve mostrar só 127.0.0.1:8080
curl -m 3 http://<ip-publico>:8080/        # deve falhar
```

## 5. Backup e restauração

```bash
sudo deploy/scripts/backup-db.sh /var/backups/vps-control
```

Agende diariamente (cron ou systemd timer). Para restaurar:

```bash
docker compose stop api
docker run --rm -v vps-control_data:/data -v /var/backups/vps-control:/backup alpine:3.22 \
  cp /backup/vpscontrol-<STAMP>.db /data/vpscontrol.db
docker compose start api
```

Teste a restauração ao menos uma vez (critério do MVP).

## 6. Atualização

```bash
git pull
docker compose up -d --build        # API; migrations aplicam no start
deploy/scripts/build-agent.sh x64 && sudo deploy/scripts/install-agent.sh artifacts/agent-x64
```

O container da API tem a label `vpsctl.protected=true`: o painel nunca o para por engano.

## Solução de problemas

| Sintoma | Causa provável |
|---|---|
| Dashboard mostra "Fonte de métricas indisponível" | Agente parado, ou `VPSCTL_GID` errado (sem permissão no socket) |
| `Permission denied` no token | Token sem `root:vpsctl 0640` |
| 403 em tudo | `VPSCTL_TAILSCALE_USER` diferente do login Tailscale real |
| Login não persiste | Acesso por `http://<ip>` — cookies são `Secure`; use a URL `https://` do Serve |
| "Agente recusou o token" | Token do container diferente de `/etc/vps-control/agent.token` |

Sessões e tokens antiforgery usam chaves em `/data/keys` (volume `data`): sobrevivem a restart e
atualização do container. Apagar o volume desloga todos.
