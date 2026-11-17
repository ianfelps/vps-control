# syntax=docker/dockerfile:1
# Compila o agente Native AOT para Linux (precisa de toolchain Linux; funciona a partir do Windows).
# Uso: deploy/scripts/build-agent.sh  →  artifacts/agent/vpsctl-agent
ARG ARCH=x64

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
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
COPY --from=build /out /
