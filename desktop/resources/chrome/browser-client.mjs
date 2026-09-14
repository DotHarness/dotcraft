import { ChromeHostClient, ChromeTabsApi, ChromeUserApi, EmptyCapabilityCollection, ChromeTab } from './browser-api.mjs';

class ChromeBrowser {
  constructor(client) {
    this.client = client;
    this.tabs = new ChromeTabsApi(this);
    this.user = new ChromeUserApi(this);
    this.capabilities = new EmptyCapabilityCollection();
  }

  async nameSession(name) {
    return await this.client.request("browser.nameSession", { name });
  }

  async documentation() {
    return JSON.stringify({ ...this.describeApi(), tab: ChromeTab.prototype.describeApi() }, null, 2);
  }

  describeApi() {
    return {
      browser: ["documentation()", "nameSession(name)", "tabs", "user", "capabilities.list()"],
      tabs: this.tabs.describeApi(),
      user: this.user.describeApi(),
    };
  }
}

const chromeClients = new Set();

export async function setupBrowserRuntime(options = {}) {
  const globals = options.globals || globalThis;
  const backend = options.backend || "extension";

  if (backend !== "extension") {
    const fallbackPath = options.browserClientPath || globals.dotcraft?.browserClientPath;
    if (!fallbackPath) {
      throw new Error("No Browser client path is available for non-extension backend.");
    }
    const fallback = await import(fallbackPath);
    return await fallback.setupBrowserRuntime(options);
  }

  const bridgeOptions = options.chromeHost || options.chromeBridge || {};
  const browserSessionProvider = () => globals.dotcraft?.browserSession || options.browserSession || bridgeOptions.browserSession || null;
  const cancelHook = async (evaluationId, reason) => {
    await Promise.all([...chromeClients].map((client) => client.cancelEvaluation?.(evaluationId, reason)));
  };
  if (typeof globals.__dotcraftSetChromeCancelHook === "function") {
    globals.__dotcraftSetChromeCancelHook(cancelHook);
  }
  const agent = { browsers: {
    async list() {
      return [{ id: "extension", name: "DotCraft Chrome", type: "extension" }];
    },
    async get(name = "extension") {
      if (name === "extension" || name === "chrome") {
        const client = new ChromeHostClient({
          ...bridgeOptions,
          browserSessionProvider,
          logger: globals.console || console,
        });
        await client.ensureConnected();
        chromeClients.add(client);
        return new ChromeBrowser(client);
      }
      throw new Error(`Unsupported browser backend: ${name}`);
    },
    describeApi: () => [
      'get("extension")',
      'get("chrome")',
    ],
  } };

  return agent;
}
