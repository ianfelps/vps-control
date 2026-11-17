# Deploy na VPS com deploy automático

A cada push na `main` com CI verde, o GitHub Actions:

1. publica duas imagens no GHCR, ambas com a tag do **SHA do commit**:
   - `vps-control-api`: API e SPA;
   - `vps-control-bundle`: agente Native AOT, `compose.yaml`, unit systemd e scripts;
2. entra na sua tailnet como um nó **efêmero** `tag:ci`;
3. executa via **Tailscale SSH** `sudo /opt/vps-control/bin/deploy.sh <sha>` na VPS. É o único
   comando que o usuário `deploy` pode rodar.

O `deploy.sh` faz backup do banco, atualiza o agente se o binário mudou, sobe a API com
`docker compose --wait` e confere os healthchecks. Se algo falhar, **reativa a versão anterior
automaticamente**.

A VPS não precisa de porta SSH pública, e não existe chave SSH guardada no GitHub.

```
GitHub Actions ──(nó efêmero tag:ci)──▶ tailnet ──tailscale ssh──▶ deploy@VPS
                                                                     │ sudo deploy.sh <sha>
GHCR ◀──────────── docker pull api:<sha> + bundle:<sha> ─────────────┘
```

---

## 0. Pré-requisitos

- VPS com Ubuntu 22.04 ou 24.04 e acesso root (pelo SSH atual, para a configuração inicial).
- Conta Tailscale com permissão para editar a política de acesso (ACL).
- Repositório no GitHub com o CI passando.
- Saber a arquitetura da VPS: `uname -m` → `x86_64` (padrão) ou `aarch64` (use `VPS_ARCH=arm64`).

## 1. Instalar o Docker na VPS

```bash
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2 curl
```

```bash
docker compose version
```

O compose precisa ser 2.17 ou mais novo, por causa do `--wait`. Se preferir o Docker CE oficial,
siga https://docs.docker.com/engine/install/ubuntu/. O resto do guia funciona igual.

## 2. Política do Tailscale (tags e SSH)

No painel do Tailscale, em **Access controls**, **mescle** os trechos abaixo na sua política.
Não substitua o arquivo inteiro.

```jsonc
{
  "tagOwners": {
    "tag:vps": ["autogroup:admin"],
    "tag:ci":  ["autogroup:admin"]
  },
  "grants": [
    // Você (membros da tailnet) acessa o painel via HTTPS do Serve.
    { "src": ["autogroup:member"], "dst": ["tag:vps"], "ip": ["443"] },
    // O runner do CI só alcança a porta SSH da VPS.
    { "src": ["tag:ci"], "dst": ["tag:vps"], "ip": ["22"] }
  ],
  "ssh": [
    // O CI só pode entrar como o usuário "deploy".
    { "action": "accept", "src": ["tag:ci"], "dst": ["tag:vps"], "users": ["deploy"] }
  ]
}
```

> **Partindo da política padrão** (`{"src": ["*"], "dst": ["*"], "ip": ["*"]}`), duas mudanças
> são obrigatórias:
>
> - Troque `"src": ["*"]` por `"src": ["autogroup:member"]`. Os grants somam permissões, e com
>   `*` o nó efêmero `tag:ci` do GitHub alcançaria **todas** as máquinas da tailnet.
> - Inclua uma regra SSH `autogroup:member → tag:vps`. Um dispositivo com tag deixa de ser
>   `autogroup:self`, e você perderia o Tailscale SSH para a VPS.
>
> Use `tests`/`sshTests` na política para o Tailscale recusar salvar se as regras do CI ficarem
> largas demais.

## 3. Colocar a VPS na tailnet

```bash
curl -fsSL https://tailscale.com/install.sh | sh
```

```bash
sudo tailscale up --ssh --advertise-tags=tag:vps
```

Anote o nome MagicDNS da VPS (`tailscale status`, ex.: `minha-vps`). Ele será o `VPS_HOST`.

> `--ssh` liga o Tailscale SSH só para conexões vindas da tailnet. O seu SSH atual continua
> funcionando.

## 4. Preparar a VPS (bootstrap)

Copie os scripts para a VPS a partir da sua máquina:

```bash
scp -r deploy/scripts root@IP_DA_VPS:/tmp/vpsctl-scripts
```

