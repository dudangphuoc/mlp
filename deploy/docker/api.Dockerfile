# mlp-api — Native AOT (P0-10). Build: docker build -f deploy/docker/api.Dockerfile -t mlp/api:<ver> .
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN dotnet publish src/Hosts/MLPlatform.Api -c Release -m:1 -o /out \
 && mkdir -p /out/install && cp deploy/install/agent.sh deploy/install/agent.ps1 /out/install/ && cp -r db /out/db

FROM mcr.microsoft.com/dotnet/runtime-deps:11.0-resolute-chiseled
WORKDIR /app
COPY --from=build /out /app
USER app
EXPOSE 5080 5081 7443
ENTRYPOINT ["/app/mlp-api"]
