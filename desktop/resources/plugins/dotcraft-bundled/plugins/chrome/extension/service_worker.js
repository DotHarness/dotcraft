const HOST_NAME = 'com.dotcraft.chromeextension';
const RELAY_PROTOCOL_VERSION = 4;
const RECONNECT_DELAY_MS = 5000;
const RECONNECT_ALARM = 'dotcraft-native-reconnect';
const RECONNECT_ALARM_PERIOD_MINUTES = 0.5;
const OVERLAY_SCRIPT = 'overlay.js';
const WEB_STORE_HOSTS = new Set(['chrome.google.com', 'chromewebstore.google.com']);
const LISTED_PROTOCOLS = new Set(['http:', 'https:', 'file:']);
const RESTRICTED_PROTOCOLS = new Set([
  'chrome:', 'chrome-untrusted:', 'chrome-search:', 'chrome-extension:', 'devtools:', 'view-source:', 'edge:', 'brave:'
]);

let nativePort = null;
let connectionState = 'disconnected';
let connectionError = null;
let reconnectTimer = null;
const heldTabs = new Set();
const overlays = new Map();
const overlaySuppressions = new Map();
const screencastTabs = new Set();
const tabGroups = new Map();

function relayError(category, message) {
  return new Error(`${category}: ${message}`);
}

function errorMessage(error) {
  return error instanceof Error ? error.message : String(error);
}

function postNative(message) {
  try {
    nativePort?.postMessage(message);
  } catch (error) {
    console.warn(errorMessage(error));
  }
}

function emit(event, params) {
  postNative({ type: 'relay-event', event, params });
}

function clearReconnect() {
  clearTimeout(reconnectTimer);
  reconnectTimer = null;
  void chrome.alarms.clear(RECONNECT_ALARM);
}

function scheduleReconnect() {
  connectionState = 'reconnecting';
  if (reconnectTimer === null) {
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      connectNative();
    }, RECONNECT_DELAY_MS);
  }
  void chrome.alarms.create(RECONNECT_ALARM, { periodInMinutes: RECONNECT_ALARM_PERIOD_MINUTES });
}

function connectNative() {
  if (nativePort) return nativePort;
  let port;
  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (error) {
    connectionError = errorMessage(error);
    scheduleReconnect();
    return null;
  }
  nativePort = port;
  connectionState = 'connected';
  connectionError = null;
  clearReconnect();
  port.onMessage.addListener((message) => {
    if (nativePort !== port) return;
    handleNativeMessage(message);
  });
  port.onDisconnect.addListener(() => {
    if (nativePort !== port) return;
    nativePort = null;
    connectionError = chrome.runtime.lastError?.message || null;
    void releaseAll();
    scheduleReconnect();
  });
  return port;
}

function handleNativeMessage(message) {
  if (message.type === 'relay-request') {
    void handleRelayRequest(message);
    return;
  }
  if (message.type === 'clients-released') {
    void releaseAll();
    return;
  }
  if (message.type === 'dotcraft-host-error') {
    connectionError = message.error || 'Native host error.';
  }
}

async function handleRelayRequest(message) {
  try {
    const result = await dispatchRelay(message.method, message.params ?? {});
    postNative({ type: 'relay-response', id: message.id, result: result ?? null });
  } catch (error) {
    postNative({ type: 'relay-response', id: message.id, error: { message: errorMessage(error) } });
  }
}

function dispatchRelay(method, params) {
  switch (method) {
    case 'hello':
      return { protocolVersion: RELAY_PROTOCOL_VERSION, extensionVersion: chrome.runtime.getManifest().version };
    case 'tabs.list':
      return listTabs();
    case 'tabs.create':
      return createTab(params);
    case 'tabs.update':
      return updateTab(params);
    case 'tabs.close':
      return closeTab(params.tabId);
    case 'debugger.attach':
      return attachDebugger(params.tabId);
    case 'debugger.detach':
      return detachDebugger(params.tabId);
    case 'debugger.send':
      return sendDebuggerCommand(params);
    case 'overlay.set':
      return setOverlay(params);
    case 'overlay.moveCursor':
      return moveCursor(params);
    default:
      throw relayError('UnsupportedApi', `Unsupported relay method: ${method}`);
  }
}

function tabUrl(tab) {
  return tab.url || tab.pendingUrl || '';
}

function parsedUrl(url) {
  try {
    return new URL(url);
  } catch {
    return null;
  }
}

function isListedTab(tab) {
  const url = parsedUrl(tabUrl(tab));
  if (!url || !LISTED_PROTOCOLS.has(url.protocol)) return false;
  return !isRestrictedUrl(url.href);
}

function isRestrictedUrl(value) {
  const url = parsedUrl(value);
  if (!url) return false;
  if (RESTRICTED_PROTOCOLS.has(url.protocol)) return true;
  return WEB_STORE_HOSTS.has(url.hostname) && (url.hostname !== 'chrome.google.com' || url.pathname.startsWith('/webstore'));
}

