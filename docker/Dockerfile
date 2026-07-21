# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotcraft-build
WORKDIR /src

COPY dotcraft.sln ./
COPY src ./src
RUN dotnet restore src/DotCraft.App/DotCraft.App.csproj
RUN dotnet publish src/DotCraft.App/DotCraft.App.csproj \
    -c Release \
    -r linux-x64 \
    -p:PublishProfile=ReleaseProfile \
    -o /out/dotcraft

FROM node:22-bookworm-slim AS channels-build
WORKDIR /src/sdk/typescript

COPY sdk/typescript/package.json sdk/typescript/package-lock.json ./
COPY sdk/typescript/packages ./packages
RUN npm ci

COPY sdk/typescript/tsconfig.json ./
COPY sdk/typescript/scripts ./scripts
COPY sdk/typescript/src ./src
RUN npm run build:all

FROM node:22-bookworm-slim AS runtime
WORKDIR /workspace

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates python3 python3-pip tini libicu72 \
    && pip3 install --break-system-packages --no-cache-dir opensandbox-server \
    && rm -rf /var/lib/apt/lists/*

COPY --from=dotcraft-build /out/dotcraft/dotcraft /opt/dotcraft/bin/dotcraft
COPY --from=channels-build /src/sdk/typescript/packages/channel-feishu/manifest.json /opt/dotcraft/modules/channel-feishu/manifest.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-feishu/package.json /opt/dotcraft/modules/channel-feishu/package.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-feishu/dist /opt/dotcraft/modules/channel-feishu/dist
COPY --from=channels-build /src/sdk/typescript/packages/channel-qq/manifest.json /opt/dotcraft/modules/channel-qq/manifest.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-qq/package.json /opt/dotcraft/modules/channel-qq/package.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-qq/dist /opt/dotcraft/modules/channel-qq/dist
COPY --from=channels-build /src/sdk/typescript/packages/channel-telegram/manifest.json /opt/dotcraft/modules/channel-telegram/manifest.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-telegram/package.json /opt/dotcraft/modules/channel-telegram/package.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-telegram/dist /opt/dotcraft/modules/channel-telegram/dist
COPY --from=channels-build /src/sdk/typescript/packages/channel-wecom/manifest.json /opt/dotcraft/modules/channel-wecom/manifest.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-wecom/package.json /opt/dotcraft/modules/channel-wecom/package.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-wecom/dist /opt/dotcraft/modules/channel-wecom/dist
COPY --from=channels-build /src/sdk/typescript/packages/channel-weixin/manifest.json /opt/dotcraft/modules/channel-weixin/manifest.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-weixin/package.json /opt/dotcraft/modules/channel-weixin/package.json
COPY --from=channels-build /src/sdk/typescript/packages/channel-weixin/dist /opt/dotcraft/modules/channel-weixin/dist

COPY docker/entrypoint.sh /opt/dotcraft/entrypoint.sh
COPY docker/render-config.mjs /opt/dotcraft/render-config.mjs
COPY docker/opensandbox-entrypoint.sh /opt/dotcraft/opensandbox-entrypoint.sh

RUN chmod +x /opt/dotcraft/bin/dotcraft /opt/dotcraft/entrypoint.sh /opt/dotcraft/opensandbox-entrypoint.sh

ENV PATH="/opt/dotcraft/bin:${PATH}" \
    DOTCRAFT_NODE_BIN="/usr/local/bin/node" \
    DOTCRAFT_MODULES_DIR="/opt/dotcraft/modules" \
    DOTCRAFT_CONTAINER="true"

EXPOSE 6700 8080 9000 9100

ENTRYPOINT ["/usr/bin/tini", "--", "/opt/dotcraft/entrypoint.sh"]