Na VPS, use o owner do GitHub **em minúsculas**:

```bash
sudo bash /tmp/vpsctl-scripts/bootstrap-vps.sh ghcr.io/SEU_USUARIO
```

O bootstrap:

- cria o usuário `deploy`, sem senha e sem acesso ao Docker;
- grava `/etc/sudoers.d/vpsctl-deploy`, que permite ao `deploy` **somente** `/opt/vps-control/bin/deploy.sh`;
- instala o `deploy.sh` (`root:root 0755`) e cria `/opt/vps-control` e `/etc/vps-control/deploy.env`;
- gera `/opt/vps-control/.env` com uma **senha inicial do admin aleatória**, mostrada uma única
  vez. **Anote essa senha.**

Para restringir o painel à sua identidade Tailscale, preencha `VPSCTL_TAILSCALE_USER` em
`/opt/vps-control/.env` (ex.: `voce@github`).

## 5. Acesso da VPS ao GHCR

As imagens não contêm segredos. Escolha uma das opções:

- **Pacotes públicos:** depois do primeiro publish, vá em GitHub → Packages →
  `vps-control-api` e `vps-control-bundle` → Package settings → Change visibility → Public.
  Nenhum login é necessário.
- **Pacotes privados:** crie um token clássico com escopo apenas `read:packages` e faça login
  **como root**, porque o `deploy.sh` roda como root:

  ```bash
  sudo docker login ghcr.io -u SEU_USUARIO
  ```

## 6. Configurar o GitHub

**OAuth client do Tailscale:** no painel do Tailscale, em Settings → OAuth clients → Generate,
use o escopo **Auth Keys: Write** e a tag `tag:ci`.

No repositório, em **Settings → Secrets and variables → Actions**:

| Tipo | Nome | Valor |
|---|---|---|
| Secret | `TS_OAUTH_CLIENT_ID` | ID do OAuth client |
| Secret | `TS_OAUTH_SECRET` | Secret do OAuth client |
| Variable | `VPS_HOST` | Nome MagicDNS da VPS (ex.: `minha-vps`) |
| Variable | `VPS_ARCH` | Opcional: `arm64` se a VPS for ARM |

Em **Settings → Environments**, crie o environment `production`. Recomendo marcar **Required
reviewers** no início: cada deploy espera a sua aprovação. Os **secrets** podem ficar no
environment em vez do repositório.

> As **variáveis** `VPS_HOST` e `VPS_ARCH` precisam ficar no nível do **repositório**, não do
> environment. O job `publish` roda fora do environment, então lá elas chegariam vazias e o
> workflow terminaria como `skipped`.

## 7. Primeiro deploy

1. Faça push na `main` e espere o **CI**. O workflow **Deploy** dispara sozinho. Para disparar
   manualmente, vá em Actions → Deploy → Run workflow.
2. Se usou pacotes públicos, mude a visibilidade agora (passo 5) e rode o Deploy de novo.
3. O log do job mostra cada etapa (`[deploy hh:mm:ss] ...`) e termina com `OK: <sha> ativa`.

Na primeira execução, o `deploy.sh` também instala o agente:

- grupo `vpsctl`;
- usuário `vpsctl-agent`;
- token em `/etc/vps-control/agent.token`;
- unit `vpsctl-agent.service` com acesso ao `docker.sock`.

Depois disso, ele preenche `VPSCTL_GID` no `.env`.

## 8. Publicar o painel na tailnet

```bash
sudo tailscale serve --bg --https=443 http://127.0.0.1:8080
```

Acesse `https://minha-vps.SUA-TAILNET.ts.net` e entre com o usuário `admin` e a senha do passo 4.

Em seguida, remova a senha inicial do `.env`. O hash dela já ficou guardado no SQLite.

```bash
sudo sed -i '/^VPSCTL_ADMIN_PASSWORD=/d' /opt/vps-control/.env
```

## 9. Checklist de validação

```bash
sudo ss -tlnp | grep -E ':8080'
```

Deve aparecer só `127.0.0.1:8080`.

Da sua máquina, **fora** da tailnet, isto deve falhar:

```bash
curl -m 5 http://IP_PUBLICO_DA_VPS:8080/
```

```bash
systemctl status vpsctl-agent --no-pager
```

```bash
ls -l /run/vps-control/
```

