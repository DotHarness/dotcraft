import fs from "node:fs/promises";
import net from "node:net";
import os from "node:os";
import path from "node:path";

const DEFAULT_TIMEOUT_MS = 15000;
const MAX_COMMAND_TIMEOUT_MS = 120000;
const DEFAULT_EVALUATE_MAX_BYTES = 1024 * 1024;
const PIPE_PREFIX = "dotcraft-chrome-";
const BRIDGE_UNAVAILABLE_MESSAGE =
  "Chrome extension backend is not connected; click the DotCraft Chrome extension after installing the native host manifest, then retry.";

class ChromeRuntimeError extends Error {
  constructor(category, message, options = {}) {
    super(`${category}: ${message}`);
    this.name = "ChromeRuntimeError";
    this.category = category;
    if (options.cause) this.cause = options.cause;
    if (options.code) this.code = options.code;
  }
}

function chromeError(category, message, options = {}) {
  return new ChromeRuntimeError(category, message, options);
}

function normalizeBridgeError(error) {
  const code = error?.code;
  if (code === "ECONNREFUSED" || code === "ECONNRESET" || code === "EPIPE" || code === "ETIMEDOUT") {
    return chromeError("BridgeDisconnected", BRIDGE_UNAVAILABLE_MESSAGE, { cause: error, code });
  }
  return error;
}

function normalizeRemoteError(error) {
  const message = error?.message || String(error || "Chrome extension command failed.");
  const categoryMatch = /^(BridgeDisconnected|CommandTimeout|CommandCancelled|ResultTooLarge|UnsupportedApi|DebuggerUnavailable|SessionMetadataMissing):\s*(.*)$/s.exec(message);
  if (categoryMatch) {
    return chromeError(categoryMatch[1], categoryMatch[2], { cause: error });
  }
  if (message.includes("timed out")) {
    return chromeError("CommandTimeout", message, { cause: error });
  }
  return new Error(message);
}

function tabReference(tab) {
  if (!tab || typeof tab !== "object") {
    return tab;
  }
  if (tab.info && typeof tab.info === "object") {
    return tabReference(tab.info);
  }
  return {
    id: tab.id,
    tabId: tab.tabId ?? tab.id,
    windowId: tab.windowId,
    title: tab.title,
    url: tab.url,
    active: tab.active,
    index: tab.index,
    claimed: tab.claimed === true,
    loading: tab.loading === true,
  };
}

function normalizeFinalizeOptions(options = {}) {
  const keep = options.keep ?? [];
  if (!Array.isArray(keep)) {
    throw new Error("browser.tabs.finalize({ keep }) requires keep to be an array of { tab, status } entries.");
  }
  return {
    ...options,
    keep: keep.map((item) => {
      if (!item || typeof item !== "object" || Array.isArray(item)) {
        throw new Error("browser.tabs.finalize keep entries must be objects shaped like { tab, status }.");
      }
      const status = item.status;
      if (status !== "handoff" && status !== "deliverable") {
        throw new Error('browser.tabs.finalize keep status must be "handoff" or "deliverable".');
      }
      return {
        status,
        tab: tabReference(item.tab),
      };
    }),
  };
}

function truncateContent(value, maxLength) {
  if (typeof value !== "string" || typeof maxLength !== "number" || maxLength < 0) {
    return value;
  }
  return value.length > maxLength ? value.slice(0, maxLength) : value;
}

function unsupportedApi(name) {
  throw chromeError("UnsupportedApi", `DotCraft Chrome does not support ${name} yet. Use the documented Chrome compatibility subset or ask the user before choosing another browser-control path.`);
}

function clampTimeoutMs(value, fallback = DEFAULT_TIMEOUT_MS) {
  const candidate = Number(value);
  if (!Number.isFinite(candidate) || candidate <= 0) return fallback;
  return Math.max(1, Math.min(Math.floor(candidate), MAX_COMMAND_TIMEOUT_MS));
}

function requestTimeoutFrom(options = {}, fallback = DEFAULT_TIMEOUT_MS) {
  return clampTimeoutMs(options?.timeoutMs, fallback);
}

function normalizeEvaluateMaxBytes(value) {
  const candidate = Number(value);
  if (!Number.isFinite(candidate) || candidate < 0) return DEFAULT_EVALUATE_MAX_BYTES;
  return Math.min(Math.floor(candidate), DEFAULT_EVALUATE_MAX_BYTES);
}

