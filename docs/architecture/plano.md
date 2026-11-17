# VPS Control — Plano de implementação (revisão .NET)

## 1. Visão geral

Aplicação web self-hosted para monitorar e administrar uma VPS Ubuntu Server, com foco em
aplicações Docker. Acesso exclusivo pela tailnet (Tailscale Serve).

Objetivos: centralizar monitoramento (CPU, RAM, disco, rede), monitorar containers, consultar
logs, executar ações administrativas autorizadas e auditadas, sem comprometer o host.

## 2. Stack

| Camada | Tecnologia |
|---|---|
| Frontend | React 19, TypeScript, Vite, Tailwind CSS v4, componentes estilo shadcn/ui |
| Gráficos | Recharts |
| Estado/consultas | TanStack Query |
| Contratos front | Tipos TS gerados do OpenAPI da API (`openapi-typescript`) |
| Backend | ASP.NET Core 10 (LTS), Minimal APIs |
| Tempo real | SignalR |
| Persistência | SQLite + EF Core (migrations) |
| Agente | .NET 10 Worker Service, Native AOT, systemd |
| API ↔ Agente | gRPC sobre Unix domain socket |
| Docker | Docker.DotNet (somente no agente) |
| Métricas Linux | leitura direta de `/proc` e `/sys` |
| Logs | Serilog (JSON estruturado) |
| Testes | xUnit, Vitest, Playwright |
| Infra | Docker Compose (API), systemd (agente), Tailscale Serve |
| CI | GitHub Actions |

## 3. Arquitetura

```
[Navegador] ──tailnet──▶ [Tailscale Serve :443] ──▶ 127.0.0.1:8080
                                                     │
                                       ┌─────────────▼──────────────┐
                                       │ VpsControl.Api (container) │
                                       │ - serve SPA React          │
                                       │ - auth, auditoria, SQLite  │
                                       │ - SignalR                  │
                                       └─────────────┬──────────────┘
                                   gRPC / UDS /run/vps-control/agent.sock
                                       ┌─────────────▼──────────────┐
                                       │ VpsControl.Agent (systemd) │
                                       │ - /proc, /sys              │
                                       │ - docker.sock              │
                                       └────────────────────────────┘
```

Regras invariáveis:

1. **Somente o agente acessa `docker.sock`.** A API nunca o monta.
2. O agente expõe apenas operações tipadas e enumeradas. Sem execução arbitrária de comandos.
3. Socket do agente: `root:vpsctl 0660`. A API entra via `group_add`. Token compartilhado
   (arquivo `0600`) em metadata gRPC.
4. Containers com label `vpsctl.protected=true` não podem ser parados/reiniciados — bloqueio no agente.
5. A API serve o build do React (same-origin, sem CORS, cookie `SameSite=Strict`).
6. Abstrações `IHostMetricsProvider` / `IContainerRuntime` com implementação **fake** para
   desenvolvimento em Windows/macOS.

## 4. Segurança

- Cookie `HttpOnly`, `Secure`, `SameSite=Strict`; antiforgery em mutações.
- Senha do admin armazenada como hash (PBKDF2 do ASP.NET Identity `PasswordHasher`).
- Allowlist opcional de usuários Tailscale via header `Tailscale-User-Login`.
- Rate limiting no login e nas ações administrativas.
- Variáveis de ambiente de containers mascaradas por padrão (`*_KEY`, `*_SECRET`,
  `*_PASSWORD`, `*_TOKEN`, ...). Revelar = ação auditada.
- Logs com tail limitado e taxa máxima no streaming.
- Auditoria append-only: quem, ação, alvo, resultado, IP, timestamp UTC.
- Unit systemd endurecida (`ProtectSystem=strict`, `NoNewPrivileges`, `RestrictAddressFamilies=AF_UNIX`).

## 5. Repositório

```
vps-control/
├── apps/web/                    # React + Vite
├── src/
│   ├── VpsControl.Api/
│   ├── VpsControl.Agent/
│   ├── VpsControl.Contracts/    # .proto + DTOs
│   └── VpsControl.Core/         # domínio, abstrações, fakes
├── tests/
├── deploy/{docker,systemd,scripts}
├── docs/
├── VpsControl.slnx
├── Directory.Build.props / Directory.Packages.props / global.json
├── pnpm-workspace.yaml
└── compose.yaml
```

## 6. Fases

### Fase 1 — Fundação + walking skeleton
- Monorepo, solution .NET, app React, lint/format, testes, CI.
- Healthcheck, layout/rotas, login com cookie, provedores fake.
- **Deploy do esqueleto na VPS** (Tailscale Serve + socket do agente) para validar rede e permissões cedo.
- Threat model curto em `docs/architecture/`.

### Fase 2 — Monitoramento do Ubuntu
- Agente lendo `/proc`/`/sys`: CPU, RAM, swap, disco, rede, uptime, kernel, hostname.
- gRPC sobre UDS; API agrega e publica via SignalR.
- Ring buffer em memória (~1 h) para gráficos.
- Estado "agente indisponível" na UI.

### Fase 3 — Docker (leitura) + agrupamento por Compose
- Docker.DotNet no agente: listar, inspecionar (com máscara), stats, logs (tail + stream).
- Agrupamento por label `com.docker.compose.project`.
- Páginas de containers, detalhes, aplicações; filtros e busca.

### Fase 4 — Gerenciamento Docker
- start/stop/restart com confirmação, autorização por operação, rate limit, auditoria.
- Autoproteção (`vpsctl.protected`).
- Testes de validação de IDs e permissões.

### Fase 5 — Metadados de aplicações
- Cadastro de nome, descrição, URL em SQLite; métricas agregadas.

### Fase 6 — Produção e endurecimento
- Imagens finais, volumes, restart policies, bind em 127.0.0.1.
- ACLs da tailnet, backup SQLite (`VACUUM INTO` agendado), restore testado.
- Documentação de instalação, atualização e recuperação.

## 7. Critérios do MVP (mensuráveis)

1. Porta do painel ausente em `ss -tlnp` fora de `127.0.0.1`; acesso só via tailnet.
2. Dashboard com métricas reais atualizadas a cada ≤ 2 s.
3. Containers listados com stats atualizados; logs consultáveis e em streaming.
4. start/stop/restart funcionam; containers protegidos são recusados.
5. Toda ação administrativa autenticada e registrada na auditoria.
6. Usuário fora da allowlist recebe 403 (teste automatizado).
7. Agente em idle: < 1% CPU, < 50 MB RAM.
8. Configurações persistem após reinício; restore de backup testado.

## 8. Futuro

Histórico com retenção, alertas, monitoramento HTTP, notificações, imagens/volumes/redes,
backups de apps, terminal web, múltiplas VPS, widgets, exportação.
