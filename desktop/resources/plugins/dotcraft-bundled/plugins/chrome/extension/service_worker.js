const HOST_NAME = 'com.dotcraft.chromeextension';
const DEFAULT_EVALUATE_MAX_BYTES = 1024 * 1024;

let nativePort = null;
let nativeStatus = {
  connected: false,
  bridgeReady: false,
  error: null,
  pipePath: null,
  updatedAt: 0
};
const browserSessions = new Map();
const debuggerQueues = new Map();
const pendingCommands = new Map();

function delay(ms, command) {
  if (!command) return new Promise((resolve) => setTimeout(resolve, ms));
  return new Promise((resolve, reject) => {
    if (command.cancelled) {
      reject(cancelledError(command));
      return;
    }
    const timer = setTimeout(() => {
      command.timers.delete(timer);
      command.rejectors.delete(reject);
      resolve();
    }, ms);
    command.timers.add(timer);
    command.rejectors.add(reject);
  });
}

function classifiedError(category, message, options) {
  return new Error(`${category}: ${message}`, options);
}

function cancelledError(command) {
  return classifiedError('CommandCancelled', `Chrome command ${command.commandId} was cancelled: ${command.reason || 'cancelled'}.`);
}

function throwIfCancelled(command) {
  if (command?.cancelled) throw cancelledError(command);
}

function beginCommand(message) {
  const commandId = String(message?.commandId || message?.params?.commandId || `extension-command-${Date.now().toString(36)}`);
  const command = {
    commandId,
    cancelled: false,
    reason: null,
    timers: new Set(),
    rejectors: new Set(),
    startedAt: Date.now()
  };
  pendingCommands.set(commandId, command);
  return command;
}

function finishCommand(command) {
  if (!command) return;
  for (const timer of command.timers) clearTimeout(timer);
  command.timers.clear();
  command.rejectors.clear();
  pendingCommands.delete(command.commandId);
}

function cancelCommand(commandId, reason = 'cancelled') {
  const command = pendingCommands.get(String(commandId || ''));
  if (!command) return false;
  command.cancelled = true;
  command.reason = reason;
  for (const timer of command.timers) clearTimeout(timer);
  command.timers.clear();
  for (const reject of [...command.rejectors]) {
    try {
      reject(cancelledError(command));
    } catch {
      // Ignore rejector failures.
    }
  }
  command.rejectors.clear();
  return true;
}

function commandCancelPromise(command) {
  if (!command) return new Promise(() => {});
  return new Promise((_, reject) => {
    if (command.cancelled) {
      reject(cancelledError(command));
      return;
    }
    command.rejectors.add(reject);
  });
}

function browserSessionId(params) {
  return requireBrowserSession(params).sessionId;
}

function requireBrowserSession(params) {
  const session = params?.browserSession;
  if (!session?.sessionId || !session?.turnId || !session?.evaluationId) {
    throw classifiedError('SessionMetadataMissing', 'Chrome command requires browserSession.sessionId, browserSession.turnId, and browserSession.evaluationId.');
  }
  return session;
}

function getBrowserSession(params) {
  const browserSession = requireBrowserSession(params);
  const sessionId = String(browserSession.sessionId);
  let session = browserSessions.get(sessionId);
  if (!session) {
    session = {
      sessionId,
      turnId: browserSession.turnId || null,
      evaluationId: browserSession.evaluationId || null,
      sessionName: 'Chrome',
      claimedTabs: new Set(),
      createdTabs: new Set(),
      keptTabs: new Map(),
      openTabsSeenIds: new Set()
    };
    browserSessions.set(sessionId, session);
  }
  session.turnId = browserSession.turnId || session.turnId;
  session.evaluationId = browserSession.evaluationId || session.evaluationId;
  return session;
}

function connectNative() {
  if (nativePort) return nativePort;
  try {
    nativePort = chrome.runtime.connectNative(HOST_NAME);
  } catch (error) {
    nativeStatus = {
      connected: false,
      bridgeReady: false,
      error: error instanceof Error ? error.message : String(error),
      pipePath: null,
      updatedAt: Date.now()
    };
    throw error;
  }
  nativeStatus = {
    connected: true,
    bridgeReady: false,
    error: null,
    pipePath: null,
    updatedAt: Date.now()
  };
  nativePort.onMessage.addListener((message) => {
    if (message?.type === 'dotcraft-host-ready') {
      nativeStatus = {
        connected: true,
        bridgeReady: true,
        error: null,
        pipePath: message.pipePath ?? null,
        updatedAt: Date.now()
      };
      return;
    }
    if (message?.type === 'dotcraft-cancel') {
      cancelCommand(message.commandId, message.reason || 'cancelled');
      return;
    }
    if (message?.type === 'dotcraft-host-error') {
      nativeStatus = {
        connected: true,
        bridgeReady: false,
        error: message.error || 'Native host error.',
        pipePath: null,
        updatedAt: Date.now()
      };
      return;
    }
    if (message?.type === 'dotcraft-request') {
      void handleRequest(message);
    }
  });
  nativePort.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError?.message || null;
    if (error) console.warn(error);
    nativeStatus = {
      connected: false,
      bridgeReady: false,
      error,
      pipePath: null,
      updatedAt: Date.now()
    };
    nativePort = null;
  });
  return nativePort;
}

