# 部署中继服务

在自己的服务器上部署中继，让已配对的手机和电脑从其他网络访问本机 DotCraft。

准备一台安装了 Docker 和 Compose 的 Linux x64 服务器，以及指向该服务器的域名。开放服务器的 TCP 80 和 443 端口，部署模板会自动配置 HTTPS。

## 下载部署文件

从 [DotCraft 发布列表](https://github.com/DotHarness/dotcraft/releases)选择版本，将下面的 `vX.Y.Z` 替换为该版本号：

```bash
mkdir -p /opt/dotcraft-relay
cd /opt/dotcraft-relay
DOTCRAFT_VERSION=vX.Y.Z
TEMPLATE_URL="https://raw.githubusercontent.com/DotHarness/dotcraft/${DOTCRAFT_VERSION}/docker/relay"
curl -fSLO "${TEMPLATE_URL}/docker-compose.yml"
curl -fSLO "${TEMPLATE_URL}/.env.example"
curl -fSLO "${TEMPLATE_URL}/Caddyfile"
sed "s/^DOTCRAFT_VERSION=.*/DOTCRAFT_VERSION=${DOTCRAFT_VERSION}/" .env.example > .env
```

## 配置并启动

生成一个随机 Token：

```bash
openssl rand -hex 32
```

编辑 `.env`，填入服务器域名和生成的 Token：

```dotenv
RELAY_PUBLIC_HOST=relay.example.com
RELAY_TOKEN=your-generated-token
```

不要公开或提交 `.env`。启动中继：

```bash
docker compose up -d
```

## 连接 Desktop

在需要被访问的电脑上打开 **设置 → 连接 → 控制这台电脑 → 随处访问**，填入中继地址（例如 `https://relay.example.com`）和相同的 Token，并打开设备访问。

已经配对的设备，先在同一网络下连接电脑一次，然后就能从其他网络访问。

## 使用已有反向代理

如果服务器已有 HTTPS 反向代理，只启动中继：

```bash
docker compose up -d relay
```

将该域名的 `/r/*` 请求转发到 `http://127.0.0.1:47620`，保留请求路径，并启用 WebSocket 和长连接支持。

## 更新版本

从目标版本下载部署文件，保留已有的 `.env`，将其中的 `DOTCRAFT_VERSION` 改为目标版本号，然后运行：

```bash
docker compose pull
docker compose up -d
```
