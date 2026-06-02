import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";

const env = process.env;
const workspace = env.DOTCRAFT_WORKSPACE || "/workspace";
const craftDir = path.join(workspace, ".craft");
const userCraftDir = path.join(env.HOME || "/root", ".craft");

const knownChannels = new Set(["telegram", "feishu", "qq", "wecom", "weixin"]);
const defaultOneClickChannels = ["telegram", "feishu", "qq", "wecom"];

function trim(value) {
  return typeof value === "string" ? value.trim() : "";
}

function boolEnv(name, defaultValue) {
  const value = trim(env[name]).toLowerCase();
  if (!value) return defaultValue;
  return ["1", "true", "yes", "on"].includes(value);
}

function intEnv(name, defaultValue) {
  const value = Number.parseInt(trim(env[name]), 10);
  return Number.isInteger(value) ? value : defaultValue;
}

function parseChannels() {
  const raw = trim(env.ENABLED_CHANNELS);
  if (!raw) return [];
  if (raw.toLowerCase() === "all") return defaultOneClickChannels;

  const channels = [];
  for (const item of raw.split(",")) {
    const name = item.trim().toLowerCase();
    if (!name) continue;
    if (!knownChannels.has(name)) {
      throw new Error(`Unsupported channel in ENABLED_CHANNELS: ${name}`);
    }
    if (!channels.includes(name)) channels.push(name);
  }
  return channels;
}

async function readJson(filePath, fallback = {}) {
  try {
    return JSON.parse(await readFile(filePath, "utf8"));
  } catch (error) {
    if (error && error.code === "ENOENT") return fallback;
    throw new Error(`Failed to read JSON ${filePath}: ${error.message}`);
  }
}

