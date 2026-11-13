# Deploy a relay

Run a relay on your own server so paired phones and computers can reach your local DotCraft from other networks.

Use a Linux x64 server with Docker and Compose installed, and a domain name pointing to that server. Open TCP ports 80 and 443 on the server. The deployment template configures HTTPS automatically.

## Download the deployment files

Choose a version from the [DotCraft releases](https://github.com/DotHarness/dotcraft/releases) and replace `vX.Y.Z` below with its version tag:

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

## Configure and start

Generate a random token:

```bash
openssl rand -hex 32
```

Edit `.env` with the server's domain name and the token you generated:

```dotenv
RELAY_PUBLIC_HOST=relay.example.com
RELAY_TOKEN=your-generated-token
```

Keep `.env` private and out of version control. Start the relay:

```bash
docker compose up -d
```

## Connect Desktop

On the computer you want to reach, open **Settings → Connections → Control this PC → Access from anywhere**, enter the relay address (such as `https://relay.example.com`) and the same token, and enable device access.

For devices you've already paired, connect to the computer once on the same network. They can then connect from other networks.

## Use an existing reverse proxy

If the server already has an HTTPS reverse proxy, start only the relay:

```bash
docker compose up -d relay
```

Forward the domain's `/r/*` requests to `http://127.0.0.1:47620`, preserving the request path and enabling WebSocket support and long-lived connections.

## Update the version

Download the deployment files from the target release, keep your existing `.env`, and change its `DOTCRAFT_VERSION` to the target version tag. Then run:

```bash
docker compose pull
docker compose up -d
```
