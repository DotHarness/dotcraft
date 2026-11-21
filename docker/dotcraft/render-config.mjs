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

function enumEnv(name, allowed) {
  const value = trim(env[name]);
  if (!value) return undefined;
  const canonical = allowed.find((item) => item.toLowerCase() === value.toLowerCase());
  if (!canonical) {
    throw new Error(`${name} must be one of: ${allowed.join(", ")}`);
  }
  return canonical;
}

function setProviderPreference(config, providerId) {
  if (!providerId) return;

  const model = trim(env.DOTCRAFT_MODEL);
  const effort = enumEnv(
    "DOTCRAFT_REASONING_EFFORT",
    ["off", "low", "medium", "high", "extraHigh"],
  );
  const output = enumEnv(
    "DOTCRAFT_REASONING_OUTPUT",
    ["none", "summary", "full"],
  );
  const speed = enumEnv("DOTCRAFT_SPEED", ["standard", "fast"]);
  const contextWindow = enumEnv(
    "DOTCRAFT_CONTEXT_WINDOW",
    ["default", "max"],
  );

  const preferences = objectAt(config, "ProviderPreferences");
  const existingKey = Object.keys(preferences).find(
    (key) => key.toLowerCase() === providerId.toLowerCase(),
  );
  const existing = existingKey && isObject(preferences[existingKey])
    ? preferences[existingKey]
    : undefined;
  if (!model && !existing) return;

  const preference = existing
    ? { ...existing }
    : {
        model,
        reasoning: { enabled: false, effort: "medium", output: "full" },
        speed: "standard",
        contextWindow: { mode: "default" },
      };
  if (model) preference.model = model;
  const reasoning = isObject(preference.reasoning)
    ? { ...preference.reasoning }
    : { enabled: false, effort: "medium", output: "full" };
  if (effort) {
    reasoning.enabled = effort !== "off";
    if (effort !== "off") reasoning.effort = effort;
  }
  if (output) reasoning.output = output;
  preference.reasoning = reasoning;
  if (speed) preference.speed = speed;
  const currentContext = isObject(preference.contextWindow)
    ? { ...preference.contextWindow }
    : { mode: "default" };
  if (contextWindow) currentContext.mode = contextWindow;
  preference.contextWindow = currentContext;
  preferences[existingKey ?? providerId] = preference;
  if (Object.keys(preferences).length === 0) delete config.ProviderPreferences;
}