async function writeJson(filePath, value) {
  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function objectAt(root, key) {
  if (!isObject(root[key])) root[key] = {};
  return root[key];
}

function setIfMissing(target, key, value) {
  if (value === undefined || value === null || value === "") return;
  if (target[key] === undefined || target[key] === null || target[key] === "") {
    target[key] = value;
  }
}

function ensureDotCraftSection(config) {
  const dotcraft = objectAt(config, "dotcraft");
  setIfMissing(dotcraft, "wsUrl", `ws://127.0.0.1:${intEnv("APPSERVER_PORT", 9100)}/ws`);
  setIfMissing(dotcraft, "token", env.APPSERVER_TOKEN || "");
}

function requireField(errors, channel, filePath, label, value) {
  if (value === undefined || value === null || value === "") {
    errors.push(`${channel}: missing ${label} for ${filePath}`);
  }
}

function first(value, fallback) {
  const trimmed = trim(value);
  return trimmed || fallback;
}

function listenHost(name, legacyName, fallback) {
  return first(env[name], first(env[legacyName], fallback));
}

async function renderGlobalConfig() {
  const filePath = path.join(userCraftDir, "config.json");
  const config = await readJson(filePath);

  const providerId = first(env.DOTCRAFT_PROVIDER, first(env.DOTCRAFT_PROVIDER_ID, ""));
  const model = first(env.DOTCRAFT_MODEL, "");
  const apiKey = first(env.DOTCRAFT_API_KEY, "");

  if (providerId) {
    config.ProviderId = providerId;
    if (model) config.Model = model;

    const providers = objectAt(config, "Providers");
    const provider = objectAt(providers, providerId);
    provider.DisplayName = first(env.DOTCRAFT_PROVIDER_DISPLAY_NAME, providerId);
    provider.Protocol = first(env.DOTCRAFT_PROVIDER_PROTOCOL, "openai-chat-completions");
    if (apiKey) provider.ApiKey = "$DOTCRAFT_API_KEY";
    if (trim(env.DOTCRAFT_PROVIDER_ENDPOINT)) provider.EndPoint = trim(env.DOTCRAFT_PROVIDER_ENDPOINT);
  }

  await writeJson(filePath, config);
}

async function renderWorkspaceConfig(enabledChannels) {
  const filePath = path.join(craftDir, "config.json");
  const config = await readJson(filePath);

  setIfMissing(config, "Language", first(env.DOTCRAFT_LANGUAGE, "English"));
  if (trim(env.DOTCRAFT_PROVIDER) || trim(env.DOTCRAFT_PROVIDER_ID)) {
    config.ProviderId = first(env.DOTCRAFT_PROVIDER, env.DOTCRAFT_PROVIDER_ID);
  }
  if (trim(env.DOTCRAFT_MODEL)) config.Model = trim(env.DOTCRAFT_MODEL);

  config.AppServer = {
    ...(isObject(config.AppServer) ? config.AppServer : {}),
    Mode: "WebSocket",
    WebSocket: {
      ...(isObject(config.AppServer?.WebSocket) ? config.AppServer.WebSocket : {}),
      Host: listenHost("APPSERVER_LISTEN_HOST", "APPSERVER_HOST", "0.0.0.0"),
      Port: intEnv("APPSERVER_PORT", 9100),
      Token: env.APPSERVER_TOKEN || "",
    },
  };

  config.DashBoard = {
    ...(isObject(config.DashBoard) ? config.DashBoard : {}),
    Enabled: boolEnv("DASHBOARD_ENABLED", true),
    Host: listenHost("DASHBOARD_LISTEN_HOST", "DASHBOARD_HOST", "0.0.0.0"),
    Port: intEnv("DASHBOARD_PORT", 8080),
  };

  const tools = objectAt(config, "Tools");
  tools.Sandbox = {
    ...(isObject(tools.Sandbox) ? tools.Sandbox : {}),
    Enabled: boolEnv("SANDBOX_ENABLED", false),
    Domain: first(env.SANDBOX_DOMAIN, "opensandbox:5880"),
    UseHttps: boolEnv("SANDBOX_USE_HTTPS", false),
    Image: first(env.SANDBOX_IMAGE, "ubuntu:latest"),
    NetworkPolicy: first(env.SANDBOX_NETWORK_POLICY, "allow"),
    SyncWorkspace: boolEnv("SANDBOX_SYNC_WORKSPACE", true),
  };

  const externalChannels = objectAt(config, "ExternalChannels");
  for (const name of knownChannels) {
    if (enabledChannels.includes(name)) {
      externalChannels[name] = {
        ...(isObject(externalChannels[name]) ? externalChannels[name] : {}),
        enabled: true,
        transport: "managedWebsocket",
        builtinModule: `channel-${name}`,
      };
    } else if (isObject(externalChannels[name])) {
      externalChannels[name].enabled = false;
    }
  }

  await writeJson(filePath, config);
}

async function renderTelegram(errors) {
  const filePath = path.join(craftDir, "telegram.json");
  const config = await readJson(filePath);
  ensureDotCraftSection(config);
  const telegram = objectAt(config, "telegram");
  setIfMissing(telegram, "botToken", trim(env.TELEGRAM_BOT_TOKEN));
  requireField(errors, "telegram", filePath, "TELEGRAM_BOT_TOKEN / telegram.botToken", telegram.botToken);
  await writeJson(filePath, config);
}

async function renderFeishu(errors) {
  const filePath = path.join(craftDir, "feishu.json");
  const config = await readJson(filePath);
  ensureDotCraftSection(config);
  const feishu = objectAt(config, "feishu");
  setIfMissing(feishu, "appId", trim(env.FEISHU_APP_ID));
  setIfMissing(feishu, "appSecret", trim(env.FEISHU_APP_SECRET));
  requireField(errors, "feishu", filePath, "FEISHU_APP_ID / feishu.appId", feishu.appId);
  requireField(errors, "feishu", filePath, "FEISHU_APP_SECRET / feishu.appSecret", feishu.appSecret);
  await writeJson(filePath, config);
}

async function renderQq() {
  const filePath = path.join(craftDir, "qq.json");
  const config = await readJson(filePath);
  ensureDotCraftSection(config);
  const qq = objectAt(config, "qq");
  setIfMissing(qq, "host", listenHost("QQ_LISTEN_HOST", "QQ_HOST", "0.0.0.0"));
  setIfMissing(qq, "port", intEnv("QQ_PORT", 6700));
  setIfMissing(qq, "accessToken", trim(env.QQ_ACCESS_TOKEN));
  await writeJson(filePath, config);

  if (!trim(qq.accessToken)) {
    console.warn("WARNING: QQ is enabled without QQ_ACCESS_TOKEN; configure NapCat access carefully.");
  }
}

async function renderWeCom(errors) {
  const filePath = path.join(craftDir, "wecom.json");
  const config = await readJson(filePath);
  ensureDotCraftSection(config);
  const wecom = objectAt(config, "wecom");
  setIfMissing(wecom, "host", listenHost("WECOM_LISTEN_HOST", "WECOM_HOST", "0.0.0.0"));
  setIfMissing(wecom, "port", intEnv("WECOM_PORT", 9000));
  setIfMissing(wecom, "scheme", first(env.WECOM_SCHEME, "http"));

  if (!Array.isArray(wecom.robots) || wecom.robots.length === 0) {
    const token = trim(env.WECOM_ROBOT_TOKEN);
    const aesKey = trim(env.WECOM_ROBOT_AES_KEY);
    const robotPath = first(env.WECOM_ROBOT_PATH, "/dotcraft");
    if (token || aesKey) {
      wecom.robots = [{ path: robotPath, token, aesKey }];
    }
  }

  if (!Array.isArray(wecom.robots) || wecom.robots.length === 0) {
    errors.push(`wecom: missing WECOM_ROBOT_TOKEN/WECOM_ROBOT_AES_KEY or wecom.robots in ${filePath}`);
  } else {
    for (const [index, robot] of wecom.robots.entries()) {
      if (!isObject(robot) || !trim(robot.path) || !trim(robot.token) || !trim(robot.aesKey)) {
        errors.push(`wecom: invalid wecom.robots[${index}] in ${filePath}; path, token, and aesKey are required`);
      }
    }
  }

  await writeJson(filePath, config);
}

async function renderWeixin() {
  const filePath = path.join(craftDir, "weixin.json");
  const config = await readJson(filePath);
  ensureDotCraftSection(config);
  const weixin = objectAt(config, "weixin");
  setIfMissing(weixin, "apiBaseUrl", first(env.WEIXIN_API_BASE_URL, "https://ilinkai.weixin.qq.com"));
  await writeJson(filePath, config);
  console.warn("WARNING: Weixin requires interactive QR login; check /workspace/.craft/tmp after startup.");
}

async function main() {
  await mkdir(craftDir, { recursive: true });
  await mkdir(userCraftDir, { recursive: true });

  const enabledChannels = parseChannels();
  await renderGlobalConfig();
  await renderWorkspaceConfig(enabledChannels);

  const errors = [];
  for (const channel of enabledChannels) {
    if (channel === "telegram") await renderTelegram(errors);
    if (channel === "feishu") await renderFeishu(errors);
    if (channel === "qq") await renderQq(errors);
    if (channel === "wecom") await renderWeCom(errors);
    if (channel === "weixin") await renderWeixin(errors);
  }

  if (errors.length > 0) {
    throw new Error(`Invalid DotCraft channel configuration:\n- ${errors.join("\n- ")}`);
  }

  console.log(
    enabledChannels.length > 0
      ? `Enabled DotCraft channels: ${enabledChannels.join(", ")}`
      : "No DotCraft channels enabled. Set ENABLED_CHANNELS to enable adapters.",
  );
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