function popupStatus() {
  const manifest = chrome.runtime.getManifest();
  return {
    connected: nativeStatus.connected === true && nativeStatus.bridgeReady === true,
    nativeConnected: nativeStatus.connected,
    bridgeReady: nativeStatus.bridgeReady,
    error: nativeStatus.error,
    pipePath: nativeStatus.pipePath,
    updatedAt: nativeStatus.updatedAt,
    version: manifest.version
  };
}

function sendResponse(id, ok, result, error, commandId) {
  try {
    connectNative().postMessage({
      type: 'dotcraft-response',
      id,
      commandId,
      ok,
      result,
      error
    });
  } catch (err) {
    console.warn(err instanceof Error ? err.message : String(err));
  }
}

async function handleRequest(message) {
  const command = beginCommand(message);
  try {
    requireBrowserSession(message.params ?? {});
    const result = await dispatchCommand(message.method, message.params ?? {}, command);
    throwIfCancelled(command);
    sendResponse(message.id, true, result, null, command.commandId);
  } catch (error) {
    sendResponse(message.id, false, null, error instanceof Error ? error.message : String(error), command.commandId);
  } finally {
    finishCommand(command);
  }
}

function publicTab(tab, session = null) {
  return {
    id: tab.id,
    tabId: tab.id,
    windowId: tab.windowId,
    index: tab.index,
    title: tab.title ?? '',
    url: tab.url ?? '',
    active: Boolean(tab.active),
    loading: tab.status === 'loading',
    pinned: Boolean(tab.pinned),
    audible: Boolean(tab.audible),
    groupId: tab.groupId,
    claimed: session?.claimedTabs.has(tab.id) === true,
    createdByAgent: session?.createdTabs.has(tab.id) === true,
    keptStatus: session?.keptTabs.get(tab.id)
  };
}

async function getTab(tabId) {
  const id = Number(tabId);
  if (!Number.isInteger(id)) throw new Error('A valid Chrome tab id is required.');
  return chrome.tabs.get(id);
}

async function selectedTab() {
  const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  if (!tab?.id) throw new Error('No active Chrome tab is available.');
  return tab;
}

async function allTabs(session, rememberForClaim = false) {
  const tabs = await chrome.tabs.query({});
  const publicTabs = tabs.filter((tab) => typeof tab.id === 'number').map((tab) => publicTab(tab, session));
  if (rememberForClaim) {
    session.openTabsSeenIds = new Set(publicTabs.map((tab) => tab.id));
  }
  return publicTabs;
}

function tabIdFromParams(params) {
  const candidate = params?.tabId ?? params?.tab?.tabId ?? params?.tab?.id ?? params?.id ?? params?.tab;
  const id = Number(String(candidate ?? '').replace(/^chrome:/, ''));
  if (!Number.isInteger(id)) throw new Error('A valid Chrome tab id is required.');
  return id;
}

function tabIdFromReference(value) {
  const candidate = value?.tabId ?? value?.id ?? value;
  const id = Number(String(candidate ?? '').replace(/^chrome:/, ''));
  if (!Number.isInteger(id)) throw new Error('A valid Chrome tab id is required.');
  return id;
}

function parseFinalizeKeep(keep) {
  if (!Array.isArray(keep)) {
    throw new Error('browser.tabs.finalize requires keep to be an array of { tab, status } entries.');
  }
  const result = new Map();
  for (const entry of keep) {
    if (!entry || typeof entry !== 'object' || Array.isArray(entry)) {
      throw new Error('browser.tabs.finalize keep entries must be objects shaped like { tab, status }.');
    }
    if (entry.status !== 'handoff' && entry.status !== 'deliverable') {
      throw new Error('browser.tabs.finalize keep status must be "handoff" or "deliverable".');
    }
    result.set(tabIdFromReference(entry.tab), entry.status);
  }
  return result;
}

function truncateContent(value, maxLength) {
  if (typeof value !== 'string' || typeof maxLength !== 'number' || maxLength < 0) return value;
  return value.length > maxLength ? value.slice(0, maxLength) : value;
}

function normalizeEvaluateMaxBytes(value) {
  const candidate = Number(value);
  if (!Number.isFinite(candidate) || candidate < 0) return DEFAULT_EVALUATE_MAX_BYTES;
  return Math.min(Math.floor(candidate), DEFAULT_EVALUATE_MAX_BYTES);
}

function serializedSizeBytes(value) {
  const json = JSON.stringify(value);
  const text = json === undefined ? String(value) : json;
  if (typeof TextEncoder !== 'undefined') {
    return new TextEncoder().encode(text).length;
  }
  return text.length;
}

function assertSerializedSize(value, maxBytes, operation) {
  const actualBytes = serializedSizeBytes(value);
  if (actualBytes > maxBytes) {
    throw classifiedError(
      'ResultTooLarge',
      `${operation} result exceeded ${maxBytes} bytes; actual approximately ${actualBytes} bytes. Narrow the query, use maxLength, or fetch smaller chunks.`
    );
  }
}

