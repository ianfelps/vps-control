# syntax=docker/dockerfile:1
# "Bundle" de deploy: tudo que vai para o host além da imagem da API, versionado junto no GHCR.
#   /bundle/agent/        binário Native AOT + appsettings.json
#   /bundle/systemd/      unit do agente
#   /bundle/scripts/      install-agent.sh, backup-db.sh
#   /bundle/compose.yaml
# O deploy.sh na VPS extrai com `docker create` + `docker cp` (a imagem nunca é executada).
ARG ARCH=x64

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS agent
ARG ARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/VpsControl.Core/VpsControl.Core.csproj src/VpsControl.Core/
COPY src/VpsControl.Contracts/VpsControl.Contracts.csproj src/VpsControl.Contracts/
COPY src/VpsControl.Agent/VpsControl.Agent.csproj src/VpsControl.Agent/
RUN dotnet restore src/VpsControl.Agent/VpsControl.Agent.csproj -r linux-${ARCH}
COPY src/VpsControl.Core src/VpsControl.Core
COPY src/VpsControl.Contracts src/VpsControl.Contracts
COPY src/VpsControl.Agent src/VpsControl.Agent
RUN dotnet publish src/VpsControl.Agent/VpsControl.Agent.csproj -c Release -r linux-${ARCH} \
      --no-restore -o /out \
 && rm -f /out/*.dbg /out/*.pdb /out/*.staticwebassets.*.json /out/appsettings.Development.json

FROM scratch
COPY --from=agent /out /bundle/agent
COPY deploy/systemd/vpsctl-agent.service /bundle/systemd/vpsctl-agent.service
COPY deploy/scripts/install-agent.sh deploy/scripts/backup-db.sh /bundle/scripts/
COPY compose.yaml /bundle/compose.yaml