function tabSnapshot(tab) {
  return {
    tabId: tab.id,
    windowId: tab.windowId,
    url: tabUrl(tab),
    title: tab.title ?? '',
    loading: tab.status === 'loading',
    active: tab.active === true,
    width: tab.width,
    height: tab.height
  };
}

async function getTab(tabId) {
  try {
    return await chrome.tabs.get(tabId);
  } catch {
    throw relayError('TabStale', `Chrome tab is no longer available: ${tabId}`);
  }
}

async function listTabs() {
  const tabs = await chrome.tabs.query({});
  return tabs.filter(isListedTab).map(tabSnapshot);
}

async function groupTab(tab, title) {
  if (!title) return;
  const key = `${tab.windowId}:${title}`;
  const existing = tabGroups.get(key);
  if (existing !== undefined) {
    try {
      await chrome.tabs.group({ groupId: existing, tabIds: [tab.id] });
      return;
    } catch {
      tabGroups.delete(key);
    }
  }
  const groupId = await chrome.tabs.group({ createProperties: { windowId: tab.windowId }, tabIds: [tab.id] });
  tabGroups.set(key, groupId);
  await chrome.tabGroups.update(groupId, { title, color: 'blue' });
}

async function createTab(params) {
  const window = await chrome.windows.getLastFocused({ windowTypes: ['normal'] }).catch(() => null);
  const tab = await chrome.tabs.create({
    url: params.url ?? 'about:blank',
    active: params.active === true,
    ...(window ? { windowId: window.id } : {})
  });
  await groupTab(tab, params.groupTitle).catch((error) => console.warn(errorMessage(error)));
  return tabSnapshot(await getTab(tab.id));
}

async function updateTab(params) {
  const { tabId, url, active } = params;
  await getTab(tabId);
  await chrome.tabs.update(tabId, { ...(url ? { url } : {}), ...(typeof active === 'boolean' ? { active } : {}) });
  return tabSnapshot(await getTab(tabId));
}

async function closeTab(tabId) {
  heldTabs.delete(tabId);
  overlays.delete(tabId);
  try {
    await chrome.tabs.remove(tabId);
  } catch {
    throw relayError('TabStale', `Chrome tab is no longer available: ${tabId}`);
  }
  return { closed: true };
}

async function attachDebugger(tabId) {
  if (heldTabs.has(tabId)) return { attached: true };
  const tab = await getTab(tabId);
  if (isRestrictedUrl(tabUrl(tab))) {
    throw relayError('DebuggerUnavailable', 'Chrome does not allow extensions to control this page. Use a normal web page.');
  }
  try {
    await chrome.debugger.attach({ tabId }, '1.3');
  } catch (error) {
    const message = errorMessage(error);
    if (!message.includes('Another debugger') || !await reclaimOwnAttachment(tabId)) {
      throw relayError('DebuggerUnavailable', message.includes('Another debugger')
        ? 'Another debugger is attached to this tab. Close DevTools or other tools debugging it.'
        : message);
    }
  }
  heldTabs.add(tabId);
  return { attached: true };
}

// An attachment left by an earlier service worker instance is ours exactly when this extension can detach it.
async function reclaimOwnAttachment(tabId) {
  try {
    await chrome.debugger.detach({ tabId });
    await chrome.debugger.attach({ tabId }, '1.3');
    return true;
  } catch {
    return false;
  }
}

async function detachDebugger(tabId) {
  const held = heldTabs.delete(tabId);
  screencastTabs.delete(tabId);
  overlaySuppressions.delete(tabId);
  if (held) await chrome.debugger.detach({ tabId }).catch(() => {});
  return { detached: held };
}

async function sendDebuggerCommand(params) {
  const { tabId, method } = params;
  if (!heldTabs.has(tabId)) throw relayError('DebuggerUnavailable', `Chrome debugger is not attached to tab ${tabId}.`);
  const sessionId = params.sessionId;
  const target = sessionId ? { tabId, sessionId } : { tabId };
  const topLevel = !sessionId;
  const capture = topLevel && method === 'Page.captureScreenshot';
  if (capture) await suppressOverlay(tabId, 1);
  if (topLevel && method === 'Page.startScreencast' && !screencastTabs.has(tabId)) {
    screencastTabs.add(tabId);
    await suppressOverlay(tabId, 1);
  }
  try {
    return await chrome.debugger.sendCommand(target, method, params.params ?? {}) ?? {};
  } catch (error) {
    const message = errorMessage(error);
    if (/not attached|detached/i.test(message)) {
      heldTabs.delete(tabId);
      throw relayError('DebuggerUnavailable', message);
    }
    throw error;
  } finally {
    if (capture) await suppressOverlay(tabId, -1);
    if (topLevel && method === 'Page.stopScreencast' && screencastTabs.delete(tabId)) await suppressOverlay(tabId, -1);
  }
}

async function releaseAll() {
  const tabs = [...heldTabs];
  heldTabs.clear();
  screencastTabs.clear();
  overlaySuppressions.clear();
  const overlayTabs = [...overlays.keys()];
  overlays.clear();
  await Promise.all([
    ...tabs.map((tabId) => chrome.debugger.detach({ tabId }).catch(() => {})),
    ...overlayTabs.map((tabId) => sendOverlayMessage(tabId, { action: 'state', active: false, suppressed: false }))
  ]);
}