function timeoutMs(params, fallback = 10000) {
  const candidate = params?.timeoutMs ?? params?.options?.timeoutMs;
  return typeof candidate === 'number' && Number.isFinite(candidate) && candidate >= 0 ? candidate : fallback;
}

function waitUntil(params) {
  const candidate = String(params?.waitUntil ?? params?.options?.waitUntil ?? 'commit').toLowerCase();
  return candidate === 'load' ? 'load' : 'commit';
}

function committedUrl(tab, expectedUrl, previousUrl) {
  const actual = tab.url ?? '';
  if (!actual) return false;
  if (expectedUrl && (actual === expectedUrl || actual.includes(expectedUrl))) return true;
  if (previousUrl && actual !== previousUrl && actual !== 'about:blank') return true;
  return !previousUrl && actual !== 'about:blank';
}

async function waitForNavigation(params, command) {
  const tabId = tabIdFromParams(params);
  const expectedUrl = String(params.url ?? params.options?.url ?? '');
  const previousUrl = String(params.previousUrl ?? params.options?.previousUrl ?? '');
  const timeout = timeoutMs(params, 10000);
  const wantedState = waitUntil(params);
  const start = Date.now();
  let current = await getTab(tabId);
  while (Date.now() - start <= timeout) {
    throwIfCancelled(command);
    current = await getTab(tabId);
    if (committedUrl(current, expectedUrl, previousUrl)) {
      if (wantedState !== 'load' || current.status === 'complete') {
        return { ok: true, url: current.url ?? '', status: current.status ?? '' };
      }
    }
    await delay(100, command);
  }
  const pending = current.pendingUrl ? `; pending URL is "${current.pendingUrl}"` : '';
  throw new Error(`Timed out waiting for navigation after ${timeout}ms; current URL is "${current.url ?? ''}"${pending}.`);
}

function normalizeDebuggerError(error) {
  const message = error instanceof Error ? error.message : String(error);
  if (
    message.includes('Another debugger') ||
    message.includes('Cannot access a chrome-extension:// URL') ||
    message.includes('Cannot access contents of url') ||
    message.includes('debugger')
  ) {
    return classifiedError('DebuggerUnavailable', 'Chrome debugger bridge is unavailable for this tab. Close DevTools or any open extension UI for the tab, then retry.', {
      cause: error instanceof Error ? error : undefined
    });
  }
  return error instanceof Error ? error : new Error(message);
}

async function withDebugger(tabId, task) {
  const normalizedTabId = Number(tabId);
  const previous = debuggerQueues.get(normalizedTabId) ?? Promise.resolve();
  const queued = previous.catch(() => {}).then(async () => {
    const target = { tabId: normalizedTabId };
    let attached = false;
    try {
      await chrome.debugger.attach(target, '1.3');
      attached = true;
      const send = (method, params = {}) => chrome.debugger.sendCommand(target, method, params);
      return await task(send, target);
    } catch (error) {
      throw normalizeDebuggerError(error);
    } finally {
      if (attached) {
        try {
          await chrome.debugger.detach(target);
        } catch {
          // The tab may close while the command is running.
        }
      }
    }
  });
  debuggerQueues.set(normalizedTabId, queued);
  try {
    return await queued;
  } finally {
    if (debuggerQueues.get(normalizedTabId) === queued) {
      debuggerQueues.delete(normalizedTabId);
    }
  }
}

async function executeInTab(tabId, source, arg, options = {}, command) {
  const expression = `(() => { const arguments = [${JSON.stringify(arg ?? null)}];\n${source}\n})()`;
  const timeout = timeoutMs(options, 10000);
  const operation = withDebugger(tabId, async (send) => {
    throwIfCancelled(command);
    const result = await send('Runtime.evaluate', {
      expression,
      awaitPromise: true,
      returnByValue: true
    });
    throwIfCancelled(command);
    if (result.exceptionDetails) {
      throw new Error(result.exceptionDetails.text || 'Chrome Runtime.evaluate failed.');
    }
    const value = result.result?.value ?? null;
    if (Object.prototype.hasOwnProperty.call(options, 'maxBytes')) {
      assertSerializedSize(value, normalizeEvaluateMaxBytes(options.maxBytes), 'tab.evaluate');
    }
    return value;
  });
  return Promise.race([
    operation,
    commandCancelPromise(command),
    delay(timeout, command).then(() => {
      throw classifiedError('CommandTimeout', `Chrome Runtime.evaluate timed out after ${timeout}ms.`);
    })
  ]);
}

