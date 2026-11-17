# VPS Control

Painel web self-hosted para monitorar e administrar uma VPS Ubuntu Server, com foco em
aplicações Docker. Acessível **somente pela sua tailnet** (Tailscale Serve), sem nenhuma porta
pública.

![status](https://img.shields.io/badge/fase-1%20conclu%C3%ADda-blue)
![.NET](https://img.shields.io/badge/.NET-10%20LTS-512BD4)
![React](https://img.shields.io/badge/React-19-61DAFB)

---

## Sumário

- [Funcionalidades](#funcionalidades)
- [Arquitetura](#arquitetura)
- [Stack](#stack)
- [Modelo de segurança](#modelo-de-segurança)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Desenvolvimento local](#desenvolvimento-local)
- [Comandos](#comandos)
- [Configuração](#configuração)
- [API](#api)
- [Testes e qualidade](#testes-e-qualidade)
- [Deploy na VPS](#deploy-na-vps)
- [Solução de problemas](#solução-de-problemas)
- [Roadmap](#roadmap)
- [Convenções](#convenções)

---

## Funcionalidades

### Disponível (Fase 1 + coleta da Fase 2)

- **Dashboard em tempo real** (SignalR, amostra a cada 2 s):
  - CPU (uso %, load average 1/5/15, núcleos)
  - Memória e swap
  - Uso por disco (sistemas de arquivos físicos, sem duplicar bind mounts)
  - Tráfego por interface de rede (ignora `lo` e `veth*` dos containers)
  - Hostname, distribuição, kernel e uptime
  - Gráficos de CPU/memória e rede com janelas de 5 min, 15 min e 1 h
- **Histórico em memória** de 1 h (ring buffer), recarregado ao reconectar.
- **Aviso de fonte indisponível** com a última amostra preservada, e recuperação automática
  quando o agente volta.
- **Login** com sessão por cookie, logout e expiração deslizante (8 h por padrão).
- **Auditoria append-only** de logins (sucesso e falha) e logouts, com tela de consulta.
- **Interface responsiva** (desktop e mobile) com tema claro/escuro seguindo o sistema.

### Planejado

| Fase | Entrega |
|---|---|
| 2 | Deploy do agente real na VPS e validação do hardening systemd |
| 3 | Containers: lista, status, healthcheck, stats, logs (tail + stream), agrupamento por projeto Compose |
| 4 | start/stop/restart com confirmação, autorização por operação e auditoria |
| 5 | Metadados de aplicações (descrição, URL) e métricas agregadas |
| 6 | Endurecimento final, ACLs da tailnet, backup/restore testado |

Detalhes em [docs/architecture/plano.md](docs/architecture/plano.md).

---

## Arquitetura

```
 Navegador (membro da tailnet)
        │  HTTPS
        ▼
 ┌──────────────────────┐
 │ Tailscale Serve :443 │  termina TLS, injeta Tailscale-User-Login
 └──────────┬───────────┘
            │  HTTP 127.0.0.1:8080
            ▼
 ┌────────────────────────────────────┐   container Docker
 │ VpsControl.Api                     │   (não-root, read-only, cap_drop ALL)
 │  • serve a SPA React               │
 │  • auth, antiforgery, rate limit   │
 │  • auditoria + SQLite (/data)      │
 │  • SignalR /hubs/metrics           │
 └──────────┬─────────────────────────┘
            │  gRPC sobre Unix socket /run/vps-control/agent.sock
            │  (root:vpsctl 0660 + token compartilhado)
            ▼
 ┌────────────────────────────────────┐   serviço systemd no host
 │ VpsControl.Agent (Native AOT)      │   (usuário vpsctl-agent, hardening)
 │  • lê /proc e /sys                 │
 │  • (fase 3) único com docker.sock  │
 └────────────────────────────────────┘
```

**Por que um agente separado?** A API roda em container sem privilégios. Tudo que exige acesso
ao host (métricas do SO, e depois o `docker.sock`, que equivale a root) fica num processo
pequeno, compilado AOT, que expõe só operações tipadas definidas em
[agent.proto](src/VpsControl.Contracts/Protos/agent.proto) e não fala com a rede.

**Fontes de métricas.** A API escolhe a fonte por configuração (`Metrics:Source`):

| Fonte | Uso |
|---|---|
| `Agent` | Produção: chama o agente via gRPC (Unix socket ou TCP em dev) |
| `Fake` | Desenvolvimento e testes: dados sintéticos, funciona em Windows/macOS |
| `ProcFs` | API rodando direto num host Linux, sem agente |

---

## Stack

| Camada | Tecnologia |
|---|---|
| Frontend | React 19, TypeScript 5.9, Vite 8 |
| UI | Tailwind CSS v4, componentes no estilo shadcn/ui, lucide-react |
| Gráficos | Recharts 3 |
| Dados | TanStack Query 5, `openapi-fetch` com tipos gerados do OpenAPI |
| Tempo real | SignalR (`@microsoft/signalr`) |
| Backend | ASP.NET Core 10 (LTS), Minimal APIs |
| Persistência | SQLite + EF Core 10 (migrations) |
| Logs | Serilog (texto em dev, JSON compacto em produção) |
| Agente | .NET 10 Worker/Kestrel, Native AOT, `Microsoft.Extensions.Hosting.Systemd` |
| Contrato API ↔ agente | gRPC (protobuf) sobre Unix domain socket |
| Testes | xUnit + `WebApplicationFactory`, Vitest |
| Infra | Docker Compose, systemd, Tailscale Serve, GitHub Actions |

---

## Modelo de segurança

Regras que o código impõe (não só convenções):

1. **Sem exposição pública.** A API publica apenas `127.0.0.1:8080`; o acesso externo é via
   `tailscale serve`.
2. **Somente o agente terá acesso ao `docker.sock`.** A API nunca o monta.
3. **Sem execução arbitrária de comandos.** O agente expõe apenas RPCs enumerados no `.proto`.
4. **Canal API ↔ agente autenticado:** socket `0660` restrito ao grupo `vpsctl` + token de
   ≥ 32 caracteres comparado em tempo constante.
5. **Sessão:** cookie `HttpOnly`, `Secure`, `SameSite=Strict`; senha com hash PBKDF2
   (`PasswordHasher` do ASP.NET Identity), rehash automático quando necessário.
6. **CSRF:** toda mutação em `/api` exige token antiforgery (double-submit via header
   `X-XSRF-TOKEN`), vinculado à identidade e reemitido no login/logout.
7. **Força bruta:** login limitado por IP (5/min por padrão, responde 429).
8. **Allowlist da tailnet (opcional):** exige `Tailscale-User-Login` numa lista de identidades.
   Como a API só escuta em loopback, o header não pode ser forjado por clientes da tailnet.
9. **Auditoria append-only:** o `DbContext` recusa alterar ou remover registros de auditoria.
10. **Containers protegidos:** a API tem a label `vpsctl.protected=true`; ações de
    stop/restart (fase 4) serão recusadas no agente para esses containers.
11. **Hardening de runtime:** imagem chiseled sem shell, usuário não-root, filesystem
    read-only, `no-new-privileges`, limite de memória; unit systemd com `ProtectSystem=strict`,
    `RestrictAddressFamilies=AF_UNIX`, `IPAddressDeny=any`, filtro de syscalls, sem capabilities.

---

## Estrutura do repositório

```
vps-control/
├── apps/
│   └── web/                      # React + Vite
│       ├── openapi/openapi.json  # gerado no build da API (versionado)
│       └── src/
│           ├── api/              # client tipado, hooks TanStack Query, SignalR
│           ├── components/       # layout, UI base, guarda de rota
│           ├── lib/              # formatação e utilitários (+ testes Vitest)
│           └── pages/            # login, dashboard, auditoria, placeholders
├── src/
│   ├── VpsControl.Api/           # API ASP.NET Core; serve a SPA em produção
│   │   ├── Audit/                # serviço de auditoria
│   │   ├── Data/                 # DbContext, entidades, migrations, seed do admin
│   │   ├── Endpoints/            # auth, system, audit
│   │   ├── Metrics/              # sampler, ring buffer, hub SignalR, cliente do agente
│   │   └── Security/             # allowlist Tailscale, antiforgery
│   ├── VpsControl.Agent/         # agente do host (systemd, Native AOT)
│   ├── VpsControl.Contracts/     # agent.proto + mapeamento proto ↔ domínio
│   └── VpsControl.Core/          # modelos de domínio, provedores /proc e fake
├── tests/
│   ├── VpsControl.Api.Tests/     # integração (auth, CSRF, rate limit, allowlist, métricas)
│   └── VpsControl.Core.Tests/    # parsers do /proc + leitura real em Linux
├── deploy/
│   ├── docker/                   # api.Dockerfile, agent-build.Dockerfile
│   ├── systemd/                  # vpsctl-agent.service
│   └── scripts/                  # build-agent, install-agent, backup-db
├── docs/
│   ├── architecture/plano.md     # plano e decisões
│   └── deployment/deploy.md      # guia de deploy passo a passo
├── .github/workflows/ci.yml
├── compose.yaml
├── .env.example
├── Directory.Build.props         # net10.0, nullable, warnings como erro, analyzers
├── Directory.Packages.props      # versões NuGet centralizadas
├── global.json                   # fixa o SDK .NET
├── dotnet-tools.json             # dotnet-ef
├── package.json / pnpm-workspace.yaml
└── VpsControl.slnx
```

---

## Desenvolvimento local

Funciona em Windows, macOS e Linux. Em dev as métricas vêm do provedor **fake**, então não é
preciso Linux nem o agente.

### Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (versão em `global.json` ou mais nova da mesma banda)
- Node.js 22 e pnpm 10 (`corepack enable`)
- Docker (opcional, para imagens e build do agente)

### Primeira execução

```bash
pnpm install
```

```bash
dotnet tool restore
```

Em dois terminais:

```bash
pnpm dev:api
```

```bash
pnpm dev:web
```

Abra http://localhost:5173 e entre com usuário `admin` e senha `dev-password-change-me`
(definida em `src/VpsControl.Api/appsettings.Development.json`).

O Vite faz proxy de `/api` e `/hubs` para a API em `http://localhost:5080`. O banco de dev e as
chaves de sessão ficam em `src/VpsControl.Api/.data/` (ignorado pelo git); apague a pasta para
recomeçar do zero.

### Testar o caminho API → agente localmente

O agente em modo Development escuta TCP `localhost:5081`, sem token, com dados fake:

```bash
pnpm dev:agent
```

```bash
dotnet run --project src/VpsControl.Api --launch-profile http-agent
```

### Rodar a stack de produção localmente

```bash
docker build -f deploy/docker/api.Dockerfile -t vps-control-api:local .
```

```bash
bash deploy/scripts/build-agent.sh x64
```

O build do agente usa Docker para compilar AOT para Linux, inclusive a partir do Windows.

---

## Comandos

| Comando | O que faz |
|---|---|
| `pnpm dev:api` | API em `http://localhost:5080` (métricas fake) |
| `pnpm dev:web` | Frontend em `http://localhost:5173` |
| `pnpm dev:agent` | Agente em TCP `localhost:5081` (dados fake) |
| `pnpm build` | Build da solution .NET + build do frontend |
| `pnpm test` | Testes .NET + Vitest |
| `pnpm lint` | ESLint do frontend |
| `pnpm --filter @vps-control/web format` | Prettier no frontend |
| `pnpm gen:api` | Rebuild da API → `openapi.json` → `src/api/schema.d.ts` |
| `dotnet ef migrations add <Nome> --project src/VpsControl.Api --output-dir Data/Migrations` | Nova migration |
| `bash deploy/scripts/build-agent.sh [x64\|arm64]` | Binário AOT do agente em `artifacts/agent-<arch>/` |

**Ao alterar endpoints ou DTOs:** rode `pnpm gen:api` e versione `apps/web/openapi/openapi.json`
e `apps/web/src/api/schema.d.ts`. O CI falha se estiverem desatualizados.

**Migrations** são aplicadas automaticamente no start da API.

---

## Configuração

### API

Chaves em `appsettings*.json`. Também podem ser definidas por variáveis de ambiente com prefixo
`VPSCTL_` e `__` como separador (ex.: `VPSCTL_Auth__SessionHours=12`).

| Chave | Padrão (produção) | Descrição |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=/data/vpscontrol.db` | Banco SQLite. Chaves de sessão ficam em `<dir>/keys` |
| `Auth:AdminUsername` | `admin` | Usuário criado no primeiro start |
| `Auth:InitialAdminPassword` | — | Senha do admin inicial (≥ 12 caracteres). Usada só com banco vazio |
| `Auth:TailscaleAllowedUsers` | `[]` | Identidades Tailscale permitidas. Vazio = desligado |
| `Auth:SecureCookies` | `true` | Cookies `Secure`. `false` só em dev HTTP |
| `Auth:SessionHours` | `8` | Duração da sessão (deslizante) |
| `Auth:LoginAttemptsPerMinute` | `5` | Rate limit de login por IP |
| `Metrics:Source` | `Agent` | `Agent`, `Fake` ou `ProcFs` |
| `Metrics:AgentSocketPath` | `/run/vps-control/agent.sock` | Socket do agente |
| `Metrics:AgentUrl` | — | Alternativa TCP (dev), ex.: `http://localhost:5081` |
| `Metrics:AgentTokenFile` | `/run/secrets/agent_token` | Arquivo com o token do agente |
| `Metrics:SampleIntervalSeconds` | `2` | Intervalo de coleta |
| `Metrics:HistoryMinutes` | `60` | Tamanho do histórico em memória |
| `Proxy:AssumeHttps` | `true` | Trata requisições como HTTPS (TLS termina no Tailscale Serve) |
| `ForwardedHeaders:KnownNetworks` | `[]` | Redes confiáveis para `X-Forwarded-*` |

### Agente

Arquivo `appsettings.json` ao lado do binário, ou variáveis `Agent__<Chave>`.

| Chave | Padrão | Descrição |
|---|---|---|
| `Agent:SocketPath` | `/run/vps-control/agent.sock` | Unix socket. Vazio = TCP (somente dev) |
| `Agent:TcpPort` | `5081` | Porta loopback quando sem socket |
| `Agent:TokenFile` | `/etc/vps-control/agent.token` | Token compartilhado. Obrigatório fora de Development |
| `Agent:MetricsSource` | `ProcFs` | `ProcFs` ou `Fake` |

### Compose (`.env`)

| Variável | Descrição |
|---|---|
| `VPSCTL_GID` | GID do grupo `vpsctl` no host (impresso por `install-agent.sh`) |
| `VPSCTL_ADMIN_PASSWORD` | Senha do admin inicial; remova após o primeiro login |
| `VPSCTL_TAILSCALE_USER` | Opcional: identidade Tailscale permitida (ex.: `voce@github`) |

---

## API

Contrato OpenAPI completo em [apps/web/openapi/openapi.json](apps/web/openapi/openapi.json)
(em dev também em `http://localhost:5080/openapi/v1.json`).

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/healthz` | — | Liveness (usado pelo healthcheck do container) |
| GET | `/api/health` | — | Status e versão |
| GET | `/api/auth/antiforgery` | — | Emite o cookie `XSRF-TOKEN` |
| POST | `/api/auth/login` | — | `{ username, password }` → cria sessão (rate limited) |
| POST | `/api/auth/logout` | ✔ | Encerra a sessão |
| GET | `/api/auth/me` | ✔ | Usuário atual e identidade Tailscale |
| GET | `/api/system/status` | ✔ | Disponibilidade da fonte de métricas e último erro |
| GET | `/api/system/snapshot` | ✔ | Última amostra completa (503 se ainda não houver) |
| GET | `/api/system/history` | ✔ | Pontos do histórico (CPU %, memória %, rx/tx) |
| GET | `/api/audit?limit=100` | ✔ | Registros de auditoria (máx. 500) |

Mutações (`POST`, `PUT`, `PATCH`, `DELETE`) exigem o header `X-XSRF-TOKEN` com o valor do cookie
`XSRF-TOKEN`; o client do frontend faz isso automaticamente.

### Tempo real

Hub SignalR em `/hubs/metrics` (requer sessão). Eventos enviados pelo servidor:

| Evento | Payload | Quando |
|---|---|---|
| `snapshot` | `(HostSnapshot, MetricsPoint)` | A cada amostra bem-sucedida |
| `status` | `SourceStatus` | Quando a coleta falha |

---

## Testes e qualidade

```bash
pnpm test
```

- **API (15 testes de integração):** endpoints públicos/protegidos, rejeição sem antiforgery,
  senha errada auditada, login → métricas, logout, rota `/api` desconhecida, login via HTTP atrás
  de proxy TLS, rate limit, allowlist Tailscale (inclusive entrada vazia).
- **Core (8 testes):** parsers de `/proc/stat`, `meminfo`, `net/dev`, `mounts`, `loadavg`,
  `uptime`, `os-release` e leitura do `/proc` real (efetiva só em Linux).
- **Frontend (Vitest):** formatação de bytes, uptime e severidade.

Padrões aplicados no build: `TreatWarningsAsErrors`, analyzers `latest-recommended`, nullable,
ESLint (incluindo regras de hooks/pureza do React) e Prettier.

### CI (GitHub Actions)

[.github/workflows/ci.yml](.github/workflows/ci.yml) executa em push para `main` e em PRs:

1. **dotnet:** restore, build Release, testes, verificação do `openapi.json` versionado.
2. **web:** tipos gerados atualizados, lint, format check, testes, build.
3. **images:** build da imagem da API e do agente Native AOT.

---

## Deploy na VPS

Guia completo: **[docs/deployment/deploy.md](docs/deployment/deploy.md)**. Resumo:

1. Compilar e instalar o agente:

   ```bash
   bash deploy/scripts/build-agent.sh x64
   ```

   ```bash
   sudo deploy/scripts/install-agent.sh artifacts/agent-x64
   ```

2. Configurar e subir a API:

   ```bash
   cp .env.example .env
   ```

   ```bash
   docker compose up -d --build
   ```

3. Publicar na tailnet:

   ```bash
   sudo tailscale serve --bg --https=443 http://127.0.0.1:8080
   ```

4. Validar que nada ficou exposto:

   ```bash
   sudo ss -tlnp | grep 8080
   ```

5. Agendar backup diário:

   ```bash
   sudo deploy/scripts/backup-db.sh /var/backups/vps-control
   ```

---

## Solução de problemas

| Sintoma | Causa provável / correção |
|---|---|
| "Fonte de métricas indisponível" / "Agente inacessível" | Agente parado (`systemctl status vpsctl-agent`) ou `VPSCTL_GID` errado |
| "Agente recusou o token" | Token montado no container difere de `/etc/vps-control/agent.token` |
| 403 em todas as rotas | `VPSCTL_TAILSCALE_USER` diferente do seu login Tailscale |
| Login não persiste | Acesso por `http://<ip>`; use a URL `https://…ts.net` do Serve |
| 429 no login | Rate limit; aguarde 1 minuto |
| Todos deslogados após atualização | Volume `data` recriado (chaves de sessão ficam em `/data/keys`) |
| Build local falha com `NU1903` (ILCompiler) | SDK desatualizado; o projeto força o ILCompiler 10.0.15, mas atualize o SDK |
| Dev: "Offline" no topo do dashboard | API não está rodando em `localhost:5080` |

---

## Roadmap

Após o MVP (fases 1–6): histórico persistente com retenção, alertas (CPU, RAM, disco,
indisponibilidade), monitoramento HTTP das aplicações, notificações, gestão de imagens, volumes
e redes, backups de aplicações, terminal web com controles extras, múltiplas VPS, dashboard
com widgets e exportação de métricas.

---

## Convenções

- **Contratos primeiro:** mudou o agente → edite `agent.proto`; mudou a API → `pnpm gen:api`.
- **Toda nova ação administrativa** deve: exigir autenticação, passar pelo antiforgery, ser
  auditada via `AuditLog` e, se tocar o host, ser um RPC enumerado do agente.
- **Versões NuGet** só em `Directory.Packages.props`.
- **Idioma:** identificadores em inglês; comentários, mensagens de UI e documentação em português.
- **Segredos** nunca no repositório: `.env`, `*.token` e bancos são ignorados pelo git.
