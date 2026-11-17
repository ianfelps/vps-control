# Threat model — VPS Control

Escopo: painel single-tenant que administra uma VPS Ubuntu com Docker, acessado só pela tailnet.
Método: STRIDE simplificado sobre os fluxos de dados. Revisar a cada nova capacidade administrativa.

## Ativos

| Ativo | Por que importa |
|---|---|
| `docker.sock` | Controle total do Docker = root no host |
| Segredos em variáveis de ambiente dos containers | Senhas de banco, tokens de API |
| Sessão do admin | Dá acesso a todas as ações do painel |
| Token do agente (`/etc/vps-control/agent.token`) | Permite falar com o agente |
| Banco SQLite (`/data`) | Hash de senha, auditoria, chaves de sessão |
| Disponibilidade dos serviços hospedados | O painel pode parar containers |

## Fronteiras de confiança

```
Internet ──✗── VPS (nenhuma porta do painel exposta)
Tailnet  ──▶ Tailscale Serve (TLS, identidade) ──▶ API 127.0.0.1:8080   [1]
API (container, não-root) ──▶ agente via Unix socket + token            [2]
Agente (host, systemd endurecido) ──▶ docker.sock, /proc                [3]
Logs/env de containers (dados de terceiros) ──▶ API ──▶ navegador        [4]
```

## Ameaças e mitigações

| # | Ameaça (STRIDE) | Mitigação | Onde |
|---|---|---|---|
| T1 | Acesso pela internet (S) | API publica só em `127.0.0.1`; acesso via `tailscale serve`; validação com `ss -tlnp` no deploy | `compose.yaml`, deploy.md |
| T2 | Membro da tailnet não autorizado (S/E) | ACLs da tailnet + allowlist opcional `Tailscale-User-Login` + login próprio | `SecurityMiddleware` |
| T3 | Força bruta de senha (S) | Hash PBKDF2, rate limit 5/min/IP, falhas auditadas | `AuthEndpoints`, rate limiter |
| T4 | CSRF a partir de outra aba (T) | Cookie `SameSite=Strict` + antiforgery double-submit em toda mutação | `UseApiAntiforgery` |
| T5 | Roubo de sessão via XSS (I/E) | Cookie `HttpOnly`; React escapa saída; logs e env renderizados como texto, nunca HTML | frontend |
| T6 | Comprometimento da API leva a root (E) | API não tem `docker.sock`; container read-only, `cap_drop: ALL`, não-root; agente só aceita RPCs enumerados | arquitetura |
| T7 | Processo local qualquer fala com o agente (S/E) | Socket `0660` grupo `vpsctl` + token ≥ 32 chars comparado em tempo constante | agente |
| T8 | Injeção via id de container (path traversal na Docker API) (T/E) | Regex de id/nome na API e no agente (`GuardedContainerRuntime`); `Uri.EscapeDataString` | Core |
| T9 | Painel para a si mesmo ou serviço crítico (D) | Label `vpsctl.protected=true` recusada no agente (stop/restart) | guard |
| T10 | Ações em massa por script/clique repetido (D) | Confirmação na UI + rate limit 30/min/usuário em ações administrativas | rate limiter |
| T11 | Vazamento de segredos em env (I) | Máscara por padrão no agente; revelar exige permissão `secrets.reveal`, confirmação e é auditado | `EnvMasker` |
| T12 | Flood de logs derruba navegador/API (D) | Tail ≤ 2000, 200 linhas/s no follow, linha ≤ 4 KB, buffer de 2000 linhas no cliente | guard, `useLogStream` |
| T13 | Repúdio de ações (R) | Auditoria append-only (DbContext recusa UPDATE/DELETE), inclui negadas e falhas | `AuditLog` |
| T14 | Agente comprometido (E) | Unit com `NoNewPrivileges`, `ProtectSystem=strict`, sem capabilities, `AF_UNIX` apenas, `IPAddressDeny=any`, filtro de syscalls | `vpsctl-agent.service` |
| T15 | Perda do banco (D) | Backup `VACUUM INTO` agendado; restore documentado | `backup-db.sh` |

## Riscos aceitos

- **Grupo `docker` no agente** equivale a root. Aceito porque é a única forma de operar o Docker;
  mitigado por T6–T8 e T14. Alternativa futura: rootless Docker ou socket-proxy com verbos fixos.
- **Usuário único com todas as permissões.** As permissões já são verificadas por operação
  (`containers.manage`, `secrets.reveal`, `applications.edit`), mas só existe o admin.
- **Chaves de sessão em disco sem criptografia adicional** (`/data/keys`). Protegidas pelo volume
  privado do container; não há DPAPI no Linux.
- **Header `Tailscale-User-Login`** é confiável porque a API só escuta em loopback; se a API for
  exposta de outra forma, a allowlist pode ser forjada.

## Fora de escopo

Comprometimento do host por outros serviços, da conta Tailscale do admin ou do próprio Docker
Engine; ataques físicos; terminal web (futuro, exigirá revisão deste documento).