function selectorScript(selector, action, value) {
  return `
const selector = arguments[0].selector;
const action = arguments[0].action;
const value = arguments[0].value;
const waitState = arguments[0].waitState || 'visible';
const timeoutMs = arguments[0].timeoutMs || 10000;
function textMatches(actual, expected, exact) {
  const a = String(actual || '').trim();
  const e = String(expected || '').trim();
  return exact ? a === e : a.toLowerCase().includes(e.toLowerCase());
}
function accessibleName(el) {
  return el.getAttribute('aria-label') || el.getAttribute('title') || el.innerText || el.textContent || '';
}
function visibleElements(items) {
  return items.filter((el) => {
    const box = el.getBoundingClientRect();
    return box.width > 0 && box.height > 0;
  });
}
function byDescriptor(desc) {
  if (desc.kind === 'css') return Array.from(document.querySelectorAll(desc.value));
  if (desc.kind === 'testId') return Array.from(document.querySelectorAll('[data-testid="' + CSS.escape(desc.value) + '"]'));
  if (desc.kind === 'placeholder') return Array.from(document.querySelectorAll('input,textarea')).filter((el) => textMatches(el.getAttribute('placeholder'), desc.value, desc.exact));
  if (desc.kind === 'label') {
    const labels = Array.from(document.querySelectorAll('label')).filter((el) => textMatches(el.innerText || el.textContent, desc.value, desc.exact));
    return labels.map((label) => label.control || (label.getAttribute('for') ? document.getElementById(label.getAttribute('for')) : null)).filter(Boolean);
  }
  if (desc.kind === 'role') return Array.from(document.querySelectorAll('[role],button,a,input,select,textarea')).filter((el) => {
    const role = el.getAttribute('role') || (el.tagName === 'BUTTON' ? 'button' : el.tagName === 'A' ? 'link' : '').toLowerCase();
    if (role !== String(desc.value).toLowerCase()) return false;
    return desc.name == null || textMatches(accessibleName(el), desc.name, desc.exact);
  });
  if (desc.kind === 'text') return Array.from(document.querySelectorAll('body *')).filter((el) => textMatches(el.innerText || el.textContent, desc.value, desc.exact));
  return [];
}
function applyNth(items) {
  if (!Number.isInteger(selector.nth)) return items;
  if (selector.nth === -1) return items.length > 0 ? [items[items.length - 1]] : [];
  return items[selector.nth] ? [items[selector.nth]] : [];
}
function resolveElements(visibleOnly) {
  const raw = applyNth(byDescriptor(selector));
  return visibleOnly ? visibleElements(raw) : raw;
}
let elements = resolveElements(action !== 'count' && action !== 'allTextContents');
if (action === 'count') return resolveElements(false).length;
if (action === 'allTextContents') return resolveElements(false).map((el) => el.textContent || '');
if (action === 'isVisible') return elements.length > 0;
if (action === 'waitFor') {
  const start = Date.now();
  return new Promise((resolve, reject) => {
    const tick = () => {
      const raw = resolveElements(false);
      const visible = visibleElements(raw);
      const ok = waitState === 'attached'
        ? raw.length > 0
        : waitState === 'hidden'
          ? visible.length === 0
          : waitState === 'detached'
            ? raw.length === 0
            : visible.length > 0;
      if (ok) {
        resolve({ ok: true, state: waitState });
        return;
      }
      if (Date.now() - start >= timeoutMs) {
        reject(new Error('Timed out waiting for locator state "' + waitState + '" after ' + timeoutMs + 'ms.'));
        return;
      }
      setTimeout(tick, 100);
    };
    tick();
  });
}
const el = elements[0];
if (!el) throw new Error('No matching element found.');
if (action === 'textContent') return el.textContent || '';
if (action === 'innerText') return el.innerText || el.textContent || '';
if (action === 'getAttribute') return el.getAttribute(value);
if (action === 'isEnabled') return !(el.disabled || el.getAttribute('aria-disabled') === 'true');
if (action === 'click') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.click();
  return { ok: true };
}
if (action === 'dblclick') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, view: window }));
  return { ok: true };
}
if (action === 'fill') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.focus();
  el.value = value;
  el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return { ok: true };
}
if (action === 'check') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.focus();
  if (!('checked' in el)) throw new Error('Matched element is not checkable.');
  el.checked = Boolean(value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return { ok: true };
}
if (action === 'selectOption') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.focus();
  if (el.tagName !== 'SELECT') throw new Error('Matched element is not a select element.');
  const values = Array.isArray(value) ? value : [value];
  const wanted = values.map((item) => {
    if (item && typeof item === 'object') return item.value ?? item.label ?? String(item.index ?? '');
    return String(item);
  });
  for (const option of Array.from(el.options)) {
    option.selected = wanted.includes(option.value) || wanted.includes(option.label);
  }
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return { ok: true };
}
if (action === 'type') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.focus();
  el.value = String(el.value || '') + String(value || '');
  el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
  return { ok: true };
}
if (action === 'press') {
  el.scrollIntoView({ block: 'center', inline: 'center' });
  el.focus();
  const key = String(value || '');
  el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  el.dispatchEvent(new KeyboardEvent('keyup', { key, bubbles: true, cancelable: true }));
  return { ok: true };
}
throw new Error('Unsupported locator action: ' + action);
`;
}

function selectorDescriptor(params) {
  if (params.selector && typeof params.selector === 'object' && params.selector.kind) {
    return params.selector;
  }
  const options = params.selectorOptions ?? {};
  return {
    kind: options.kind ?? 'css',
    value: options.value ?? options.text ?? options.role ?? options.testId ?? params.selector,
    name: options.name,
    exact: options.exact === true,
    nth: Number.isInteger(options.nth) ? options.nth : undefined
  };
}

async function locatorAction(params, action, command) {
  return executeInTab(tabIdFromParams(params), selectorScript(selectorDescriptor(params), action, params.value), {
    selector: selectorDescriptor(params),
    action,
    value: params.value,
    waitState: params.options?.state,
    timeoutMs: timeoutMs(params, 10000)
  }, command);
}

