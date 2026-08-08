# syntax=docker/dockerfile:1
#
# Oratorio headless backend image.
#
# The build context is the DotCraft repository root.
#
#   docker build -f docker/oratorio/Dockerfile -t oratorio-server .
#
# ---------------------------------------------------------------------------
# Build stage: compile + publish a self-contained linux-x64 build.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH=amd64
WORKDIR /src

# Copy only what the server build needs.
COPY src/DotCraft.Protocol/ ./src/DotCraft.Protocol/
COPY src/DotCraft.Protocol.Generators/ ./src/DotCraft.Protocol.Generators/
COPY sdk/dotnet/src/DotCraft.Sdk/ ./sdk/dotnet/src/DotCraft.Sdk/
COPY src/Oratorio.Server/ ./src/Oratorio.Server/

WORKDIR /src/src/Oratorio.Server
RUN dotnet publish Oratorio.Server.csproj \
        -c Release -r linux-x64 --self-contained true \
        -o /out

# ---------------------------------------------------------------------------
# Runtime stage: minimal OS deps for a self-contained app + git (worktrees).
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime

# git: required by WorktreeManager / GitDeliveryClient (worktree add/commit/push).
# curl: used by the container HEALTHCHECK. tini: clean PID 1 / signal handling.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates curl tini \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/oratorio
COPY --from=build /out/ ./
COPY docker/oratorio/oratorio-entrypoint.sh /usr/local/bin/oratorio-entrypoint.sh
RUN chmod +x /usr/local/bin/oratorio-entrypoint.sh /opt/oratorio/oratorio-server

ENV ASPNETCORE_URLS=http://0.0.0.0:5087 \
    DOTCRAFT_MANAGED_SERVICE_STATE_ROOT=/data/oratorio \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0

EXPOSE 5087
VOLUME ["/data/oratorio"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://127.0.0.1:5087/health || exit 1

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/oratorio-entrypoint.sh"]
