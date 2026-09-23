# 部署模型服务

让 Oratorio、机器人和其他 DotCraft Runtime 共用模型提供商。模型服务保存 API key 和 ChatGPT 登录凭据，每个 Runtime 使用独立、可撤销的客户端凭据连接。

## 启动服务

从 DotCraft 源码目录复制 Compose 模板：

```bash
cp -r docker/model-service /opt/dotcraft-models
cd /opt/dotcraft-models
mkdir -p state
cp config.example.json state/config.json
```

示例配置使用名为 `openai` 的 ChatGPT 提供商。使用 API key 时，编辑 `state/config.json`：

```json
{
  "Providers": {
    "openai": {
      "Protocol": "openai-responses",
      "ApiKey": "$OPENAI_API_KEY"
    }
  }
}
```

在该部署的 `.env` 中填写密钥：

```dotenv
OPENAI_API_KEY=your-openai-api-key
```

Compose 模板会将 `OPENAI_API_KEY` 传入 model-service 容器。其他提供商使用的环境变量，需要在 `docker-compose.yml` 中该服务的 `environment` 下添加对应条目。

使用 ChatGPT 时，在有浏览器的电脑上转发回调端口：

```bash
ssh -N -L 1455:127.0.0.1:1455 -L 1457:127.0.0.1:1457 user@model-host
```

在 Linux 服务器上运行：

```bash
docker compose --profile auth run --rm auth
```

在浏览器中打开输出的链接并完成登录。登录凭据保存在 `state/credentials` 中。

在此部署的 `.env` 中，将 `MODEL_SERVICE_PUBLISH_HOST` 设置为服务器的私有网络地址，然后启动服务：

```bash
docker compose up -d model-service
```

默认端口为 `8090`。跨不可信网络连接时，在反向代理上配置 HTTPS。

## 连接 Stack

为每个 Runtime 创建凭据。命令会输出客户端 ID，并将凭据写入文件：

```bash
docker compose run --rm model-service dotcraft model-service --state /state \
  client create --name oratorio --provider openai --output /state/oratorio.token
```

将 `state/oratorio.token` 复制到运行 Stack 的机器，然后初始化部署：

```bash
dotcraft stack init --dir /opt/oratorio-stack --no-start \
  --model-service-url http://model-host:8090/model-service/ \
  --model-service-token-file /opt/oratorio.token \
  --provider openai --model your-model-id
cd /opt/oratorio-stack
docker compose up -d
dotcraft stack doctor --dir /opt/oratorio-stack
```

机器人部署使用单独创建的客户端凭据，在自己的 `.env` 中填写：

```dotenv
DOTCRAFT_MODEL_MODE=remote
DOTCRAFT_MODEL_SERVICE_URL=http://model-host:8090/model-service/
DOTCRAFT_MODEL_SERVICE_TOKEN=your-client-credential
DOTCRAFT_PROVIDER=openai
DOTCRAFT_MODEL=your-model-id
```

每个 Runtime 的工作区和配置卷与模型服务的 `state` 目录分开挂载。Desktop 可以选择所连接 Runtime 的模型，提供商配置和登录在模型服务端管理。

## 管理访问

使用客户端 ID 撤销访问：

```bash
docker compose run --rm model-service dotcraft model-service --state /state \
  client revoke --id your-client-id
```

通过同一个 Compose 服务运行 `dotcraft model-service --state /state check` 检查提供商配置，运行 `dotcraft model-service --state /state auth logout` 退出登录。

备份模型服务的 `state` 目录，其中包含提供商配置、客户端授权和订阅凭据。