async function tabContent(params, contentType, command) {
  const tabId = tabIdFromParams(params);
  const value = await executeInTab(
    tabId,
    contentType === 'html'
      ? 'return document.documentElement ? document.documentElement.outerHTML : "";'
      : 'return document.body ? document.body.innerText : "";',
    null,
    { timeoutMs: timeoutMs(params, 10000) },
    command
  );
  return truncateContent(value, params.maxLength);
}

async function waitForLoadState(params, command) {
  const state = String(params.state || params.options?.state || 'load').toLowerCase();
  const timeout = timeoutMs(params, 10000);
  const expected = state === 'domcontentloaded' ? 'interactive' : 'complete';
  return executeInTab(tabIdFromParams(params), `
const expected = arguments[0].expected;
const state = arguments[0].state;
const timeoutMs = arguments[0].timeoutMs;
const ok = () => expected === 'interactive'
  ? document.readyState === 'interactive' || document.readyState === 'complete'
  : document.readyState === 'complete';
return new Promise((resolve, reject) => {
  const start = Date.now();
  const tick = () => {
    if (ok()) {
      resolve({ ok: true, state, readyState: document.readyState });
      return;
    }
    if (Date.now() - start >= timeoutMs) {
      reject(new Error('Timed out waiting for load state "' + state + '" after ' + timeoutMs + 'ms; readyState=' + document.readyState + '.'));
      return;
    }
    setTimeout(tick, 100);
  };
  tick();
});
`, { expected, state, timeoutMs: timeout }, { timeoutMs: timeout }, command);
}

async function waitForURL(params, command) {
  const tabId = tabIdFromParams(params);
  const expectedUrl = String(params.url || '');
  const timeout = timeoutMs(params, 10000);
  const start = Date.now();
  while (Date.now() - start < timeout) {
    throwIfCancelled(command);
    const tab = await getTab(tabId);
    const actual = tab.url ?? '';
    if (actual === expectedUrl || actual.includes(expectedUrl)) {
      return { ok: true, url: actual };
    }
    await delay(100, command);
  }
  const tab = await getTab(tabId);
  throw new Error(`Timed out waiting for URL "${expectedUrl}" after ${timeout}ms; current URL is "${tab.url ?? ''}".`);
}

async function temporaryTabContent(url, params, contentType, command) {
  const tab = await chrome.tabs.create({ url, active: false });
  try {
    const session = getBrowserSession(params);
    const publicInfo = publicTab(tab, session);
    await waitForLoadState({ tab: publicInfo, state: 'load', options: { timeoutMs: timeoutMs(params, 10000) } }, command);
    const current = publicTab(await getTab(tab.id), session);
    return {
      tab: current,
      title: current.title,
      url: current.url,
      content: await tabContent({ tab: current, maxLength: params.maxLength, timeoutMs: timeoutMs(params, 10000) }, contentType, command)
    };
  } finally {
    try {
      await chrome.tabs.remove(tab.id);
    } catch {
      // The temporary tab may already be closed.
    }
  }
}

async function waitForFileChooser(params, command) {
  const tabId = tabIdFromParams(params);
  const timeout = timeoutMs(params, 10000);
  return executeInTab(tabId, `
const timeoutMs = arguments[0].timeoutMs;
function findInput() {
  const active = document.activeElement;
  if (active && active.matches && active.matches('input[type="file"]')) return active;
  const visible = Array.from(document.querySelectorAll('input[type="file"]')).find((el) => {
    const box = el.getBoundingClientRect();
    return box.width > 0 && box.height > 0;
  });
  return visible || document.querySelector('input[type="file"]');
}
return new Promise((resolve, reject) => {
  const start = Date.now();
  const tick = () => {
    const input = findInput();
    if (input) {
      const token = 'dotcraft-file-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
      input.setAttribute('data-dotcraft-file-chooser-id', token);
      resolve({
        selector: '[data-dotcraft-file-chooser-id="' + token + '"]',
        multiple: input.multiple === true,
        accept: input.getAttribute('accept') || ''
      });
      return;
    }
    if (Date.now() - start >= timeoutMs) {
      reject(new Error('Timed out waiting for a file input after ' + timeoutMs + 'ms. Use a more specific input[type=file] locator or upload the file manually.'));
      return;
    }
    setTimeout(tick, 100);
  };
  tick();
});
`, { timeoutMs: timeout }, { timeoutMs: timeout }, command);
}

async function fileChooserIsMultiple(params, command) {
  return executeInTab(tabIdFromParams(params), `
const selector = arguments[0].selector;
const input = document.querySelector(selector);
if (!input) throw new Error('File input is no longer available.');
return input.multiple === true;
`, { selector: params.fileChooser?.selector || 'input[type="file"]' }, { timeoutMs: timeoutMs(params, 10000) }, command);
}

