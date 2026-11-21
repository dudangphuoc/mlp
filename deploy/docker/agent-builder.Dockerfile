# agent-builder (plug-and-play): keeps the agent (and mlp-shim) source in the control plane, builds mlp-agent for the RID of each plugged-in machine, then caches it.
# Native AOT for the RID matching the builder's architecture (linux glibc); other RIDs → self-contained single-file (no cross toolchain needed).
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props BannedSymbols.txt ./
COPY proto/ proto/
COPY src/Framework/MLPlatform.Contracts/ src/Framework/MLPlatform.Contracts/
COPY src/Framework/MLPlatform.AgentProtocol/ src/Framework/MLPlatform.AgentProtocol/
COPY src/Hosts/MLPlatform.Agent/ src/Hosts/MLPlatform.Agent/
# mlp-shim (baked into recipe images for training) is built here too, as rid "shim-linux-{x64,arm64}"
COPY src/Framework/MLPlatform.Recipes/ src/Framework/MLPlatform.Recipes/
COPY src/Hosts/MLPlatform.Shim/ src/Hosts/MLPlatform.Shim/
RUN find . -name bin -o -name obj | xargs rm -rf \
 && dotnet restore src/Hosts/MLPlatform.Agent -p:RestoreLockedMode=false \
 && dotnet restore src/Hosts/MLPlatform.Shim -p:RestoreLockedMode=false
COPY deploy/docker/agent-builder.sh /usr/local/bin/agent-builder
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
CMD ["agent-builder"]