function setIfMissing(target, key, value) {
  if (value === undefined || value === null || value === "") return;
  if (target[key] === undefined || target[key] === null || target[key] === "") {
    target[key] = value;
  }
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

function listenHost(name, fallback) {
  return first(env[name], fallback);
}

async function renderGlobalConfig(authMethod) {
  const filePath = path.join(userCraftDir, "config.json");
  const config = await readJson(filePath);

  const configuredProviderId = trim(env.DOTCRAFT_PROVIDER);
  const providerId = first(configuredProviderId, trim(config.ProviderId));
  const apiKey = first(env.DOTCRAFT_API_KEY, "");
  if (trim(env.DOTCRAFT_MODEL_MODE) !== "remote") delete config.ModelService;

  if (trim(env.DOTCRAFT_MODEL_MODE) === "remote") {
    config.ModelService = {
      Endpoint: trim(env.DOTCRAFT_MODEL_SERVICE_URL),
      Token: "$DOTCRAFT_MODEL_SERVICE_TOKEN",
    };
    delete config.Providers;
    if (configuredProviderId) config.ProviderId = configuredProviderId;
  } else if (configuredProviderId) {
    config.ProviderId = configuredProviderId;

    const providers = objectAt(config, "Providers");
    const provider = objectAt(providers, configuredProviderId);
    provider.DisplayName = first(env.DOTCRAFT_PROVIDER_DISPLAY_NAME, configuredProviderId);
    provider.AuthMethod = authMethod;
    provider.Protocol = authMethod === "chatgptOAuth"
      ? "openai-responses"
      : first(env.DOTCRAFT_PROVIDER_PROTOCOL, "openai-chat-completions");
    if (authMethod === "chatgptOAuth") {
      delete provider.ApiKey;
      delete provider.EndPoint;
    } else {
      if (apiKey) provider.ApiKey = "$DOTCRAFT_API_KEY";
      if (trim(env.DOTCRAFT_PROVIDER_ENDPOINT)) provider.EndPoint = trim(env.DOTCRAFT_PROVIDER_ENDPOINT);
    }
  }

  setProviderPreference(config, providerId);

  await writeJson(filePath, config);
}

async function renderWorkspaceConfig(enabledChannels) {
  const filePath = path.join(craftDir, "config.json");
  const config = await readJson(filePath);
  const configuredProviderId = trim(env.DOTCRAFT_PROVIDER);
  const providerId = first(configuredProviderId, trim(config.ProviderId));

  if (configuredProviderId) {
    config.ProviderId = configuredProviderId;
  }
  setProviderPreference(config, providerId);

  config.AppServer = {
    ...(isObject(config.AppServer) ? config.AppServer : {}),
    Mode: "WebSocket",
    WebSocket: {
      ...(isObject(config.AppServer?.WebSocket) ? config.AppServer.WebSocket : {}),
      Host: listenHost("APPSERVER_LISTEN_HOST", "0.0.0.0"),
      Port: intEnv("APPSERVER_PORT", 9100),
      Token: env.APPSERVER_TOKEN || "",
    },
  };

  config.DashBoard = {
    ...(isObject(config.DashBoard) ? config.DashBoard : {}),
    Enabled: boolEnv("DASHBOARD_ENABLED", true),
    Host: listenHost("DASHBOARD_LISTEN_HOST", "0.0.0.0"),
    Port: intEnv("DASHBOARD_PORT", 8080),
  };

  const active = Object.hasOwn(env, "ENABLED_CHANNELS")
    ? await configureManagedChannels(config, enabledChannels)
    : [];
  await writeJson(filePath, config);
  return active;
}

async function configureManagedChannels(config, enabledChannels) {
  const trackingPath = path.join(craftDir, "docker-channels.json");
  const previous = (await readJson(trackingPath))?.managed;
  const tracked = new Set(Array.isArray(previous) ? previous : []);
  const channels = objectAt(config, matchingKey(config, "Channels") ?? "Channels");
  const active = [];

  for (const name of knownChannels) {
    const pluginId = `channel-${name}`;
    const nameKey = matchingKey(channels, name) ?? name;
    const current = channels[nameKey];
    const selected = enabledChannels.includes(name);
    const dockerManaged = tracked.has(name) && isObject(current) &&
      current[matchingKey(current, "pluginId")] === pluginId;

    if (isObject(current) && !dockerManaged) {
      tracked.delete(name);
      if (selected) console.warn(`Keeping the existing ${name} channel registration.`);
      continue;
    }
    if (!selected && !dockerManaged) continue;

    const entry = { ...current };
    for (const field of ["pluginId", "enabled"]) {
      delete entry[matchingKey(entry, field)];
    }
    channels[nameKey] = { ...entry, enabled: selected, pluginId };
    tracked.add(name);
    if (selected) active.push(name);
  }
  if (tracked.size || previous?.length) {
    await writeJson(trackingPath, { managed: [...tracked].sort() });
  }
  return active;
}

function matchingKey(object, key) {
  const match = key.toLowerCase();
  return Object.keys(object).find((candidate) => candidate.toLowerCase() === match);
}

async function renderTelegram(errors) {
  const filePath = path.join(craftDir, "telegram.json");
  const config = await readJson(filePath);
  const telegram = objectAt(config, "telegram");
  setIfMissing(telegram, "botToken", trim(env.TELEGRAM_BOT_TOKEN));
  requireField(errors, "telegram", filePath, "TELEGRAM_BOT_TOKEN / telegram.botToken", telegram.botToken);
  await writeJson(filePath, config);
}

async function renderFeishu(errors) {
  const filePath = path.join(craftDir, "feishu.json");
  const config = await readJson(filePath);
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
  const qq = objectAt(config, "qq");
  setIfMissing(qq, "host", listenHost("QQ_LISTEN_HOST", "0.0.0.0"));
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
  const wecom = objectAt(config, "wecom");
  setIfMissing(wecom, "host", listenHost("WECOM_LISTEN_HOST", "0.0.0.0"));
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
  const weixin = objectAt(config, "weixin");
  setIfMissing(weixin, "apiBaseUrl", first(env.WEIXIN_API_BASE_URL, "https://ilinkai.weixin.qq.com"));
  await writeJson(filePath, config);
  console.warn("WARNING: Weixin requires interactive QR login; check /workspace/.craft/tmp after startup.");
}

async function main() {
  const mode = trim(env.DOTCRAFT_MODEL_MODE) || "direct";
  if (mode !== "direct" && mode !== "remote")
    throw new Error("DOTCRAFT_MODEL_MODE must be direct or remote.");
  const authMethod = trim(env.DOTCRAFT_AUTH_METHOD);
  if (mode === "remote") {
    for (const key of ["DOTCRAFT_MODEL_SERVICE_URL", "DOTCRAFT_MODEL_SERVICE_TOKEN", "DOTCRAFT_PROVIDER", "DOTCRAFT_MODEL"])
      if (!trim(env[key])) throw new Error(`${key} is required in remote model mode.`);
  } else if (authMethod !== "apiKey" && authMethod !== "chatgptOAuth")
    throw new Error("DOTCRAFT_AUTH_METHOD must be apiKey or chatgptOAuth.");
  if (mode === "direct" && authMethod === "chatgptOAuth") {
    if (!trim(env.DOTCRAFT_PROVIDER) || !trim(env.DOTCRAFT_MODEL))
      throw new Error("ChatGPT subscription mode requires DOTCRAFT_PROVIDER and DOTCRAFT_MODEL.");
  }
  await mkdir(craftDir, { recursive: true });
  await mkdir(userCraftDir, { recursive: true });

  const enabledChannels = parseChannels();
  await renderGlobalConfig(authMethod);
  const activeChannels = await renderWorkspaceConfig(enabledChannels);

  const errors = [];
  for (const channel of activeChannels) {
    try {
      if (channel === "telegram") await renderTelegram(errors);
      if (channel === "feishu") await renderFeishu(errors);
      if (channel === "qq") await renderQq(errors);
      if (channel === "wecom") await renderWeCom(errors);
      if (channel === "weixin") await renderWeixin(errors);
    } catch {
      errors.push(`${channel}: could not prepare its configuration. Check .craft/${channel}.json.`);
    }
  }

  if (errors.length > 0) {
    console.warn(`DotCraft channel configuration needs attention:\n- ${errors.join("\n- ")}`);
  }

  console.log(
    activeChannels.length > 0
      ? `Enabled deployment channels: ${activeChannels.join(", ")}`
      : "No deployment channels enabled through ENABLED_CHANNELS.",
  );
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