async function setFileChooserFiles(params, command) {
  const tabId = tabIdFromParams(params);
  const selector = params.fileChooser?.selector || 'input[type="file"]';
  const files = Array.isArray(params.files) ? params.files.map(String) : [];
  if (files.length === 0) throw new Error('At least one file path is required.');

  return withDebugger(tabId, async (send) => {
    throwIfCancelled(command);
    const multiple = await send('Runtime.evaluate', {
      expression: `(() => {
        const input = document.querySelector(${JSON.stringify(selector)});
        if (!input) throw new Error('File input is no longer available. Use a more specific input[type=file] locator or upload the file manually.');
        return input.multiple === true;
      })()`,
      awaitPromise: true,
      returnByValue: true
    });
    throwIfCancelled(command);
    if (multiple.exceptionDetails) {
      throw new Error(multiple.exceptionDetails.text || 'File chooser validation failed.');
    }
    if (files.length > 1 && multiple.result?.value !== true) {
      throw new Error('The selected file input does not allow multiple files.');
    }

    const documentResult = await send('DOM.getDocument', { depth: 1, pierce: true });
    const queryResult = await send('DOM.querySelector', {
      nodeId: documentResult.root.nodeId,
      selector
    });
    if (!queryResult.nodeId) {
      throw new Error('File input is no longer available. Use a more specific input[type=file] locator or upload the file manually.');
    }
    await send('DOM.setFileInputFiles', {
      nodeId: queryResult.nodeId,
      files
    });
    throwIfCancelled(command);
    return { ok: true, fileCount: files.length };
  });
}

function keyFromInput(value) {
  if (Array.isArray(value)) return value.map(String).join('+');
  return String(value ?? '');
}

async function cuaAction(params, command) {
  const tabId = tabIdFromParams(params);
  const options = params.options ?? {};
  const action = params.action;
  return withDebugger(tabId, async (send) => {
    throwIfCancelled(command);
    if (action === 'move') {
      await send('Input.dispatchMouseEvent', {
        type: 'mouseMoved',
        x: Number(options.x ?? 0),
        y: Number(options.y ?? 0)
      });
      return { ok: true };
    }
    if (action === 'click' || action === 'double_click') {
      const x = Number(options.x ?? 0);
      const y = Number(options.y ?? 0);
      const clickCount = action === 'double_click' ? 2 : 1;
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
      await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount });
      await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount });
      return { ok: true };
    }
    if (action === 'scroll') {
      await send('Input.dispatchMouseEvent', {
        type: 'mouseWheel',
        x: Number(options.x ?? 0),
        y: Number(options.y ?? 0),
        deltaX: Number(options.deltaX ?? options.scrollX ?? options.xDelta ?? 0),
        deltaY: Number(options.deltaY ?? options.scrollY ?? options.yDelta ?? 0)
      });
      return { ok: true };
    }
    if (action === 'type') {
      await send('Input.insertText', { text: String(options.text ?? '') });
      return { ok: true };
    }
    if (action === 'keypress') {
      const keys = Array.isArray(options.keys) ? options.keys : [options.key ?? options.keys];
      for (const key of keys.map(keyFromInput).filter(Boolean)) {
        await send('Input.dispatchKeyEvent', { type: 'keyDown', key });
        await send('Input.dispatchKeyEvent', { type: 'keyUp', key });
      }
      return { ok: true };
    }
    throw new Error('Unsupported CUA action: ' + action);
  });
}

function visibleDomScript(action, options) {
  return `
const action = arguments[0].action;
const options = arguments[0].options || {};
const selectors = 'a,button,input,textarea,select,[role],[data-testid]';
function allNodes() {
  return Array.from(document.querySelectorAll(selectors)).filter((el) => {
    const box = el.getBoundingClientRect();
    return box.width > 0 && box.height > 0;
  });
}
function nodeFromId(id) {
  const index = Number(String(id || '').replace(/^dom:/, ''));
  const node = allNodes()[index];
  if (!node) throw new Error('DOM CUA node is no longer visible: ' + id);
  return node;
}
if (action === 'snapshot') {
  return allNodes().slice(0, 200).map((el, index) => {
    const box = el.getBoundingClientRect();
    return {
      node_id: 'dom:' + index,
      tagName: el.tagName.toLowerCase(),
      role: el.getAttribute('role') || '',
      ariaName: el.getAttribute('aria-label') || el.getAttribute('title') || '',
      visibleText: el.innerText || el.textContent || el.value || '',
      testId: el.getAttribute('data-testid') || null,
      boundingBox: { x: box.x, y: box.y, width: box.width, height: box.height }
    };
  });
}
const node = nodeFromId(options.node_id);
if (action === 'click') {
  node.scrollIntoView({ block: 'center', inline: 'center' });
  node.click();
  return { ok: true };
}
if (action === 'double_click') {
  node.scrollIntoView({ block: 'center', inline: 'center' });
  node.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, view: window }));
  return { ok: true };
}
if (action === 'type') {
  node.scrollIntoView({ block: 'center', inline: 'center' });
  node.focus();
  if ('value' in node) node.value = String(node.value || '') + String(options.text || '');
  node.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: String(options.text || '') }));
  return { ok: true };
}
if (action === 'keypress') {
  node.focus();
  const keys = Array.isArray(options.keys) ? options.keys : [options.key || options.keys || ''];
  for (const key of keys) {
    node.dispatchEvent(new KeyboardEvent('keydown', { key: String(key), bubbles: true, cancelable: true }));
    node.dispatchEvent(new KeyboardEvent('keyup', { key: String(key), bubbles: true, cancelable: true }));
  }
  return { ok: true };
}
if (action === 'scroll') {
  const deltaX = Number(options.deltaX || options.scrollX || options.xDelta || 0);
  const deltaY = Number(options.deltaY || options.scrollY || options.yDelta || 0);
  if (typeof node.scrollBy === 'function') {
    node.scrollBy(deltaX, deltaY);
  } else {
    node.scrollLeft += deltaX;
    node.scrollTop += deltaY;
  }
  return { ok: true };
}
throw new Error('Unsupported DOM CUA action: ' + action);
`;
}