function serializedSizeBytes(value) {
  const json = JSON.stringify(value);
  const text = json === undefined ? String(value) : json;
  return Buffer.byteLength(text, "utf8");
}

function assertSerializedSize(value, maxBytes, operation) {
  const actualBytes = serializedSizeBytes(value);
  if (actualBytes > maxBytes) {
    throw chromeError(
      "ResultTooLarge",
      `${operation} result exceeded ${maxBytes} bytes; actual approximately ${actualBytes} bytes. Narrow the query, use maxLength, or fetch smaller chunks.`
    );
  }
}

export function encodeChromeHostFrame(message) {
  const body = Buffer.from(JSON.stringify(message), "utf8");
  const header = Buffer.alloc(4);
  header.writeUInt32LE(body.length, 0);
  return Buffer.concat([header, body]);
}

export class ChromeHostFrameDecoder {
  constructor() {
    this.buffer = Buffer.alloc(0);
  }

  push(chunk) {
    this.buffer = Buffer.concat([this.buffer, Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)]);
    const frames = [];
    while (this.buffer.length >= 4) {
      const length = this.buffer.readUInt32LE(0);
      if (this.buffer.length < length + 4) break;
      const body = this.buffer.subarray(4, 4 + length);
      this.buffer = this.buffer.subarray(4 + length);
      frames.push(JSON.parse(body.toString("utf8")));
    }
    return frames;
  }
}

async function defaultPipeCandidates() {
  if (process.platform === "win32") {
    try {
      const names = await fs.readdir("\\\\.\\pipe\\");
      return names
        .filter((name) => name.startsWith(PIPE_PREFIX))
        .map((name) => `\\\\.\\pipe\\${name}`);
    } catch {
      return [];
    }
  }

  const roots = [os.tmpdir()];
  const candidates = [];
  for (const root of roots) {
    try {
      const names = await fs.readdir(root);
      for (const name of names) {
        if (name.startsWith(PIPE_PREFIX) && name.endsWith(".sock")) {
          candidates.push(path.join(root, name));
        }
      }
    } catch {
      // Ignore inaccessible temp roots.
    }
  }
  return candidates;
}

function validateBrowserSession(browserSession) {
  if (!browserSession?.sessionId || !browserSession?.turnId || !browserSession?.evaluationId) {
    throw chromeError(
      "SessionMetadataMissing",
      "Chrome command requires browserSession.sessionId, browserSession.turnId, and browserSession.evaluationId."
    );
  }
}

