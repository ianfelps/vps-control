# syntax=docker/dockerfile:1
# Build: docker build -f deploy/docker/api.Dockerfile -t vps-control-api .

# ---------- Frontend ----------
FROM node:22-alpine AS web
WORKDIR /src
RUN corepack enable
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY apps/web/package.json apps/web/
RUN pnpm install --frozen-lockfile --filter @vps-control/web
COPY apps/web apps/web
RUN pnpm --filter @vps-control/web build

# ---------- Backend ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/VpsControl.Core/VpsControl.Core.csproj src/VpsControl.Core/
COPY src/VpsControl.Contracts/VpsControl.Contracts.csproj src/VpsControl.Contracts/
COPY src/VpsControl.Api/VpsControl.Api.csproj src/VpsControl.Api/
RUN dotnet restore src/VpsControl.Api/VpsControl.Api.csproj
COPY src/VpsControl.Core src/VpsControl.Core
COPY src/VpsControl.Contracts src/VpsControl.Contracts
COPY src/VpsControl.Api src/VpsControl.Api
# OpenAPI já está versionado em apps/web/openapi; não regenerar dentro da imagem.
RUN dotnet publish src/VpsControl.Api/VpsControl.Api.csproj -c Release -o /out --no-restore \
      -p:OpenApiGenerateDocuments=false
COPY --from=web /src/apps/web/dist /out/wwwroot
RUN mkdir -p /data

# ---------- Runtime ----------
# Chiseled: sem shell, sem gerenciador de pacotes, usuário não-root (app, UID 1654).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=api /out .
COPY --from=api --chown=1654:1654 /data /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "VpsControl.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "VpsControl.Api.dll"]