O socket deve aparecer como `srw-rw---- vpsctl-agent vpsctl`.

No painel:

- o dashboard mostra o hostname, os discos e as interfaces reais;
- Containers lista os seus containers, e `vps-control-api-1` aparece como **protegido**;
- Auditoria registra o seu login.

## 10. Backup diário

O deploy já faz um backup antes de cada atualização. Para ter também um backup diário, abra o
crontab do root:

```bash
sudo crontab -e
```

Adicione a linha:

```
0 3 * * * /opt/vps-control/bin/backup-db.sh /var/backups/vps-control >/dev/null 2>&1
```

Teste a restauração ao menos uma vez:

```bash
cd /opt/vps-control && sudo docker compose stop api
```

```bash
sudo docker run --rm -v vps-control_data:/data -v /var/backups/vps-control:/backup alpine:3.22 cp /backup/vpscontrol-STAMP.db /data/vpscontrol.db
```

```bash
cd /opt/vps-control && sudo docker compose start api
```

## 11. Opcional: fechar o SSH público

Só faça isso **depois** de confirmar que `tailscale ssh SEU_USUARIO@minha-vps` funciona da sua
máquina. Bloqueie a porta 22 pública no firewall do provedor, ou com
`ufw deny 22/tcp` combinado com `ufw allow in on tailscale0`. Se errar, você perde o acesso
remoto e precisará do console do provedor.

---

## Operação no dia a dia

| Tarefa | Como |
|---|---|
| Deploy | Push na `main` → CI → Deploy (com aprovação, se o environment exigir) |
| Rollback | Actions → Deploy → Run workflow → informe um **SHA anterior** |
| Ver versão ativa | `cat /opt/vps-control/current` |
| Deploy manual na VPS | `sudo /opt/vps-control/bin/deploy.sh <sha40>` |
| Logs da API | `cd /opt/vps-control && sudo docker compose logs -f api` |
| Logs do agente | `journalctl -u vpsctl-agent -f` |
| Atualizar o `deploy.sh` | Rode o `bootstrap-vps.sh` de novo; ele é idempotente e preserva o `.env` |

O `deploy.sh` guarda as 5 releases mais recentes em `/opt/vps-control/releases/`, além da atual e
da anterior. Um rollback para qualquer uma delas não precisa de download.

### O que acontece se um deploy falhar

| Falha | Resultado |
|---|---|
| Imagem não encontrada no GHCR | Aborta antes de mexer em qualquer coisa |
| Agente novo não sobe | Rollback para a versão anterior |
| API não fica saudável (`compose --wait` ou `/api/health`) | Rollback |
| Agente cai durante o deploy | Rollback |
| Outro deploy em andamento | Recusa (lock); o GitHub enfileira os workflows |

O job do GitHub termina com erro e o log mostra `ROLLBACK para <sha>`.

### Solução de problemas

| Sintoma | Causa provável |
|---|---|
| `tailscale ssh` recusado no job | Regra `ssh` ou `grants` da ACL ausente, ou o OAuth client sem `tag:ci` |
| `sudo: a password is required` | O sudoers não foi instalado; rode o bootstrap de novo |
| `denied` / `unauthorized` no `docker pull` | Pacote privado sem `sudo docker login ghcr.io` |
| `IMAGE_PREFIX inválido` | Owner com maiúsculas; o bootstrap exige o prefixo em minúsculas |
| `exec format error` no agente | Arquitetura errada; defina `VPS_ARCH=arm64` |
| "Agente inacessível" no painel | `systemctl status vpsctl-agent`; confira o `VPSCTL_GID` no `.env` |
| 403 em todas as páginas | `VPSCTL_TAILSCALE_USER` diferente do seu login Tailscale |
| Login não persiste | Acesso por `http://IP`; use a URL `https://…ts.net` |

### Validação deste fluxo

O `deploy.sh` foi testado numa VPS simulada: Ubuntu 24.04 com Docker Engine próprio e registry
local no lugar do GHCR. Cenários cobertos:

- primeira instalação;
- redeploy idempotente;
- agente parado sendo reinstalado;
- versão quebrada com rollback automático;
- rejeição de SHA inválido;
- restrição do sudoers.

O que só pode ser validado na VPS real: systemd de verdade com o hardening da unit, Tailscale
SSH e Serve, e o acesso ao GHCR.