async function injectOverlay(tabId) {
  await chrome.scripting.executeScript({ target: { tabId, frameIds: [0] }, files: [OVERLAY_SCRIPT] });
}

async function sendOverlayMessage(tabId, message) {
  try {
    return await chrome.tabs.sendMessage(tabId, { type: 'dotcraft-overlay', ...message }, { frameId: 0 });
  } catch {
    return null;
  }
}

function overlayState(tabId) {
  const overlay = overlays.get(tabId);
  return {
    action: 'state',
    active: overlay?.active === true,
    suppressed: (overlaySuppressions.get(tabId) ?? 0) > 0,
    sessionName: overlay?.sessionName ?? ''
  };
}

async function applyOverlay(tabId) {
  if (!overlays.has(tabId)) return;
  try {
    await injectOverlay(tabId);
  } catch {
    return;
  }
  await sendOverlayMessage(tabId, overlayState(tabId));
}

async function suppressOverlay(tabId, delta) {
  const count = Math.max(0, (overlaySuppressions.get(tabId) ?? 0) + delta);
  if (count > 0) overlaySuppressions.set(tabId, count);
  else overlaySuppressions.delete(tabId);
  if (overlays.has(tabId)) await sendOverlayMessage(tabId, overlayState(tabId));
}

async function setOverlay(params) {
  const { tabId } = params;
  if (params.active === true) {
    overlays.set(tabId, { active: true, sessionName: params.sessionName ?? '' });
    await applyOverlay(tabId);
  } else if (overlays.delete(tabId)) {
    await sendOverlayMessage(tabId, { action: 'state', active: false, suppressed: false });
  }
  return { ok: true };
}

async function cursorCanAnimate(tabId) {
  try {
    const tab = await chrome.tabs.get(tabId);
    if (tab.active !== true) return false;
    const window = await chrome.windows.get(tab.windowId);
    return window.focused === true;
  } catch {
    return false;
  }
}

async function moveCursor(params) {
  const { tabId, x, y, sequence } = params;
  const animate = params.animate !== false && overlays.has(tabId) && await cursorCanAnimate(tabId);
  const delivered = overlays.has(tabId)
    ? sendOverlayMessage(tabId, { action: 'move', x, y, sequence, animate })
    : Promise.resolve(null);
  void delivered.then(() => emit('overlay.cursorArrived', { tabId, sequence }));
  return { ok: true, animate };
}

chrome.debugger.onEvent.addListener((source, method, params) => {
  if (!heldTabs.has(source.tabId)) return;
  emit('debugger.event', {
    tabId: source.tabId,
    ...(source.sessionId ? { sessionId: source.sessionId } : {}),
    method,
    params: params ?? {}
  });
});

chrome.debugger.onDetach.addListener((source, reason) => {
  if (!heldTabs.has(source.tabId)) return;
  heldTabs.delete(source.tabId);
  screencastTabs.delete(source.tabId);
  overlaySuppressions.delete(source.tabId);
  emit('debugger.detached', { tabId: source.tabId, reason });
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if ('url' in changeInfo || 'title' in changeInfo || 'status' in changeInfo) {
    emit('tabs.updated', tabSnapshot({ ...tab, id: tabId }));
  }
  if (changeInfo.status === 'complete' && overlays.has(tabId)) void applyOverlay(tabId);
});

chrome.tabs.onActivated.addListener(({ windowId }) => {
  void chrome.tabs.query({ windowId })
    .then((tabs) => {
      for (const tab of tabs) emit('tabs.updated', tabSnapshot(tab));
    })
    .catch(() => {});
});

chrome.tabs.onRemoved.addListener((tabId) => {
  heldTabs.delete(tabId);
  overlays.delete(tabId);
  overlaySuppressions.delete(tabId);
  screencastTabs.delete(tabId);
  emit('tabs.removed', { tabId });
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === RECONNECT_ALARM) connectNative();
});

chrome.runtime.onStartup.addListener(() => {
  connectNative();
});

chrome.runtime.onInstalled.addListener(() => {
  connectNative();
});

function popupStatus() {
  return {
    state: connectionState,
    connected: connectionState === 'connected',
    error: connectionState === 'connected' ? null : connectionError,
    version: chrome.runtime.getManifest().version
  };
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === 'dotcraft-popup-status') {
    connectNative();
    sendResponse({ ok: true, status: popupStatus() });
    return false;
  }
  if (message?.type === 'dotcraft-popup-open-settings') {
    const port = connectNative();
    if (port) {
      port.postMessage({ type: 'dotcraft-open-settings' });
      sendResponse({ ok: true, status: popupStatus() });
    } else {
      sendResponse({ ok: false, error: connectionError || 'DotCraft is not connected.', status: popupStatus() });
    }
    return false;
  }
  return false;
});

connectNative();