function newCommandId() {
  return `chrome-command-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

function errorCategory(error) {
  const message = error?.message || String(error || "");
  return /^([A-Za-z]+):/.exec(message)?.[1] || error?.category || undefined;
}

function normalizeWaitForLoadStateArgs(stateOrOptions = "load", options = {}) {
  if (stateOrOptions && typeof stateOrOptions === "object") {
    return {
      state: stateOrOptions.state || "load",
      options: stateOrOptions,
    };
  }
  return {
    state: stateOrOptions || "load",
    options,
  };
}

class EmptyCapabilityCollection {
  async list() {
    return [];
  }

  async get(id) {
    unsupportedApi(`capability "${id}"`);
  }
}

class ChromeFileChooser {
  constructor(tab, info = {}) {
    this.tab = tab;
    this.info = info;
  }

  async isMultiple() {
    return await this.tab.client.request("tab.fileChooserIsMultiple", {
      tab: tabReference(this.tab.info),
      fileChooser: this.info,
    });
  }

  async setFiles(files) {
    const paths = Array.isArray(files) ? files : [files];
    return await this.tab.client.request("tab.fileChooserSetFiles", {
      tab: tabReference(this.tab.info),
      fileChooser: this.info,
      files: paths.map((file) => String(file)),
    });
  }

  describeApi() {
    return ["isMultiple()", "setFiles(files)"];
  }
}

class ChromeCuaApi {
  constructor(tab) {
    this.tab = tab;
  }

  async get_visible_screenshot(options = {}) {
    return await this.tab.screenshot(options);
  }

  async click(options = {}) {
    return await this.action("click", options);
  }

  async double_click(options = {}) {
    return await this.action("double_click", options);
  }

  async scroll(options = {}) {
    return await this.action("scroll", options);
  }

  async type(textOrOptions = {}) {
    const options = typeof textOrOptions === "string" ? { text: textOrOptions } : textOrOptions;
    return await this.action("type", options);
  }

  async keypress(keyOrOptions = {}) {
    const options = typeof keyOrOptions === "string" || Array.isArray(keyOrOptions)
      ? { keys: Array.isArray(keyOrOptions) ? keyOrOptions : [keyOrOptions] }
      : keyOrOptions;
    return await this.action("keypress", options);
  }

  async move(options = {}) {
    return await this.action("move", options);
  }

  async drag() {
    unsupportedApi("tab.cua.drag()");
  }

  async action(action, options) {
    return await this.tab.client.request("cua.action", {
      tab: tabReference(this.tab.info),
      action,
      options,
    });
  }

  describeApi() {
    return ["get_visible_screenshot()", "click(options)", "double_click(options)", "scroll(options)", "type(options)", "keypress(options)", "move(options)", "drag() unsupported"];
  }
}

class ChromeDomCuaApi {
  constructor(tab) {
    this.tab = tab;
  }

  async get_visible_dom(options = {}) {
    return await this.tab.client.request("domCua.visibleDom", {
      tab: tabReference(this.tab.info),
      options,
    });
  }

  async click(options = {}) {
    return await this.action("click", options);
  }

  async double_click(options = {}) {
    return await this.action("double_click", options);
  }

  async scroll(options = {}) {
    return await this.action("scroll", options);
  }

  async type(options = {}) {
    return await this.action("type", options);
  }

  async keypress(options = {}) {
    return await this.action("keypress", options);
  }

  async action(action, options) {
    return await this.tab.client.request("domCua.action", {
      tab: tabReference(this.tab.info),
      action,
      options,
    });
  }

  describeApi() {
    return ["get_visible_dom()", "click(options)", "double_click(options)", "scroll(options)", "type(options)", "keypress(options)"];
  }
}

class ChromeHostClient {
  constructor(options = {}) {
    this.pipePaths = Array.isArray(options.pipePaths) ? options.pipePaths : null;
    this.listPipes = typeof options.listPipes === "function" ? options.listPipes : defaultPipeCandidates;
    this.connectPipe = typeof options.connect === "function" ? options.connect : null;
    this.timeoutMs = options.timeoutMs || DEFAULT_TIMEOUT_MS;
    this.discoveryTimeoutMs = options.discoveryTimeoutMs || DEFAULT_TIMEOUT_MS;
    this.socket = null;
    this.decoder = new ChromeHostFrameDecoder();
    this.nextId = 1;
    this.pending = new Map();
    this.browserSession = options.browserSession || null;
    this.browserSessionProvider = typeof options.browserSessionProvider === "function"
      ? options.browserSessionProvider
      : null;
    this.logger = options.logger || globalThis.console;
    this.diagnostics = {
      pipeCandidateCount: 0,
      backendCount: 0,
      connectFailures: [],
      reconnectCount: 0,
    };
  }

  async ensureConnected() {
    if (this.socket && !this.socket.destroyed) {
      return;
    }

    const candidates = this.pipePaths || await this.listPipes();
    this.diagnostics.pipeCandidateCount = candidates.length;
    this.diagnostics.connectFailures = [];
    for (const pipePath of candidates) {
      try {
        const socket = await this.openPipe(pipePath);
        this.socket = socket;
        this.decoder = new ChromeHostFrameDecoder();
        socket.on("data", (chunk) => this.handleData(chunk));
        socket.on("close", () => this.handleClose());
        socket.on("error", (error) => this.handleClose(normalizeBridgeError(error)));
        const info = await this.requestInfo();
        if (info?.protocolVersion === 3 && info?.backendId) {
          this.diagnostics.backendCount += 1;
          return;
        }
        socket.destroy();
        this.socket = null;
      } catch (error) {
        this.diagnostics.connectFailures.push({
          pipePath: String(pipePath),
          error: error instanceof Error ? error.message : String(error),
        });
        if (this.socket) {
          this.socket.destroy();
          this.socket = null;
        }
      }
    }
    throw chromeError("BridgeDisconnected", BRIDGE_UNAVAILABLE_MESSAGE);
  }

  openPipe(pipePath) {
    return new Promise((resolve, reject) => {
      const socket = this.connectPipe ? this.connectPipe(pipePath) : net.createConnection({ path: pipePath });
      let settled = false;
      const timer = setTimeout(() => {
        socket.destroy();
        if (!settled) {
          settled = true;
          reject(normalizeBridgeError(Object.assign(new Error("Chrome host pipe connection timed out."), { code: "ETIMEDOUT" })));
        }
      }, this.discoveryTimeoutMs);
      socket.on("connect", () => {
        clearTimeout(timer);
        if (settled) return;
        settled = true;
        resolve(socket);
      });
      socket.on("error", (error) => {
        clearTimeout(timer);
        if (!settled) {
          settled = true;
          reject(normalizeBridgeError(error));
        }
      });
    });
  }

  requestInfo() {
    return this.sendEnvelope({ kind: "command", method: "getInfo", params: {}, timeoutMs: this.timeoutMs }, {
      timeoutMs: this.timeoutMs,
      requireSession: false,
      method: "getInfo",
    });
  }

  handleData(chunk) {
    let messages;
    try {
      messages = this.decoder.push(chunk);
    } catch (error) {
      console.warn("[DotCraft Chrome] Ignoring invalid host frame", error);
      return;
    }
    for (const message of messages) {
      this.handleMessage(message);
    }
  }

  handleMessage(message) {
    if (message?.kind === "event") {
      return;
    }
    const pending = this.pending.get(message.id);
    if (!pending) {
      return;
    }
    this.pending.delete(message.id);
    clearTimeout(pending.timer);
    if (message.ok === false || message.error) {
      pending.reject(normalizeRemoteError(message.error));
    } else {
      pending.resolve(message.result);
    }
  }

  handleClose(error) {
    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.removeAllListeners();
    }
    const closeError = error || chromeError("BridgeDisconnected", "Chrome backend connection closed.");
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(closeError);
    }
    this.pending.clear();
  }

  currentBrowserSession() {
    const provided = this.browserSessionProvider?.();
    return provided || this.browserSession || null;
  }

  async request(method, params = {}, options = {}) {
    await this.ensureConnected();
    const timeoutMs = requestTimeoutFrom(options, this.timeoutMs);
    const browserSession = this.currentBrowserSession();
    validateBrowserSession(browserSession);
    const commandId = newCommandId();
    return await this.sendEnvelope({
      kind: "command",
      commandId,
      method,
      params,
      browserSession,
      timeoutMs,
    }, { method, timeoutMs, commandId, browserSession });
  }

  sendEnvelope(envelope, options = {}) {
    const id = this.nextId++;
    const payload = encodeChromeHostFrame({ id, ...envelope });
    const timeoutMs = requestTimeoutFrom(options, this.timeoutMs);

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        if (options.commandId) {
          this.sendCancel(options.commandId, options.browserSession, "timeout");
        }
        const error = chromeError("CommandTimeout", `Chrome bridge request timed out: ${options.method || envelope.method} after ${timeoutMs}ms.`);
        this.logDiagnostic(options, "timeout", error, timeoutMs);
        reject(error);
      }, timeoutMs);

      this.pending.set(id, {
        resolve: (value) => {
          this.logDiagnostic(options, "ok", null, timeoutMs);
          resolve(value);
        },
        reject: (error) => {
          this.logDiagnostic(options, "error", error, timeoutMs);
          reject(error);
        },
        timer,
        commandId: options.commandId,
        browserSession: options.browserSession,
        startedAt: Date.now(),
      });
      this.socket.write(payload, (error) => {
        if (error) {
          clearTimeout(timer);
          this.pending.delete(id);
          reject(normalizeBridgeError(error));
        }
      });
    });
  }

  sendCancel(commandId, browserSession, reason) {
    if (!this.socket || this.socket.destroyed) return;
    const id = this.nextId++;
    const payload = encodeChromeHostFrame({
      id,
      kind: "cancel",
      commandId,
      browserSession,
      reason,
    });
    this.socket.write(payload, () => undefined);
  }

  async cancelEvaluation(evaluationId, reason = "cancelled") {
    const matches = [...this.pending.values()]
      .filter((pending) => pending.commandId && pending.browserSession?.evaluationId === evaluationId);
    for (const pending of matches) {
      pending.cancelled = true;
      this.sendCancel(pending.commandId, pending.browserSession, reason);
    }
  }

  logDiagnostic(options, status, error, timeoutMs) {
    const session = options.browserSession;
    if (!session || !options.commandId || !this.logger?.warn) return;
    if (status === "ok") return;
    this.logger.warn("[DotCraft Chrome diagnostic]", JSON.stringify({
      sessionId: session.sessionId,
      turnId: session.turnId,
      evaluationId: session.evaluationId,
      commandId: options.commandId,
      backendId: "chrome-extension",
      method: options.method,
      status,
      timeoutMs,
      cancelled: status === "cancelled",
      errorCategory: errorCategory(error),
      pipeCandidateCount: this.diagnostics.pipeCandidateCount,
      reconnectCount: this.diagnostics.reconnectCount,
    }));
  }

  close() {
    if (this.socket) {
      this.socket.end();
      this.socket.destroy();
    }
    this.handleClose();
  }
}

class ChromeLocator {
  constructor(tab, selector, options = {}) {
    this.tab = tab;
    this.selector = selector;
    this.options = options;
  }

  first() {
    return new ChromeLocator(this.tab, this.selector, { ...this.options, nth: 0 });
  }

  last() {
    return new ChromeLocator(this.tab, this.selector, { ...this.options, nth: -1 });
  }

  nth(index) {
    return new ChromeLocator(this.tab, this.selector, { ...this.options, nth: index });
  }

  locator(selector, options = {}) {
    return new ChromeLocator(this.tab, selector, options);
  }

  getByText(text, options = {}) {
    return this.tab.getByText(text, options);
  }

  getByRole(role, options = {}) {
    return this.tab.getByRole(role, options);
  }

  getByLabel(text, options = {}) {
    return this.tab.getByLabel(text, options);
  }

  getByPlaceholder(text, options = {}) {
    return this.tab.getByPlaceholder(text, options);
  }

  getByTestId(testId, options = {}) {
    return this.tab.getByTestId(testId, options);
  }

  async action(action, value, options = {}) {
    return await this.tab.client.request("locator.action", {
      tab: tabReference(this.tab.info),
      selector: this.selector,
      selectorOptions: this.options,
      action,
      value,
      options,
    }, options);
  }

  async count(options = {}) {
    return await this.action("count", undefined, options);
  }

  async click(options = {}) {
    return await this.action("click", undefined, options);
  }

  async dblclick(options = {}) {
    return await this.action("dblclick", undefined, options);
  }

  async fill(value, options = {}) {
    return await this.action("fill", value, options);
  }

  async type(value, options = {}) {
    return await this.action("type", value, options);
  }

  async press(key, options = {}) {
    return await this.action("press", key, options);
  }

  async textContent(options = {}) {
    return await this.action("textContent", undefined, options);
  }

  async innerText(options = {}) {
    return await this.action("innerText", undefined, options);
  }

  async getAttribute(name, options = {}) {
    return await this.action("getAttribute", name, options);
  }

  async isVisible(options = {}) {
    return await this.action("isVisible", undefined, options);
  }

  async isEnabled(options = {}) {
    return await this.action("isEnabled", undefined, options);
  }

  async waitFor(options = {}) {
    return await this.action("waitFor", undefined, options);
  }

  async check(options = {}) {
    return await this.action("check", true, options);
  }

  async uncheck(options = {}) {
    return await this.action("check", false, options);
  }

  async setChecked(checked, options = {}) {
    return await this.action("check", Boolean(checked), options);
  }

  async selectOption(value, options = {}) {
    return await this.action("selectOption", value, options);
  }

  async allTextContents(options = {}) {
    return await this.action("allTextContents", undefined, options);
  }

  all() {
    unsupportedApi("locator.all()");
  }

  filter() {
    unsupportedApi("locator.filter()");
  }

  and() {
    unsupportedApi("locator.and()");
  }

  or() {
    unsupportedApi("locator.or()");
  }

  describeApi() {
    return [
      "count()",
      "click()",
      "dblclick()",
      "fill(value)",
      "type(value)",
      "press(key)",
      "textContent()",
      "innerText()",
      "getAttribute(name)",
      "isVisible()",
      "isEnabled()",
      "waitFor(options)",
      "check(options)",
      "uncheck(options)",
      "setChecked(checked, options)",
      "selectOption(value, options)",
      "allTextContents(options)",
      "first()",
      "last()",
      "nth(index)",
    ];
  }
}

class ChromeContentApi {
  constructor(tab) {
    this.tab = tab;
  }

  async text(options = {}) {
    const value = await this.tab.client.request("tab.contentText", { tab: tabReference(this.tab.info), ...options }, options);
    return truncateContent(value, options.maxLength);
  }

  async html(options = {}) {
    const value = await this.tab.client.request("tab.contentHtml", { tab: tabReference(this.tab.info), ...options }, options);
    return truncateContent(value, options.maxLength);
  }

  async read(options = {}) {
    const contentType = options.contentType || options.type || "text";
    return contentType === "html" ? await this.html(options) : await this.text(options);
  }

  async get(options = {}) {
    return await this.read(options);
  }

  describeApi() {
    return ["text(options)", "html(options)", "read(options)", "get(options)"];
  }
}

class ChromePlaywrightApi {
  constructor(tab) {
    this.tab = tab;
  }

  locator(selector, options = {}) {
    return this.tab.locator(selector, options);
  }

  getByText(text, options = {}) {
    return this.tab.getByText(text, options);
  }

  getByRole(role, options = {}) {
    return this.tab.getByRole(role, options);
  }

  getByLabel(text, options = {}) {
    return this.tab.getByLabel(text, options);
  }

  getByPlaceholder(text, options = {}) {
    return this.tab.getByPlaceholder(text, options);
  }

  getByTestId(testId, options = {}) {
    return this.tab.getByTestId(testId, options);
  }

  async screenshot(options = {}) {
    return await this.tab.screenshot(options);
  }

  async domSnapshot(options = {}) {
    return await this.tab.domSnapshot(options);
  }

  async observe(options = {}) {
    return await this.tab.observe(options);
  }

  async innerText(selector, options = {}) {
    return await this.locator(selector, options).innerText();
  }

  async getAttribute(selector, name, options = {}) {
    return await this.locator(selector, options).getAttribute(name);
  }

  async isVisible(selector, options = {}) {
    return await this.locator(selector, options).isVisible();
  }

  async press(selector, key, options = {}) {
    return await this.locator(selector, options).press(key);
  }

  async waitFor(selector, options = {}) {
    return await this.locator(selector, options).waitFor(options);
  }

  async waitForLoadState(stateOrOptions = "load", options = {}) {
    const normalized = normalizeWaitForLoadStateArgs(stateOrOptions, options);
    return await this.tab.client.request("tab.waitForLoadState", {
      tab: tabReference(this.tab.info),
      state: normalized.state,
      options: normalized.options,
    }, normalized.options);
  }

  async waitForTimeout(ms) {
    await new Promise((resolve) => setTimeout(resolve, ms));
  }

  async waitForURL(url, options = {}) {
    return await this.tab.client.request("tab.waitForURL", {
      tab: tabReference(this.tab.info),
      url,
      options,
    }, options);
  }

  async expectNavigation(action, options = {}) {
    if (typeof action !== "function") {
      throw new Error("tab.playwright.expectNavigation(action, options) requires an async action function.");
    }
    const previousUrl = await this.tab.url();
    const result = await action();
    if (options.url) {
      await this.waitForURL(options.url, options);
    } else {
      await this.tab.client.request("tab.waitForNavigation", {
        tab: tabReference(this.tab.info),
        previousUrl,
        options,
      }, options);
    }
    if (options.waitUntil === "load") {
      await this.waitForLoadState("load", options);
    }
    return result;
  }

  async waitForEvent(event, options = {}) {
    if (event !== "filechooser") {
      unsupportedApi(`playwright.waitForEvent("${event}")`);
    }
    const info = await this.tab.client.request("tab.waitForFileChooser", {
      tab: tabReference(this.tab.info),
      options,
    }, options);
    return new ChromeFileChooser(this.tab, info);
  }

  frameLocator() {
    unsupportedApi("playwright.frameLocator()");
  }

  describeApi() {
    return [
      "locator(selector)",
      "getByText(text)",
      "getByRole(role)",
      "getByLabel(text)",
      "getByPlaceholder(text)",
      "getByTestId(testId)",
      "screenshot(options)",
      "domSnapshot(options)",
      "observe(options)",
      "waitForLoadState({ state, timeoutMs })",
      "waitForTimeout(ms)",
      "waitForURL(url)",
      "expectNavigation(action, options)",
      "waitForEvent(\"filechooser\", options)",
      "frameLocator(selector) unsupported",
      "innerText(selector)",
      "getAttribute(selector, name)",
      "isVisible(selector)",
      "press(selector, key)",
      "waitFor(selector)",
    ];
  }
}

class ChromeTab {
  constructor(browser, tab) {
    this.browser = browser;
    this.client = browser.client;
    this.info = tabReference(tab);
    this.content = new ChromeContentApi(this);
    this.playwright = new ChromePlaywrightApi(this);
    this.capabilities = new EmptyCapabilityCollection();
    this.clipboard = {
      read: async () => unsupportedApi("tab.clipboard.read()"),
      readText: async () => unsupportedApi("tab.clipboard.readText()"),
      write: async () => unsupportedApi("tab.clipboard.write()"),
      writeText: async () => unsupportedApi("tab.clipboard.writeText()"),
    };
    this.cua = new ChromeCuaApi(this);
    this.dom_cua = new ChromeDomCuaApi(this);
    this.dev = {
      logs: async () => [],
    };
  }

  async goto(url, options = {}) {
    const result = await this.client.request("tab.goto", { tab: tabReference(this.info), url, options }, options);
    this.info = tabReference(result || this.info);
    return this;
  }

  async reload(options = {}) {
    return await this.client.request("tab.reload", { tab: tabReference(this.info), options });
  }

  async back(options = {}) {
    return await this.client.request("tab.back", { tab: tabReference(this.info), options });
  }

  async forward(options = {}) {
    return await this.client.request("tab.forward", { tab: tabReference(this.info), options });
  }

  async close() {
    return await this.client.request("tab.close", { tab: tabReference(this.info) });
  }

  async title() {
    return await this.client.request("tab.title", { tab: tabReference(this.info) });
  }

  async url() {
    return await this.client.request("tab.url", { tab: tabReference(this.info) });
  }

  async screenshot(options = {}) {
    return await this.client.request("tab.screenshot", { tab: tabReference(this.info), options });
  }

  async evaluate(pageFunction, arg, options = {}) {
    const source = typeof pageFunction === "function" ? `return (${pageFunction.toString()})(arguments[0]);` : String(pageFunction);
    const maxBytes = normalizeEvaluateMaxBytes(options.maxBytes);
    const result = await this.client.request("tab.evaluate", {
      tab: tabReference(this.info),
      source,
      arg,
      maxBytes,
      timeoutMs: options.timeoutMs,
    }, options);
    assertSerializedSize(result, maxBytes, "tab.evaluate");
    return result;
  }

  async domSnapshot(options = {}) {
    return await this.client.request("tab.domSnapshot", { tab: tabReference(this.info), options });
  }

  async observe(options = {}) {
    const current = await this.client.request("tabs.get", { tab: tabReference(this.info) });
    this.info = tabReference(current || this.info);
    const observation = {
      tab: tabReference(this.info),
      url: this.info.url || "",
      title: this.info.title || "",
      loading: this.info.loading === true,
    };
    if (options.domSnapshot !== false) {
      try {
        observation.domSnapshot = await this.domSnapshot(options.domSnapshotOptions ?? {});
      } catch (error) {
        observation.domError = error instanceof Error ? error.message : String(error);
      }
    }
    if (options.screenshot === true) {
      try {
        observation.screenshot = await this.screenshot(options.screenshotOptions ?? {});
      } catch (error) {
        observation.screenshotError = error instanceof Error ? error.message : String(error);
      }
    }
    return observation;
  }

  locator(selector, options = {}) {
    return new ChromeLocator(this, selector, options);
  }

  getByText(text, options = {}) {
    return new ChromeLocator(this, text, { ...options, kind: "text", text });
  }

  getByRole(role, options = {}) {
    return new ChromeLocator(this, role, { ...options, kind: "role", role });
  }

  getByLabel(text, options = {}) {
    return new ChromeLocator(this, text, { ...options, kind: "label", text });
  }

  getByPlaceholder(text, options = {}) {
    return new ChromeLocator(this, text, { ...options, kind: "placeholder", text });
  }

  getByTestId(testId, options = {}) {
    return new ChromeLocator(this, testId, { ...options, kind: "testId", testId });
  }

  describeApi() {
    return [
      "goto(url)",
      "reload()",
      "back()",
      "forward()",
      "close()",
      "title()",
      "url()",
      "screenshot(options)",
      "evaluate(fn, arg)",
      "domSnapshot(options)",
      "observe(options)",
      "locator(selector)",
      "content.text(options)",
      "content.html(options)",
      "content.read(options)",
      "content.get(options)",
      "playwright.domSnapshot(options)",
      "capabilities.list() returns []",
      "clipboard unsupported with explicit errors",
      "cua basic coordinate API",
      "dom_cua basic DOM node API",
    ];
  }
}

class ChromeTabsApi {
  constructor(browser) {
    this.browser = browser;
  }

  async new(options = {}) {
    const tab = await this.browser.client.request("tabs.new", options, options);
    return new ChromeTab(this.browser, tab);
  }

  async selected(options = {}) {
    const tab = await this.browser.client.request("tabs.selected", options);
    return new ChromeTab(this.browser, tab);
  }

  async list(options = {}) {
    return await this.browser.client.request("tabs.list", options);
  }

  async get(tab) {
    const result = await this.browser.client.request("tabs.get", { tab: tabReference(tab) });
    return new ChromeTab(this.browser, result);
  }

  async content(options = {}) {
    if (Array.isArray(options.urls) && options.urls.length > 0) {
      return await this.browser.client.request("tabs.content", options, options);
    }
    const tab = await this.selected(options);
    return await tab.content.read(options);
  }

  async read(options = {}) {
    return await this.content(options);
  }

  async finalize(options = {}) {
    return await this.browser.client.request("tabs.finalize", normalizeFinalizeOptions(options), options);
  }

  describeApi() {
    return ["new(options)", "selected(options)", "list(options)", "get(tab)", "content(options)", "read(options)", "finalize(options)"];
  }
}

class ChromeUserApi {
  constructor(browser) {
    this.browser = browser;
  }

  async openTabs(options = {}) {
    return await this.browser.client.request("user.openTabs", options);
  }

  async claimTab(tab, options = {}) {
    const result = await this.browser.client.request("user.claimTab", { tab: tabReference(tab), options });
    return new ChromeTab(this.browser, result);
  }

  async history(_options = {}) {
    throw new Error("Chrome history access is intentionally unavailable in DotCraft.");
  }

  describeApi() {
    return ["openTabs(options)", "claimTab(tab, options)", "history() intentionally unavailable"];
  }
}

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

  describeApi() {
    return {
      browser: ["nameSession(name)", "tabs", "user", "capabilities.list()"],
      tabs: this.tabs.describeApi(),
      user: this.user.describeApi(),
    };
  }
}

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
  const chromeClients = globals.__dotcraftChromeClients instanceof Set
    ? globals.__dotcraftChromeClients
    : new Set();
  globals.__dotcraftChromeClients = chromeClients;
  const cancelHook = async (evaluationId, reason) => {
    await Promise.all([...chromeClients].map((client) => client.cancelEvaluation?.(evaluationId, reason)));
  };
  if (typeof globals.__dotcraftSetChromeCancelHook === "function") {
    globals.__dotcraftSetChromeCancelHook(cancelHook);
  } else {
    globals.__dotcraftChromeCancelEvaluation = cancelHook;
  }
  globals.agent = globals.agent || {};
  const existingBrowsers = globals.agent.browsers;
  const existingList = typeof existingBrowsers?.list === "function"
    ? existingBrowsers.list.bind(existingBrowsers)
    : null;
  const existingGet = typeof existingBrowsers?.get === "function"
    ? existingBrowsers.get.bind(existingBrowsers)
    : null;
  const existingDescribeApi = typeof existingBrowsers?.describeApi === "function"
    ? existingBrowsers.describeApi.bind(existingBrowsers)
    : null;

  globals.agent.browsers = {
    async list() {
      const existing = existingList ? await existingList() : [];
      const items = Array.isArray(existing)
        ? existing.filter((item, index, all) =>
            !item?.id || all.findIndex((candidate) => candidate?.id === item.id) === index)
          .map((item) => item?.id === "iab"
            ? { ...item, name: "DotCraft Browser", type: "iab" }
            : item)
        : [];
      if (!items.some((item) => item?.id === "extension")) {
        items.push({ id: "extension", name: "DotCraft Chrome", type: "extension" });
      }
      return items;
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
      if (existingGet) {
        return await existingGet(name);
      }
      throw new Error(`Unsupported browser backend: ${name}`);
    },
    describeApi: () => [
      ...(existingDescribeApi ? existingDescribeApi() : []),
      'get("extension")',
      'get("chrome")',
    ],
  };

  return globals.agent;
}