async function domCuaVisibleDom(params, command) {
  return executeInTab(tabIdFromParams(params), visibleDomScript('snapshot', params.options), {
    action: 'snapshot',
    options: params.options ?? {}
  }, { timeoutMs: timeoutMs(params, 10000) }, command);
}

async function domCuaAction(params, command) {
  return executeInTab(tabIdFromParams(params), visibleDomScript(params.action, params.options), {
    action: params.action,
    options: params.options ?? {}
  }, { timeoutMs: timeoutMs(params, 10000) }, command);
}

async function dispatchCommand(method, params, command) {
  const session = getBrowserSession(params);
  throwIfCancelled(command);
  switch (method) {
    case 'browser.nameSession':
      session.sessionName = String(params.name || 'Chrome');
      return { ok: true, name: session.sessionName };
    case 'user.openTabs':
      return allTabs(session, true);
    case 'tabs.list':
      return allTabs(session, false);
    case 'user.claimTab': {
      const tab = await getTab(tabIdFromParams(params));
      if (!session.openTabsSeenIds.has(tab.id)) {
        throw new Error('Cannot claim Chrome tab: pass a tab object or id from the current session latest user.openTabs() result.');
      }
      session.claimedTabs.add(tab.id);
      return publicTab(tab, session);
    }
    case 'tabs.selected':
      return publicTab(await selectedTab(), session);
    case 'tabs.get':
      return publicTab(await getTab(tabIdFromParams(params)), session);
    case 'tabs.content': {
      const contentType = params.contentType === 'html' || params.type === 'html' ? 'html' : 'text';
      const urls = Array.isArray(params.urls) ? params.urls.map(String) : [];
      if (urls.length > 0) {
        const results = [];
        for (const url of urls) {
          results.push(await temporaryTabContent(url, params, contentType, command));
        }
        return results;
      }
      const tab = publicTab(await selectedTab(), session);
      return tabContent({ tab, maxLength: params.maxLength, timeoutMs: timeoutMs(params, 10000) }, contentType, command);
    }
    case 'tabs.new': {
      const tab = await chrome.tabs.create({ url: params.url || 'about:blank', active: params.active !== false });
      session.createdTabs.add(tab.id);
      if (params.url) {
        await waitForNavigation({
          tab: publicTab(tab, session),
          url: String(params.url),
          previousUrl: tab.url ?? 'about:blank',
          options: params.options ?? params
        }, command);
      }
      return publicTab(await getTab(tab.id), session);
    }
    case 'tabs.finalize': {
      const keep = parseFinalizeKeep(params.keep ?? []);
      const kept = [];
      const closed = [];
      const released = [];
      const ownedIds = new Set([...session.createdTabs, ...session.claimedTabs]);
      for (const [tabId, status] of keep) {
        if (ownedIds.has(tabId)) {
          session.keptTabs.set(tabId, status);
          kept.push(tabId);
        }
      }
      for (const tabId of [...session.createdTabs]) {
        if (!keep.has(tabId)) {
          try {
            await chrome.tabs.remove(tabId);
            closed.push(tabId);
          } catch {
            // The tab may already be closed.
          }
        } else {
          session.keptTabs.set(tabId, keep.get(tabId));
        }
        session.createdTabs.delete(tabId);
      }
      for (const tabId of [...session.claimedTabs]) {
        if (!keep.has(tabId)) {
          released.push(tabId);
        } else {
          session.keptTabs.set(tabId, keep.get(tabId));
        }
        session.claimedTabs.delete(tabId);
      }
      console.info('DotCraft Chrome finalize summary', {
        sessionId: session.sessionId,
        turnId: session.turnId,
        evaluationId: session.evaluationId,
        backendId: 'chrome-extension',
        kept: kept.length,
        closed: closed.length,
        released: released.length
      });
      return { ok: true, kept, closed, released };
    }
    case 'tab.goto': {
      const tabId = tabIdFromParams(params);
      const previous = await getTab(tabId);
      await chrome.tabs.update(tabId, { url: String(params.url), active: true });
      await waitForNavigation({
        tab: { id: tabId },
        url: String(params.url),
        previousUrl: previous.url ?? '',
        options: params.options ?? params
      }, command);
      return publicTab(await getTab(tabId), session);
    }
    case 'tab.reload':
      await chrome.tabs.reload(tabIdFromParams(params));
      return { ok: true };
    case 'tab.back':
      await executeInTab(tabIdFromParams(params), 'history.back(); return true;', null, { timeoutMs: timeoutMs(params, 10000) }, command);
      return { ok: true };
    case 'tab.forward':
      await executeInTab(tabIdFromParams(params), 'history.forward(); return true;', null, { timeoutMs: timeoutMs(params, 10000) }, command);
      return { ok: true };
    case 'tab.close':
      await chrome.tabs.remove(tabIdFromParams(params));
      session.createdTabs.delete(tabIdFromParams(params));
      session.claimedTabs.delete(tabIdFromParams(params));
      return { ok: true };
    case 'tab.title':
      return (await getTab(tabIdFromParams(params))).title ?? '';
    case 'tab.url':
      return (await getTab(tabIdFromParams(params))).url ?? '';
    case 'tab.contentText':
      return tabContent(params, 'text', command);
    case 'tab.contentHtml':
      return tabContent(params, 'html', command);
    case 'tab.domSnapshot':
      return executeInTab(tabIdFromParams(params), `
return Array.from(document.querySelectorAll('a,button,input,textarea,select,[role],[data-testid]')).slice(0, 200).map((el, index) => {
  const box = el.getBoundingClientRect();
  return {
    index,
    tagName: el.tagName.toLowerCase(),
    role: el.getAttribute('role') || '',
    name: el.getAttribute('aria-label') || el.getAttribute('title') || el.innerText || el.value || '',
    text: el.innerText || el.textContent || '',
    href: el.href || undefined,
    testId: el.getAttribute('data-testid') || undefined,
    visible: box.width > 0 && box.height > 0,
    boundingBox: { x: box.x, y: box.y, width: box.width, height: box.height }
  };
});
`, null, { timeoutMs: timeoutMs(params, 10000) }, command);
    case 'tab.evaluate':
      return executeInTab(tabIdFromParams(params), params.source, params.arg, {
        maxBytes: params.maxBytes,
        timeoutMs: timeoutMs(params, 10000)
      }, command);
    case 'tab.screenshot': {
      const tab = await getTab(tabIdFromParams(params));
      const dataUrl = await chrome.tabs.captureVisibleTab(tab.windowId, { format: 'png' });
      const match = /^data:([^;,]+);base64,(.*)$/i.exec(dataUrl);
      return match ? { mediaType: match[1], dataBase64: match[2] } : dataUrl;
    }
    case 'tab.waitForFileChooser':
      return waitForFileChooser(params, command);
    case 'tab.fileChooserIsMultiple':
      return fileChooserIsMultiple(params, command);
    case 'tab.fileChooserSetFiles':
      return setFileChooserFiles(params, command);
    case 'tab.waitForLoadState':
      return waitForLoadState(params, command);
    case 'tab.waitForURL':
      return waitForURL(params, command);
    case 'tab.waitForNavigation':
      return waitForNavigation(params, command);
    case 'cua.action':
      return cuaAction(params, command);
    case 'domCua.visibleDom':
      return domCuaVisibleDom(params, command);
    case 'domCua.action':
      return domCuaAction(params, command);
    case 'locator.action':
      return locatorAction(params, params.action, command);
    case 'locator.count':
      return locatorAction(params, 'count', command);
    case 'locator.click':
      return locatorAction(params, 'click', command);
    case 'locator.dblclick':
      return locatorAction(params, 'dblclick', command);
    case 'locator.fill':
      return locatorAction(params, 'fill', command);
    case 'locator.check':
      return locatorAction(params, 'check', command);
    case 'locator.selectOption':
      return locatorAction(params, 'selectOption', command);
    case 'locator.type':
      return locatorAction(params, 'type', command);
    case 'locator.press':
      return locatorAction(params, 'press', command);
    case 'locator.textContent':
      return locatorAction(params, 'textContent', command);
    case 'locator.allTextContents':
      return locatorAction(params, 'allTextContents', command);
    case 'locator.innerText':
      return locatorAction(params, 'innerText', command);
    case 'locator.getAttribute':
      return locatorAction(params, 'getAttribute', command);
    case 'locator.isVisible':
      return locatorAction(params, 'isVisible', command);
    case 'locator.isEnabled':
      return locatorAction(params, 'isEnabled', command);
    case 'locator.waitFor':
      return locatorAction(params, 'waitFor', command);
    default:
      throw classifiedError('UnsupportedApi', `Unsupported DotCraft Chrome command: ${method}`);
  }
}

chrome.runtime.onInstalled.addListener(() => {
  console.info('DotCraft Chrome extension installed.');
});

chrome.runtime.onStartup.addListener(() => {
  try {
    connectNative();
  } catch (error) {
    console.warn(error instanceof Error ? error.message : String(error));
  }
});

chrome.action.onClicked.addListener(() => {
  connectNative();
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === 'dotcraft-popup-status') {
    try {
      connectNative();
    } catch {
      // Status has already been captured.
    }
    sendResponse({ ok: true, status: popupStatus() });
    return true;
  }
  if (message?.type === 'dotcraft-popup-open-settings') {
    try {
      connectNative().postMessage({ type: 'dotcraft-open-settings' });
      sendResponse({ ok: true, status: popupStatus() });
    } catch (error) {
      sendResponse({
        ok: false,
        error: error instanceof Error ? error.message : String(error),
        status: popupStatus()
      });
    }
    return true;
  }
  return false;
});
