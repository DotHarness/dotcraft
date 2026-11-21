# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotcraft-build
WORKDIR /src

COPY dotcraft.sln ./
COPY src ./src
COPY sdk/dotnet/src/DotCraft.Sdk ./sdk/dotnet/src/DotCraft.Sdk
COPY desktop/src/renderer/styles/foundations/tokens.css desktop/src/renderer/styles/foundations/themes.css ./desktop/src/renderer/styles/foundations/
COPY desktop/resources/dotcraft.svg ./desktop/resources/
COPY docker/dotcraft ./docker/dotcraft
RUN dotnet restore src/DotCraft.App/DotCraft.App.csproj
RUN dotnet publish src/DotCraft.App/DotCraft.App.csproj \
    -c Release \
    -r linux-x64 \
    -p:PublishProfile=ReleaseProfile \
    -o /out/dotcraft

FROM node:22-bookworm-slim AS channel-seeds
ARG CHANNEL_SEEDS_RELEASE
ARG CHANNEL_SEEDS_SHA256
COPY docker/dotcraft/stage-channel-seeds.mjs /stage-channel-seeds.mjs
RUN node /stage-channel-seeds.mjs /out/channel-seeds

FROM node:22-bookworm-slim AS sdk-build
WORKDIR /src/sdk/typescript

COPY sdk/typescript/package.json sdk/typescript/package-lock.json ./
COPY sdk/typescript/packages ./packages
RUN npm ci

COPY sdk/typescript/tsconfig.json sdk/typescript/tsconfig.base.json ./
COPY sdk/typescript/scripts ./scripts
COPY sdk/typescript/src ./src
RUN npm run build:all

FROM sdk-build AS desktop-plugins-build
WORKDIR /src/desktop

COPY desktop/package.json desktop/package-lock.json ./
RUN npm ci --ignore-scripts

COPY desktop/scripts/build-bundled-plugins.mjs ./scripts/build-bundled-plugins.mjs
COPY desktop/src/bundled-plugins ./src/bundled-plugins
COPY desktop/src/shared ./src/shared
COPY desktop/resources/plugins/dotcraft-bundled/plugins ./resources/plugins/dotcraft-bundled/plugins
RUN npm run prepare:bundled-plugins

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS relay
WORKDIR /app
COPY --from=dotcraft-build --chmod=755 /out/dotcraft/dotcraft /app/dotcraft
USER $APP_UID
EXPOSE 47620
ENTRYPOINT ["/app/dotcraft", "relay", "serve"]
CMD ["--listen", "http://0.0.0.0:47620"]

FROM node:22-bookworm-slim AS runtime
WORKDIR /workspace

# Git and ripgrep are part of the baseline agent environment for repository
# inspection. Keep project-specific compilers and SDKs out of this image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates git libicu72 python3 ripgrep tini \
    && rm -rf /var/lib/apt/lists/*

COPY --from=dotcraft-build /out/dotcraft/dotcraft /opt/dotcraft/bin/dotcraft
COPY --from=channel-seeds /out/channel-seeds /opt/dotcraft/channel-seeds

COPY --from=desktop-plugins-build /src/desktop/resources/plugins/dotcraft-bundled/plugins /opt/dotcraft/plugins

COPY docker/dotcraft/entrypoint.sh /opt/dotcraft/entrypoint.sh
COPY docker/dotcraft/oratorio-entrypoint.sh /opt/dotcraft/oratorio-entrypoint.sh
COPY docker/dotcraft/render-config.mjs /opt/dotcraft/render-config.mjs

RUN chmod +x /opt/dotcraft/bin/dotcraft /opt/dotcraft/entrypoint.sh /opt/dotcraft/oratorio-entrypoint.sh

ENV PATH="/opt/dotcraft/bin:${PATH}" \
    DOTCRAFT_NODE_BIN="/usr/local/bin/node" \
    DOTCRAFT_CHANNEL_SEEDS="/opt/dotcraft/channel-seeds" \
    DOTCRAFT_BUILTIN_PLUGIN_ROOTS="/opt/dotcraft/plugins" \
    DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL="https://github.com/DotHarness/dotcraft-plugins/archive/refs/heads/main.zip" \
    DOTCRAFT_CONTAINER="true"

EXPOSE 5087 6700 8080 9000 9100

ENTRYPOINT ["/usr/bin/tini", "--", "/opt/dotcraft/entrypoint.sh"]
