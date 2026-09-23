# Deploy a model service

Share model providers across Oratorio, bots, and other DotCraft Runtimes. The model service stores API keys and ChatGPT sign-in credentials. Each Runtime connects with its own revocable client credential.

## Start the service

From a DotCraft source checkout, copy the Compose template to its deployment directory:

```bash
cp -r docker/model-service /opt/dotcraft-models
cd /opt/dotcraft-models
mkdir -p state
cp config.example.json state/config.json
```

The example configures a ChatGPT provider named `openai`. To use an API key, edit `state/config.json`:

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

Set the key in this deployment's `.env`:

```dotenv
OPENAI_API_KEY=your-openai-api-key
```

The Compose template passes `OPENAI_API_KEY` to the model-service container. For other provider environment variables, add matching entries under the service's `environment` in `docker-compose.yml`.

For ChatGPT, forward the callback ports from the computer with your browser:

```bash
ssh -N -L 1455:127.0.0.1:1455 -L 1457:127.0.0.1:1457 user@model-host
```

On the Linux server, run:

```bash
docker compose --profile auth run --rm auth
```

Open the printed URL in your browser and complete sign-in. The service keeps the login in `state/credentials`.

Set `MODEL_SERVICE_PUBLISH_HOST` in this deployment's `.env` to the server's private network address. Start the service:

```bash
docker compose up -d model-service
```

The default port is `8090`. Use HTTPS at your reverse proxy when connecting across an untrusted network.

## Connect a Stack

Create a credential for each Runtime. The command prints its client ID and writes the credential to a file:

```bash
docker compose run --rm model-service dotcraft model-service --state /state \
  client create --name oratorio --provider openai --output /state/oratorio.token
```

Copy `state/oratorio.token` to the machine hosting the Stack, then initialize it:

```bash
dotcraft stack init --dir /opt/oratorio-stack --no-start \
  --model-service-url http://model-host:8090/model-service/ \
  --model-service-token-file /opt/oratorio.token \
  --provider openai --model your-model-id
cd /opt/oratorio-stack
docker compose up -d
dotcraft stack doctor --dir /opt/oratorio-stack
```

For a bot deployment, set the same connection in its `.env` using a separately created client credential:

```dotenv
DOTCRAFT_MODEL_MODE=remote
DOTCRAFT_MODEL_SERVICE_URL=http://model-host:8090/model-service/
DOTCRAFT_MODEL_SERVICE_TOKEN=your-client-credential
DOTCRAFT_PROVIDER=openai
DOTCRAFT_MODEL=your-model-id
```

Keep each Runtime's workspace and configuration volumes separate from the model service's `state` directory. Desktop can select models on a connected Runtime. Provider configuration and sign-in are managed on the model service.

## Manage access

Revoke a client by its ID:

```bash
docker compose run --rm model-service dotcraft model-service --state /state \
  client revoke --id your-client-id
```

Check provider configuration with `dotcraft model-service --state /state check`. Sign out with `dotcraft model-service --state /state auth logout`, run through the same Compose service.

Back up the model service's `state` directory. It contains provider configuration, client grants, and subscription credentials.
